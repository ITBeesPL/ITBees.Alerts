using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Services;

public class AlertRulesService : IAlertRulesService
{
    private readonly IReadOnlyRepository<AlertRule> _ruleRoRepo;
    private readonly IWriteOnlyRepository<AlertRule> _ruleWoRepo;
    private readonly IReadOnlyRepository<AlertRuleRecipient> _recipientRoRepo;
    private readonly IWriteOnlyRepository<AlertRuleRecipient> _recipientWoRepo;
    private readonly IReadOnlyRepository<AlertRuleTarget> _targetRoRepo;
    private readonly IWriteOnlyRepository<AlertRuleTarget> _targetWoRepo;
    private readonly IReadOnlyRepository<AlertContact> _contactRoRepo;
    private readonly IAlertScopeAuthorization _authorization;
    private readonly IAlertContactBookResolver _bookResolver;
    private readonly IAlertCatalog _catalog;
    private readonly IAlertContext _alertContext;

    public AlertRulesService(
        IReadOnlyRepository<AlertRule> ruleRoRepo,
        IWriteOnlyRepository<AlertRule> ruleWoRepo,
        IReadOnlyRepository<AlertRuleRecipient> recipientRoRepo,
        IWriteOnlyRepository<AlertRuleRecipient> recipientWoRepo,
        IReadOnlyRepository<AlertRuleTarget> targetRoRepo,
        IWriteOnlyRepository<AlertRuleTarget> targetWoRepo,
        IReadOnlyRepository<AlertContact> contactRoRepo,
        IAlertScopeAuthorization authorization,
        IAlertContactBookResolver bookResolver,
        IAlertCatalog catalog,
        IAlertContext alertContext)
    {
        _bookResolver = bookResolver;
        _ruleRoRepo = ruleRoRepo;
        _ruleWoRepo = ruleWoRepo;
        _recipientRoRepo = recipientRoRepo;
        _recipientWoRepo = recipientWoRepo;
        _targetRoRepo = targetRoRepo;
        _targetWoRepo = targetWoRepo;
        _contactRoRepo = contactRoRepo;
        _authorization = authorization;
        _catalog = catalog;
        _alertContext = alertContext;
    }

    /// <summary>
    /// Without a scope id this returns every rule of the kind, each carrying its own target
    /// list - that is the administration view, which shows all rows at once and says on each
    /// one which objects it covers. With a scope id it narrows to the rules of that one object,
    /// which is what an operator sees.
    /// </summary>
    public List<AlertRuleVm> GetAll(string requestedDiscriminator, string scopeKind, Guid? scopeId)
    {
        var scope = new AlertScope(scopeKind, scopeId);
        var discriminator = _alertContext.ResolveDiscriminator(requestedDiscriminator);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckRead(scope);

        var rules = _ruleRoRepo
            .GetData(x => !x.Deleted && x.Discriminator == discriminator &&
                          x.ScopeKind == scope.Kind)
            .ToList();

        LoadTargetsAndRecipients(rules);

        if (scopeId != null)
            rules = rules.Where(x => x.Targets.Any(target => target.ScopeId == scopeId.Value)).ToList();

        return rules.Select(x => new AlertRuleVm(x)).ToList();
    }

    private void LoadTargetsAndRecipients(List<AlertRule> rules)
    {
        var guids = rules.Select(x => x.Guid).ToList();
        var targets = guids.Count == 0
            ? new List<AlertRuleTarget>()
            : _targetRoRepo.GetData(x => guids.Contains(x.AlertRuleGuid)).ToList();
        var recipients = guids.Count == 0
            ? new List<AlertRuleRecipient>()
            : _recipientRoRepo.GetData(x => guids.Contains(x.AlertRuleGuid), x => x.AlertContact).ToList();

        foreach (var rule in rules)
        {
            rule.Targets = targets.Where(x => x.AlertRuleGuid == rule.Guid).ToList();
            rule.Recipients = recipients.Where(x => x.AlertRuleGuid == rule.Guid).ToList();
        }
    }

