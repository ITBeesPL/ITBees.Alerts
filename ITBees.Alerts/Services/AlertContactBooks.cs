using ITBees.Alerts.Abstractions;
using ITBees.Alerts.DbModels;
using ITBees.Interfaces.Repository;

namespace ITBees.Alerts.Services;

/// <summary>
/// Loading contacts out of several address books at once. A book is a (kind, id) pair, and the
/// repository layer only takes one expression, so the kinds are filtered in the database and
/// the ids are matched in memory - address books are small, and this keeps the rule service and
/// the contact service reading the pool exactly the same way.
/// </summary>
internal static class AlertContactBooks
{
    public static List<AlertContact> LoadContacts(IReadOnlyRepository<AlertContact> contactRoRepo,
        IReadOnlyList<AlertContactBook> books)
    {
        if (books == null || books.Count == 0)
            return new List<AlertContact>();

        var kinds = books.Select(x => x.Kind).Distinct().ToList();

        return contactRoRepo
            .GetData(x => !x.Deleted && kinds.Contains(x.OwnerKind))
            .Where(x => books.Any(book => book.Kind == x.OwnerKind && book.Id == x.OwnerId))
            .ToList();
    }
}
