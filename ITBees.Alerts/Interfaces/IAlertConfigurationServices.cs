using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Interfaces.Repository;

namespace ITBees.Alerts.Interfaces;

/// <summary>Reads the alert catalog for the settings screen.</summary>
public interface IAlertDefinitionsService
{
    List<AlertDefinitionVm> GetAll(string scopeKind);
}

/// <summary>
/// CRUD over the routing rules. Every method authorises through
/// <see cref="IAlertScopeAuthorization"/>, so an operator can never reach another parking.
/// </summary>
public interface IAlertRulesService
{
    List<AlertRuleVm> GetAll(string discriminator, string scopeKind, Guid? scopeId);
    AlertRuleVm Create(AlertRuleIm alertRuleIm);
    AlertRuleVm Update(AlertRuleUm alertRuleUm);
    void Delete(AlertRuleDm alertRuleDm);
}

/// <summary>CRUD over the e-mail / SMS address book of a scope.</summary>
public interface IAlertContactsService
{
    List<AlertContactVm> GetAll(string discriminator, string scopeKind, Guid? scopeId);
    AlertContactVm Create(AlertContactIm alertContactIm);
    AlertContactVm Update(AlertContactUm alertContactUm);
    void Delete(AlertContactDm alertContactDm);
}

/// <summary>What happened and whether it got through.</summary>
public interface IAlertHistoryService
{
    PaginatedResult<AlertOccurrenceVm> GetOccurrences(string discriminator, string scopeKind, Guid? scopeId,
        int? page, int? pageSize);

    /// <summary>
    /// <paramref name="status"/> is what makes deliveries stranded in
    /// <see cref="AlertDeliveryStatus.Processing"/> - claimed by a dispatcher that then crashed -
    /// findable instead of buried in the full log.
    /// </summary>
    PaginatedResult<AlertDeliveryVm> GetDeliveries(string discriminator, string scopeKind, Guid? scopeId,
        int? page, int? pageSize, AlertDeliveryStatus? status = null);
}

/// <summary>Sends a sample alert through the rule that is being configured.</summary>
public interface IAlertTestService
{
    AlertTestResultVm Send(AlertTestIm alertTestIm);
}