    public AlertRuleVm Create(AlertRuleIm alertRuleIm)
    {
        var discriminator = _alertContext.ResolveDiscriminator(alertRuleIm.Discriminator);
        var targets = NormalizeTargets(alertRuleIm.ScopeIds);
        var kind = new AlertScope(alertRuleIm.ScopeKind).Kind;
        EnsureScopeAllowed(kind);
        var definition = RequireDefinition(alertRuleIm.AlertKey, new AlertScope(kind));
        AuthorizeWrite(kind, targets);
        ValidateCondition(definition, alertRuleIm.ComparisonOperator, alertRuleIm.ComparisonValue);
        ValidateRecipients(discriminator, kind, targets, alertRuleIm.Recipients);

        // Several rules may share one alert kind - "CPU > 90%" on two parkings and "> 70%" on a
        // third is a legitimate setup, and duplicate deliveries are collapsed at send time.
        var now = DateTime.UtcNow;
        var rule = _ruleWoRepo.InsertData(new AlertRule
        {
            Guid = Guid.NewGuid(),
            Discriminator = discriminator,
            ScopeKind = kind,
            AlertKey = definition.Key,
            IsPlatformRule = targets.Count == 0,
            Enabled = alertRuleIm.Enabled,
            Channels = alertRuleIm.Channels,
            MinSeverity = alertRuleIm.MinSeverity,
            Description = NormalizeDescription(alertRuleIm.Description),
            ComparisonOperator = alertRuleIm.ComparisonOperator,
            ComparisonValue = alertRuleIm.ComparisonValue,
            CustomMessage = alertRuleIm.CustomMessage,
            CreatedUtc = now,
            UpdatedUtc = now
        });

        ReplaceTargets(rule.Guid, targets);
        ReplaceRecipients(rule.Guid, discriminator, kind, targets, alertRuleIm.Recipients);
        return BuildVm(rule);
    }

    public AlertRuleVm Update(AlertRuleUm alertRuleUm)
    {
        var discriminator = _alertContext.ResolveDiscriminator(alertRuleUm.Discriminator);
        var rule = _ruleRoRepo.GetData(x => x.Guid == alertRuleUm.Guid && !x.Deleted &&
                                        x.Discriminator == discriminator).FirstOrDefault();
        if (rule == null)
            throw new FasApiErrorException("Alert rule not found", 404);

        var currentTargets = _targetRoRepo.GetData(x => x.AlertRuleGuid == rule.Guid)
            .Select(x => x.ScopeId).ToList();
        var targets = NormalizeTargets(alertRuleUm.ScopeIds);

        // Both sides have to be allowed - otherwise a rule could be dragged into, or away from,
        // a parking the caller does not administer.
        AuthorizeWrite(rule.ScopeKind, currentTargets);
        AuthorizeWrite(rule.ScopeKind, targets);
        var alertKey = string.IsNullOrWhiteSpace(alertRuleUm.AlertKey) ? rule.AlertKey : alertRuleUm.AlertKey;
        var definition = RequireDefinition(alertKey, new AlertScope(rule.ScopeKind));
        ValidateCondition(definition, alertRuleUm.ComparisonOperator, alertRuleUm.ComparisonValue);
        ValidateRecipients(discriminator, rule.ScopeKind, targets, alertRuleUm.Recipients);

        // Build the response from the write repository result - the read repository context still
        // tracks the pre-update instance, so re-reading it there would hand back the old values.
        var updatedRule = _ruleWoRepo.UpdateData(x => x.Guid == alertRuleUm.Guid, x =>
        {
            x.AlertKey = definition.Key;
            x.Enabled = alertRuleUm.Enabled;
            x.Channels = alertRuleUm.Channels;
            x.MinSeverity = alertRuleUm.MinSeverity;
            x.Description = NormalizeDescription(alertRuleUm.Description);
            x.ComparisonOperator = alertRuleUm.ComparisonOperator;
            x.ComparisonValue = alertRuleUm.ComparisonValue;
            x.CustomMessage = alertRuleUm.CustomMessage;
            x.IsPlatformRule = targets.Count == 0;
            x.UpdatedUtc = DateTime.UtcNow;
        }).First();

        ReplaceTargets(rule.Guid, targets);
        ReplaceRecipients(rule.Guid, discriminator, rule.ScopeKind, targets, alertRuleUm.Recipients);
        return BuildVm(updatedRule);
    }

