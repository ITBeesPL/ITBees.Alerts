using ITBees.Alerts.DbModels;
using RestDm = ITBees.RestClient.Interfaces.RestModelMarkup.Dm;
using RestIm = ITBees.RestClient.Interfaces.RestModelMarkup.Im;
using RestUm = ITBees.RestClient.Interfaces.RestModelMarkup.Um;
using RestVm = ITBees.RestClient.Interfaces.RestModelMarkup.Vm;

namespace ITBees.Alerts.Controllers.Models;

/// <summary>
/// A person reachable by e-mail or SMS, taken from a shared address book. The bell needs no
/// contact - it goes to whoever is logged in and has access to the scope.
/// </summary>
public class AlertContactVm : RestVm
{
    public AlertContactVm() { }

    public AlertContactVm(AlertContact contact)
    {
        Guid = contact.Guid;
        Discriminator = contact.Discriminator;
        OwnerKind = contact.OwnerKind;
        OwnerId = contact.OwnerId;
        Name = contact.Name;
        Email = contact.Email;
        Phone = contact.Phone;
        SmsQuietHoursEnabled = contact.SmsQuietHoursEnabled;
        SmsQuietHoursStartMinute = contact.SmsQuietHoursStartMinute;
        SmsQuietHoursEndMinute = contact.SmsQuietHoursEndMinute;
        SmsDigestAfterQuietHours = contact.SmsDigestAfterQuietHours;
        Enabled = contact.Enabled;
        CreatedUtc = contact.CreatedUtc;
    }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }

    /// <summary>Which address book holds this person - "platform", "company", ...</summary>
    public string OwnerKind { get; set; }

    public Guid? OwnerId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public bool SmsQuietHoursEnabled { get; set; }
    public int? SmsQuietHoursStartMinute { get; set; }
    public int? SmsQuietHoursEndMinute { get; set; }
    public bool SmsDigestAfterQuietHours { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedUtc { get; set; }
}

/// <summary>
/// The scope here is only the screen the user is on - the host resolves it to the book the
/// contact is actually filed under, so the same person is reusable everywhere that book reaches.
/// </summary>
public class AlertContactIm : RestIm
{
    public AlertContactIm() { }

    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public bool SmsQuietHoursEnabled { get; set; }
    public int? SmsQuietHoursStartMinute { get; set; }
    public int? SmsQuietHoursEndMinute { get; set; }
    public bool SmsDigestAfterQuietHours { get; set; }
    public bool Enabled { get; set; } = true;
}

public class AlertContactUm : RestUm
{
    public AlertContactUm() { }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public bool SmsQuietHoursEnabled { get; set; }
    public int? SmsQuietHoursStartMinute { get; set; }
    public int? SmsQuietHoursEndMinute { get; set; }
    public bool SmsDigestAfterQuietHours { get; set; }
    public bool Enabled { get; set; } = true;
}

public class AlertContactDm : RestDm
{
    public AlertContactDm() { }

    public Guid Guid { get; set; }
    public string Discriminator { get; set; }
    public string ScopeKind { get; set; }
    public Guid? ScopeId { get; set; }
}
