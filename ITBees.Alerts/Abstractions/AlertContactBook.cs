namespace ITBees.Alerts.Abstractions;

/// <summary>
/// The address book a contact belongs to. Deliberately separate from <see cref="AlertScope"/>:
/// a scope is what an alert is about (one parking, the whole system), while a book is who owns
/// the person's data. One book usually serves many scopes - a company book is shared by every
/// parking of that company, so a contact is entered once and then assigned to any number of
/// rules. <see cref="Kind"/> is application-defined ("platform", "company"), <see cref="Id"/>
/// is the owning object or null for the platform-wide book.
/// </summary>
public readonly struct AlertContactBook : IEquatable<AlertContactBook>
{
    public const string PlatformKind = "platform";

    private readonly string _kind;

    public AlertContactBook(string kind, Guid? id = null)
    {
        _kind = Normalize(kind);
        Id = id;
    }

    /// <summary>Normalising getter, so <c>default(AlertContactBook)</c> still names a real book.</summary>
    public string Kind => _kind ?? PlatformKind;

    public Guid? Id { get; }

    private static string Normalize(string kind) =>
        string.IsNullOrWhiteSpace(kind) ? PlatformKind : kind.Trim().ToLowerInvariant();

    public static AlertContactBook Platform => new(PlatformKind);
    public static AlertContactBook For(string kind, Guid id) => new(kind, id);

    public bool Equals(AlertContactBook other) => Kind == other.Kind && Nullable.Equals(Id, other.Id);
    public override bool Equals(object obj) => obj is AlertContactBook other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Id);
    public override string ToString() => Id.HasValue ? $"{Kind}:{Id}" : Kind;
}
