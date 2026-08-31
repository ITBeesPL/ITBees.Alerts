using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Controllers;

/// <summary>The alert catalog. Read-only: kinds live in code, subscriptions live in the database.</summary>
[Authorize]
public class AlertDefinitionsController : RestfulControllerBase<AlertDefinitionsController>
{
    private readonly IAlertDefinitionsService _alertDefinitionsService;

    public AlertDefinitionsController(ILogger<AlertDefinitionsController> logger,
        IAlertDefinitionsService alertDefinitionsService) : base(logger)
    {
        _alertDefinitionsService = alertDefinitionsService;
    }

    [HttpGet]
    [Produces<List<AlertDefinitionVm>>]
    public IActionResult Get([FromQuery] string scopeKind = null)
    {
        return ReturnOkResult(() => _alertDefinitionsService.GetAll(scopeKind));
    }
}

/// <summary>Routing rules of one scope.</summary>
[Authorize]
public class AlertRulesController : RestfulControllerBase<AlertRulesController>
{
    private readonly IAlertRulesService _alertRulesService;

    public AlertRulesController(ILogger<AlertRulesController> logger, IAlertRulesService alertRulesService)
        : base(logger)
    {
        _alertRulesService = alertRulesService;
    }

    [HttpGet]
    [Produces<List<AlertRuleVm>>]
    public IActionResult Get([FromQuery] string scopeKind, [FromQuery] Guid? scopeId = null,
        [FromQuery] string discriminator = null)
    {
        return ReturnOkResult(() => _alertRulesService.GetAll(discriminator, scopeKind, scopeId));
    }
}

[Authorize]
public class AlertRuleController : RestfulControllerBase<AlertRuleController>
{
    private readonly IAlertRulesService _alertRulesService;

    public AlertRuleController(ILogger<AlertRuleController> logger, IAlertRulesService alertRulesService)
        : base(logger)
    {
        _alertRulesService = alertRulesService;
    }

    [HttpPost]
    [Produces<AlertRuleVm>]
    public IActionResult Post(AlertRuleIm alertRuleIm)
    {
        return ReturnOkResult(() => _alertRulesService.Create(alertRuleIm));
    }

    [HttpPut]
    [Produces<AlertRuleVm>]
    public IActionResult Put(AlertRuleUm alertRuleUm)
    {
        return ReturnOkResult(() => _alertRulesService.Update(alertRuleUm));
    }

    [HttpDelete]
    public IActionResult Delete(AlertRuleDm alertRuleDm)
    {
        return ReturnOkResult(() => _alertRulesService.Delete(alertRuleDm));
    }
}

/// <summary>Address book of a scope, used by the e-mail and SMS channels.</summary>
[Authorize]
public class AlertContactsController : RestfulControllerBase<AlertContactsController>
{
    private readonly IAlertContactsService _alertContactsService;

    public AlertContactsController(ILogger<AlertContactsController> logger,
        IAlertContactsService alertContactsService) : base(logger)
    {
        _alertContactsService = alertContactsService;
    }

    [HttpGet]
    [Produces<List<AlertContactVm>>]
    public IActionResult Get([FromQuery] string scopeKind, [FromQuery] Guid? scopeId = null,
        [FromQuery] string discriminator = null)
    {
        return ReturnOkResult(() => _alertContactsService.GetAll(discriminator, scopeKind, scopeId));
    }
}

[Authorize]
public class AlertContactController : RestfulControllerBase<AlertContactController>
{
    private readonly IAlertContactsService _alertContactsService;

    public AlertContactController(ILogger<AlertContactController> logger,
        IAlertContactsService alertContactsService) : base(logger)
    {
        _alertContactsService = alertContactsService;
    }

    [HttpPost]
    [Produces<AlertContactVm>]
    public IActionResult Post(AlertContactIm alertContactIm)
    {
        return ReturnOkResult(() => _alertContactsService.Create(alertContactIm));
    }

    [HttpPut]
    [Produces<AlertContactVm>]
    public IActionResult Put(AlertContactUm alertContactUm)
    {
        return ReturnOkResult(() => _alertContactsService.Update(alertContactUm));
    }

    [HttpDelete]
    public IActionResult Delete(AlertContactDm alertContactDm)
    {
        return ReturnOkResult(() => _alertContactsService.Delete(alertContactDm));
    }
}

/// <summary>Alert occurrence history.</summary>
[Authorize]
public class AlertOccurrencesController : RestfulControllerBase<AlertOccurrencesController>
{
    private readonly IAlertHistoryService _alertHistoryService;

    public AlertOccurrencesController(ILogger<AlertOccurrencesController> logger,
        IAlertHistoryService alertHistoryService) : base(logger)
    {
        _alertHistoryService = alertHistoryService;
    }

    [HttpGet]
    [Produces<PaginatedResult<AlertOccurrenceVm>>]
    public IActionResult Get([FromQuery] string scopeKind, [FromQuery] Guid? scopeId = null,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null,
        [FromQuery] string discriminator = null)
    {
        return ReturnOkResult(() =>
            _alertHistoryService.GetOccurrences(discriminator, scopeKind, scopeId, page, pageSize));
    }
}

/// <summary>Delivery log - who was reached, on which channel, and what the provider said.</summary>
[Authorize]
public class AlertDeliveriesController : RestfulControllerBase<AlertDeliveriesController>
{
    private readonly IAlertHistoryService _alertHistoryService;

    public AlertDeliveriesController(ILogger<AlertDeliveriesController> logger,
        IAlertHistoryService alertHistoryService) : base(logger)
    {
        _alertHistoryService = alertHistoryService;
    }

    [HttpGet]
    [Produces<PaginatedResult<AlertDeliveryVm>>]
    public IActionResult Get([FromQuery] string scopeKind, [FromQuery] Guid? scopeId = null,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null,
        [FromQuery] string discriminator = null, [FromQuery] AlertDeliveryStatus? status = null)
    {
        return ReturnOkResult(() =>
            _alertHistoryService.GetDeliveries(discriminator, scopeKind, scopeId, page, pageSize, status));
    }
}

[Authorize]
public class AlertTestController : RestfulControllerBase<AlertTestController>
{
    private readonly IAlertTestService _alertTestService;

    public AlertTestController(ILogger<AlertTestController> logger, IAlertTestService alertTestService)
        : base(logger)
    {
        _alertTestService = alertTestService;
    }

    [HttpPost]
    [Produces<AlertTestResultVm>]
    public IActionResult Post(AlertTestIm alertTestIm)
    {
        return ReturnOkResult(() => _alertTestService.Send(alertTestIm));
    }
}
