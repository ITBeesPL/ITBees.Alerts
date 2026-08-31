namespace ITBees.Alerts.Abstractions;

/// <summary>
/// What an alert is about. <see cref="Kind"/> is an application-defined string (Octopark uses
/// "parking" and "global"); <see cref="Id"/> is the concrete object, or null for the whole
/// system. Keeping the scope generic is what lets the same engine serve an operator panel
/// (one parking) and an admin panel (platform plus every parking).
/// </summary>
public readonly struct AlertScope : IEquatable<AlertScope>
{
    public const string GlobalKind = "global";

    private readonly string _kind;

    public AlertScope(string kind, Guid? id = null)
    {
        _kind = Normalize(kind);
        Id = id;
    }

    /// <summary>
    /// Read through a normalising getter rather than an auto-property: this is a struct, so
    /// <c>default(AlertScope)</c> and any object initialiser that sets a scope-typed property
    /// bypass the constructor. Returning null there would silently match no rule at all.
    /// </summary>
    public string Kind => _kind ?? GlobalKind;

    public Guid? Id { get; }

    private static string Normalize(string kind) =>
        string.IsNullOrWhiteSpace(kind) ? GlobalKind : kind.Trim().ToLowerInvariant();

    public static AlertScope Global => new(GlobalKind);
    public static AlertScope For(string kind, Guid id) => new(kind, id);

    public bool Equals(AlertScope other) => Kind == other.Kind && Nullable.Equals(Id, other.Id);
    public override bool Equals(object obj) => obj is AlertScope other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, Id);
    public override string ToString() => Id.HasValue ? $"{Kind}:{Id}" : Kind;
}
