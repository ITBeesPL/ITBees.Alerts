using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Services;

/// <summary>
/// Turns metric streams into alerts. Server load, disk usage or device CPU are not events,
/// therefore every enabled user-defined comparison is evaluated on a schedule. A transition
/// from false to true raises one alert; the active-condition set is intentionally in memory.
/// <para>
/// Only alerts that somebody actually subscribed to are evaluated: the rules drive the loop,
/// not the catalog.
/// </para>
/// </summary>
public class AlertMetricEvaluator : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly IAlertCatalog _catalog;
    private readonly IAlertPublisher _publisher;
    private readonly ILogger<AlertMetricEvaluator> _logger;
    private readonly HashSet<string> _activeConditions = new();
    private readonly HashSet<Guid> _reportedUnreachableRules = new();

    public AlertMetricEvaluator(IServiceProvider serviceProvider, IAlertCatalog catalog, IAlertPublisher publisher,
        ILogger<AlertMetricEvaluator> logger)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Alert metric evaluation cycle failed");
            }

            try
            {
                await Task.Delay(EvaluationInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var matchingConditions = new HashSet<string>();
        // Only definitions we actually managed to read this cycle may have their remembered
        // conditions cleared. Pruning a definition we skipped would treat a still-true condition
        // as freshly crossed on the next cycle and re-alert for it.
        var evaluatedDefinitionKeys = new HashSet<string>();
        var metricDefinitions = _catalog.All
            .Where(x => !string.IsNullOrWhiteSpace(x.MetricKey))
            .ToList();
        if (metricDefinitions.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var sources = sp.GetServices<IAlertMetricSource>().ToList();
        if (sources.Count == 0)
            return;

        var ruleRoRepo = sp.GetRequiredService<IReadOnlyRepository<AlertRule>>();

        foreach (var definition in metricDefinitions)
        {
            var rules = ruleRoRepo.GetData(x => !x.Deleted && x.Enabled && x.AlertKey == definition.Key).ToList();
            if (rules.Count == 0)
                continue;

            var ruleGuids = rules.Select(x => x.Guid).ToList();
            var targets = sp.GetRequiredService<IReadOnlyRepository<AlertRuleTarget>>()
                .GetData(x => ruleGuids.Contains(x.AlertRuleGuid))
                .ToList();
            foreach (var rule in rules)
                rule.Targets = targets.Where(x => x.AlertRuleGuid == rule.Guid).ToList();

            var source = sources.FirstOrDefault(x => x.Handles(definition.MetricKey));
            if (source == null)
                continue;

            IReadOnlyCollection<AlertMetricReading> readings;
            try
            {
                readings = await source.ReadAsync(definition.MetricKey, cancellationToken);
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested)
            {
                // One flaky source must not abort the cycle for every other definition.
                _logger.LogWarning(e, "Alert metric source for {MetricKey} failed", definition.MetricKey);
                continue;
            }

            if (readings == null || readings.Count == 0)
                continue;

            evaluatedDefinitionKeys.Add(definition.Key);

            foreach (var reading in readings)
            {
                // Every rule covering this object is evaluated against its OWN threshold. Picking
                // a single rule meant a second subscription with a lower threshold was never
                // checked, while the alert it did raise still fanned out to that rule anyway.
                foreach (var rule in rules.Where(x => x.ScopeKind == reading.Scope.Kind &&
                                                      AlertRuleCoverage.Covers(x, reading.Scope.Id)))
                {
                    if (rule.ComparisonOperator == AlertComparisonOperator.None || rule.ComparisonValue == null)
                        continue;

                    // Threshold alerts are always published at the catalog default severity, so a
                    // rule subscribing above it can never fire. Say so instead of losing the alert.
                    if (definition.DefaultSeverity < rule.MinSeverity)
                    {
                        if (_reportedUnreachableRules.Add(rule.Guid))
                            _logger.LogWarning(
                                "Alert rule {Rule} for {Key} requires {MinSeverity} but the metric is " +
                                "published as {DefaultSeverity}, so it can never fire",
                                rule.Guid, definition.Key, rule.MinSeverity, definition.DefaultSeverity);
                        continue;
                    }

                    var conditionKey = $"{definition.Key}|{rule.Guid}|{rule.ComparisonOperator}|" +
                                       $"{rule.ComparisonValue}|{reading.Scope}|{reading.SourceId}";
                    if (!Matches(reading.Value, rule.ComparisonOperator, rule.ComparisonValue.Value))
                        continue;

                    matchingConditions.Add(conditionKey);
                    if (!_activeConditions.Add(conditionKey))
                        continue;

                    // Addressed at the one rule that crossed, and at its own application - otherwise
                    // the publisher would re-match every rule of this alert kind, including those
                    // whose threshold was not reached and those of other applications.
                    await _publisher.RaiseAsync(new AlertEvent(definition.Key, reading.Scope)
                        {
                            Discriminator = rule.Discriminator,
                            RuleGuids = new[] { rule.Guid },
                            SourceId = reading.SourceId,
                            SourceName = reading.SourceName
                        }
                        .With("value", Math.Round(reading.Value, 1))
                        .With("threshold", rule.ComparisonValue.Value)
                        .With("metric", definition.MetricKey)
                        .With("source", reading.SourceName ?? reading.SourceId), cancellationToken);
                }
            }
        }

        _activeConditions.RemoveWhere(x =>
            !matchingConditions.Contains(x) && evaluatedDefinitionKeys.Contains(DefinitionKeyOf(x)));
    }

    /// <summary>The condition key is prefixed with its alert key so the cycle can prune selectively.</summary>
    private static string DefinitionKeyOf(string conditionKey)
    {
        var separator = conditionKey.IndexOf('|');
        return separator < 0 ? conditionKey : conditionKey.Substring(0, separator);
    }

    private static bool Matches(double value, AlertComparisonOperator comparisonOperator, double expected) =>
        comparisonOperator switch
        {
            AlertComparisonOperator.LessThan => value < expected,
            AlertComparisonOperator.GreaterThan => value > expected,
            AlertComparisonOperator.Equal => Math.Abs(value - expected) < 0.000001d,
            _ => false
        };

}