    public void Delete(AlertRuleDm alertRuleDm)
    {
        var discriminator = _alertContext.ResolveDiscriminator(alertRuleDm.Discriminator);
        var rule = _ruleRoRepo.GetData(x => x.Guid == alertRuleDm.Guid && !x.Deleted &&
                                        x.Discriminator == discriminator).FirstOrDefault();
        if (rule == null)
            throw new FasApiErrorException("Alert rule not found", 404);

        AuthorizeWrite(rule.ScopeKind,
            _targetRoRepo.GetData(x => x.AlertRuleGuid == rule.Guid).Select(x => x.ScopeId).ToList());

        // Soft delete keeps the delivery history pointing at something meaningful.
        _ruleWoRepo.UpdateData(x => x.Guid == alertRuleDm.Guid, x =>
        {
            x.Deleted = true;
            x.Enabled = false;
            x.UpdatedUtc = DateTime.UtcNow;
        });
    }

    // ---- internals ----

    private static void ValidateCondition(AlertDefinition definition, AlertComparisonOperator comparisonOperator,
        double? comparisonValue)
    {
        var numeric = !string.IsNullOrWhiteSpace(definition.MetricKey);
        if (!numeric)
        {
            if (comparisonOperator != AlertComparisonOperator.None || comparisonValue != null)
                throw new FasApiErrorException("This alert type does not support a numeric condition", 400);
            return;
        }

        if (comparisonOperator == AlertComparisonOperator.None || comparisonValue == null ||
            double.IsNaN(comparisonValue.Value) || double.IsInfinity(comparisonValue.Value))
            throw new FasApiErrorException("A comparison operator and numeric value are required", 400);
    }

