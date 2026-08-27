namespace ITBees.Alerts.Interfaces;

/// <summary>Host adapter used by the filtered in-app inbox.</summary>
public interface IAlertCurrentUserAccessor
{
    Guid GetCurrentUserGuid();
}
