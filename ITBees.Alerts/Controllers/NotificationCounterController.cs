using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.Interfaces;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Controllers;

[Authorize]
public class NotificationCounterController : RestfulControllerBase<NotificationCounterController>
{
    private readonly INotificationCounterService _notificationCounterService;

    public NotificationCounterController(ILogger<NotificationCounterController> logger,
        INotificationCounterService notificationCounterService) : base(logger)
    {
        _notificationCounterService = notificationCounterService;
    }

    [HttpGet]
    [Produces<AlertNotificationsCounterVm>]
    public IActionResult Get([FromQuery] string discriminator = null, [FromQuery] string scopeKind = null,
        [FromQuery] Guid? scopeId = null)
    {
        return ReturnOkResult(() => _notificationCounterService.Get(discriminator, scopeKind, scopeId));
    }
}
