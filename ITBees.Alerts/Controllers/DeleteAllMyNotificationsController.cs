using ITBees.Alerts.Interfaces;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Alerts.Controllers;

[Authorize]
public class DeleteAllMyNotificationsController : RestfulControllerBase<DeleteAllMyNotificationsController>
{
    private readonly IDeleteAllMyNotificationsService _deleteAllMyNotificationsService;

    public DeleteAllMyNotificationsController(ILogger<DeleteAllMyNotificationsController> logger,
        IDeleteAllMyNotificationsService deleteAllMyNotificationsService) : base(logger)
    {
        _deleteAllMyNotificationsService = deleteAllMyNotificationsService;
    }

    [HttpDelete]
    public IActionResult Delete([FromQuery] string discriminator = null, [FromQuery] string scopeKind = null,
        [FromQuery] Guid? scopeId = null)
    {
        return ReturnOkResult(() =>
            _deleteAllMyNotificationsService.Delete(discriminator, scopeKind, scopeId));
    }
}
