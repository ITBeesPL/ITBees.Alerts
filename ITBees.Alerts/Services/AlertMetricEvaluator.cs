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
                var rule = PickRule(rules, reading.Scope);
                if (rule == null)
                    continue;

                if (rule.ComparisonOperator == AlertComparisonOperator.None || rule.ComparisonValue == null)
                    continue;

                var conditionKey = $"{definition.Key}|{rule.Guid}|{rule.ComparisonOperator}|" +
                                   $"{rule.ComparisonValue}|{reading.Scope}|{reading.SourceId}";
                if (Matches(reading.Value, rule.ComparisonOperator, rule.ComparisonValue.Value))
                {
                    matchingConditions.Add(conditionKey);
                    if (!_activeConditions.Add(conditionKey))
                        continue;

                    await _publisher.RaiseAsync(new AlertEvent(definition.Key, reading.Scope)
                        {
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

    /// <summary>A rule targeting the exact object wins over the catch-all covering every object.</summary>
    private static AlertRule PickRule(List<AlertRule> rules, AlertScope scope) =>
        rules.FirstOrDefault(x => x.ScopeKind == scope.Kind && x.Targets.Any(t => t.ScopeId == scope.Id))
        ?? rules.FirstOrDefault(x => x.ScopeKind == scope.Kind && x.Targets.Count == 0);

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
