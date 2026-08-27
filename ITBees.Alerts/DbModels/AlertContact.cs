namespace ITBees.Alerts.DbModels;

/// <summary>
/// An address book entry for the e-mail and SMS channels. A contact belongs to a book (its
/// owner), not to a single alert scope - the same person is entered once and then assigned to
/// as many rules as needed, across every scope that book serves. The in-app bell does not use
/// contacts at all — it reaches logged-in users of the scope, so a person who only needs the
/// bell never has to be entered here.
/// </summary>
public class AlertContact
{
    public Guid Guid { get; set; }

    /// <summary>Host application that owns this address-book entry.</summary>
    public string Discriminator { get; set; }

    /// <summary>Book owner kind - "platform", "company", whatever the host resolves.</summary>
    public string OwnerKind { get; set; }

    /// <summary>The owning object, or null for the platform-wide address book.</summary>
    public Guid? OwnerId { get; set; }

    public string Name { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }

    public bool SmsQuietHoursEnabled { get; set; }
    public int? SmsQuietHoursStartMinute { get; set; }
    public int? SmsQuietHoursEndMinute { get; set; }
    public bool SmsDigestAfterQuietHours { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public bool Deleted { get; set; }
}