    private static string NormalizeDescription(string description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private AlertDefinition RequireDefinition(string alertKey, AlertScope scope)
    {
        var definition = _catalog.Get(alertKey);
        if (definition == null)
            throw new FasApiErrorException($"Unknown alert key {alertKey}", 400);

        if (!string.Equals(definition.ScopeKind, scope.Kind, StringComparison.OrdinalIgnoreCase))
            throw new FasApiErrorException(
                $"Alert {alertKey} belongs to scope '{definition.ScopeKind}', not '{scope.Kind}'", 400);

        return definition;
    }

    private static List<Guid> NormalizeTargets(List<Guid> scopeIds) => scopeIds == null
        ? new List<Guid>()
        : scopeIds.Where(x => x != Guid.Empty).Distinct().ToList();

    /// <summary>
    /// A rule with no targets covers every object of its kind, so only the platform
    /// administrator may create or change one - otherwise a single operator could subscribe
    /// themselves to every parking on the platform. A targeted rule needs write access to each
    /// object it touches.
    /// </summary>
    private void AuthorizeWrite(string scopeKind, List<Guid> targets)
    {
        if (targets.Count == 0)
        {
            if (!_authorization.IsPlatformAdministrator())
                throw new FasApiErrorException("Only a platform administrator can manage platform-wide alert rules",
                    403);

            _authorization.CheckWrite(new AlertScope(scopeKind));
            return;
        }

        foreach (var target in targets)
            _authorization.CheckWrite(new AlertScope(scopeKind, target));
    }

    private void ReplaceTargets(Guid ruleGuid, List<Guid> targets)
    {
        _targetWoRepo.DeleteData(x => x.AlertRuleGuid == ruleGuid);
        if (targets.Count == 0)
            return;

        _targetWoRepo.InsertData(targets
            .Select(x => new AlertRuleTarget { Guid = Guid.NewGuid(), AlertRuleGuid = ruleGuid, ScopeId = x })
            .ToList());
    }

    private void ReplaceRecipients(Guid ruleGuid, string discriminator, string scopeKind, List<Guid> targets,
        List<AlertRuleRecipientIm> recipients)
    {
        if (recipients == null || recipients.Count == 0)
        {
            _recipientWoRepo.DeleteData(x => x.AlertRuleGuid == ruleGuid);
            return;
        }

        var validContactGuids = ResolveAssignableContactGuids(discriminator, scopeKind, targets, recipients);

        _recipientWoRepo.DeleteData(x => x.AlertRuleGuid == ruleGuid);

        var rows = recipients
            .Where(x => validContactGuids.Contains(x.AlertContactGuid))
            .GroupBy(x => x.AlertContactGuid)
            .Select(g => new AlertRuleRecipient
            {
                Guid = Guid.NewGuid(),
                AlertRuleGuid = ruleGuid,
                AlertContactGuid = g.Key,
                Channels = g.First().Channels
            })
            .ToList();

        if (rows.Count > 0)
            _recipientWoRepo.InsertData(rows);
    }

    private void ValidateRecipients(string discriminator, string scopeKind, List<Guid> targets,
        List<AlertRuleRecipientIm> recipients)
    {
        if (recipients == null || recipients.Count == 0)
            return;

        ResolveAssignableContactGuids(discriminator, scopeKind, targets, recipients);
    }

    /// <summary>
    /// Recipients are picked from a pool, so the only thing to prove is that every one of them
    /// sits in an address book the rule can reach - the union of the books of everything it
    /// targets. The same contact may well be assigned to rules of other parkings at the same time.
    /// </summary>
    private HashSet<Guid> ResolveAssignableContactGuids(string discriminator, string scopeKind, List<Guid> targets,
        List<AlertRuleRecipientIm> recipients)
    {
        var contactGuids = recipients
            .Where(x => x.AlertContactGuid != Guid.Empty)
            .Select(x => x.AlertContactGuid)
            .Distinct()
            .ToList();

        if (contactGuids.Count == 0)
            return new HashSet<Guid>();

        var scopes = targets.Count == 0
            ? new List<AlertScope> { new(scopeKind) }
            : targets.Select(x => new AlertScope(scopeKind, x)).ToList();
        var books = scopes.SelectMany(x => _bookResolver.ResolveVisibleBooks(x)).Distinct().ToList();

        var assignable = AlertContactBooks
            .LoadContacts(_contactRoRepo, books)
            .Where(x => x.Discriminator == discriminator && x.Enabled &&
                        contactGuids.Contains(x.Guid))
            .Select(x => x.Guid)
            .ToHashSet();

        if (assignable.Count != contactGuids.Count)
            throw new FasApiErrorException(
                "Every recipient must be an enabled contact from an address book available in this scope", 400);

        return assignable;
    }

    private AlertRuleVm BuildVm(AlertRule rule)
    {
        var vm = new AlertRuleVm(rule);
        vm.ScopeIds = _targetRoRepo.GetData(x => x.AlertRuleGuid == rule.Guid).Select(x => x.ScopeId).ToList();
        vm.Recipients = _recipientRoRepo
            .GetData(x => x.AlertRuleGuid == rule.Guid, x => x.AlertContact)
            .Select(x => new AlertRuleRecipientVm(x))
            .ToList();
        return vm;
    }

    private void EnsureScopeAllowed(string scopeKind)
    {
        if (!_alertContext.AllowsScope(scopeKind))
            throw new FasApiErrorException($"Scope '{scopeKind}' is not available in this application", 403);
    }
}
