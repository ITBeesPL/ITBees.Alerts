using ITBees.Alerts.Abstractions;

namespace ITBees.Alerts.Interfaces;

/// <summary>
/// Maps an alert scope onto the address books it may use. Implemented by the host application,
/// because only it knows that a parking belongs to a company, or that the platform keeps its
/// own on-call list. This is what turns contacts into a reusable pool: several scopes resolve
/// to the same book, so one contact serves many alerts.
/// </summary>
public interface IAlertContactBookResolver
{
    /// <summary>Where a contact created while configuring this scope is stored.</summary>
    AlertContactBook ResolveWritableBook(AlertScope scope);

    /// <summary>
    /// Every book whose contacts may be listed and assigned to rules of this scope. Order
    /// matters only for display - the first entry is the scope's own book.
    /// </summary>
    IReadOnlyList<AlertContactBook> ResolveVisibleBooks(AlertScope scope);

    /// <summary>
    /// Write access to a book - editing or deleting one of its contacts. Throw to deny; the
    /// REST layer turns that into an error response.
    /// </summary>
    void CheckBookWrite(AlertContactBook book);
}
