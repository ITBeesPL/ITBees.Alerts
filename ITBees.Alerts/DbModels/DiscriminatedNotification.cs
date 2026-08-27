using ITBees.Notifications.DbModels;

namespace ITBees.Notifications.DbModels;

/// <summary>
/// Extends the notification model with reusable routing data. The discriminator has no built-in
/// meaning; the host decides whether it represents a project, role, tenant or another context.
/// </summary>
public class DiscriminatedNotification : Notification
{
    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
}
