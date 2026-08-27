using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Controllers;

[Authorize]
public class MyNotificationsController : RestfulControllerBase<MyNotificationsController>
{
    private readonly IMyNotificationsService _myNotificationsService;

    public MyNotificationsController(ILogger<MyNotificationsController> logger,
        IMyNotificationsService myNotificationsService) : base(logger)
    {
        _myNotificationsService = myNotificationsService;
    }

    [HttpGet]
    [Produces<PaginatedResult<AlertMyNotificationVm>>]
    public IActionResult Get([FromQuery] string discriminator = null, [FromQuery] string scopeKind = null,
        [FromQuery] Guid? scopeId = null, [FromQuery] bool onlyUnread = false,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null,
        [FromQuery] string sortColumn = null)
    {
        return ReturnOkResult(() => _myNotificationsService.Get(discriminator, scopeKind, scopeId,
            onlyUnread, page, pageSize));
    }
}
