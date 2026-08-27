using ITBees.Notifications.DbModels;
using RestVm = ITBees.RestClient.Interfaces.RestModelMarkup.Vm;

namespace ITBees.Alerts.Controllers.Models;

public class AlertMyNotificationVm : RestVm
{
    public AlertMyNotificationVm() { }

    public AlertMyNotificationVm(Notification notification)
    {
        Guid = notification.Guid;
        Received = notification.Received;
        Title = notification.Title;
        Message = notification.Message;
        Link = notification.Link;
        HasBeenRead = notification.HasBeenRead;
        HasBeenClicked = notification.HasBeenClicked;
    }

    public Guid Guid { get; set; }
    public DateTime Received { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public string Link { get; set; }
    public bool HasBeenRead { get; set; }
    public bool HasBeenClicked { get; set; }
}

public class AlertNotificationsCounterVm : RestVm
{
    public int UnreadMessagesCount { get; set; }
    public int TotalMessagesCount { get; set; }
}
