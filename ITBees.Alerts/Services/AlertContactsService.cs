using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Controllers.Models;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Interfaces.Repository;
using ITBees.RestfulApiControllers.Exceptions;

namespace ITBees.Alerts.Services;

public class AlertContactsService : IAlertContactsService
{
    private readonly IReadOnlyRepository<AlertContact> _contactRoRepo;
    private readonly IWriteOnlyRepository<AlertContact> _contactWoRepo;
    private readonly IWriteOnlyRepository<AlertRuleRecipient> _recipientWoRepo;
    private readonly IAlertScopeAuthorization _authorization;
    private readonly IAlertContactBookResolver _bookResolver;
    private readonly IAlertContext _alertContext;

    public AlertContactsService(
        IReadOnlyRepository<AlertContact> contactRoRepo,
        IWriteOnlyRepository<AlertContact> contactWoRepo,
        IWriteOnlyRepository<AlertRuleRecipient> recipientWoRepo,
        IAlertScopeAuthorization authorization,
        IAlertContactBookResolver bookResolver,
        IAlertContext alertContext)
    {
        _contactRoRepo = contactRoRepo;
        _contactWoRepo = contactWoRepo;
        _recipientWoRepo = recipientWoRepo;
        _authorization = authorization;
        _bookResolver = bookResolver;
        _alertContext = alertContext;
    }

    /// <summary>
    /// The pool a scope may pick from - its own book plus anything else the host makes visible
    /// there, so one contact can serve several parkings and the system alerts alike.
    /// </summary>
    public List<AlertContactVm> GetAll(string discriminator, string scopeKind, Guid? scopeId)
    {
        var scope = new AlertScope(scopeKind, scopeId);
        var resolvedDiscriminator = _alertContext.ResolveDiscriminator(discriminator);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckRead(scope);

        return AlertContactBooks
            .LoadContacts(_contactRoRepo, _bookResolver.ResolveVisibleBooks(scope))
            .Where(x => x.Discriminator == resolvedDiscriminator)
            .OrderBy(x => x.Name)
            .Select(x => new AlertContactVm(x))
            .ToList();
    }

    public AlertContactVm Create(AlertContactIm alertContactIm)
    {
        var scope = new AlertScope(alertContactIm.ScopeKind, alertContactIm.ScopeId);
        var discriminator = _alertContext.ResolveDiscriminator(alertContactIm.Discriminator);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckWrite(scope);
        Validate(alertContactIm.Name, alertContactIm.Email, alertContactIm.Phone,
            alertContactIm.SmsQuietHoursEnabled, alertContactIm.SmsQuietHoursStartMinute,
            alertContactIm.SmsQuietHoursEndMinute);

        // Filed under the book of the scope the user happens to be on, not under the scope
        // itself - that is what makes the entry reusable for every other scope of that book.
        var book = _bookResolver.ResolveWritableBook(scope);
        _bookResolver.CheckBookWrite(book);

        var contact = _contactWoRepo.InsertData(new AlertContact
        {
            Guid = Guid.NewGuid(),
            Discriminator = discriminator,
            OwnerKind = book.Kind,
            OwnerId = book.Id,
            Name = alertContactIm.Name?.Trim(),
            Email = alertContactIm.Email?.Trim(),
            Phone = alertContactIm.Phone?.Trim(),
            SmsQuietHoursEnabled = alertContactIm.SmsQuietHoursEnabled,
            SmsQuietHoursStartMinute = alertContactIm.SmsQuietHoursStartMinute,
            SmsQuietHoursEndMinute = alertContactIm.SmsQuietHoursEndMinute,
            SmsDigestAfterQuietHours = alertContactIm.SmsDigestAfterQuietHours,
            Enabled = alertContactIm.Enabled,
            CreatedUtc = DateTime.UtcNow
        });

        return new AlertContactVm(contact);
    }

    public AlertContactVm Update(AlertContactUm alertContactUm)
    {
        var discriminator = _alertContext.ResolveDiscriminator(alertContactUm.Discriminator);
        var contact = _contactRoRepo.GetData(x => x.Guid == alertContactUm.Guid && !x.Deleted &&
                                              x.Discriminator == discriminator).FirstOrDefault();
        if (contact == null)
            throw new FasApiErrorException("Contact not found", 404);

        CheckContactWrite(contact, alertContactUm.ScopeKind, alertContactUm.ScopeId);
        Validate(alertContactUm.Name, alertContactUm.Email, alertContactUm.Phone,
            alertContactUm.SmsQuietHoursEnabled, alertContactUm.SmsQuietHoursStartMinute,
            alertContactUm.SmsQuietHoursEndMinute);

        var updated = _contactWoRepo.UpdateData(x => x.Guid == alertContactUm.Guid, x =>
        {
            x.Name = alertContactUm.Name?.Trim();
            x.Email = alertContactUm.Email?.Trim();
            x.Phone = alertContactUm.Phone?.Trim();
            x.SmsQuietHoursEnabled = alertContactUm.SmsQuietHoursEnabled;
            x.SmsQuietHoursStartMinute = alertContactUm.SmsQuietHoursStartMinute;
            x.SmsQuietHoursEndMinute = alertContactUm.SmsQuietHoursEndMinute;
            x.SmsDigestAfterQuietHours = alertContactUm.SmsDigestAfterQuietHours;
            x.Enabled = alertContactUm.Enabled;
        }).First();

        return new AlertContactVm(updated);
    }

    public void Delete(AlertContactDm alertContactDm)
    {
        var discriminator = _alertContext.ResolveDiscriminator(alertContactDm.Discriminator);
        var contact = _contactRoRepo.GetData(x => x.Guid == alertContactDm.Guid && !x.Deleted &&
                                              x.Discriminator == discriminator).FirstOrDefault();
        if (contact == null)
            throw new FasApiErrorException("Contact not found", 404);

        CheckContactWrite(contact, alertContactDm.ScopeKind, alertContactDm.ScopeId);

        // Drop the rule links first - a deleted contact must stop receiving straight away, and
        // the soft-deleted row only survives so the delivery log still has a name to show.
        _recipientWoRepo.DeleteData(x => x.AlertContactGuid == alertContactDm.Guid);
        _contactWoRepo.UpdateData(x => x.Guid == alertContactDm.Guid, x =>
        {
            x.Deleted = true;
            x.Enabled = false;
        });
    }

    private static void Validate(string name, string email, string phone, bool smsQuietHoursEnabled,
        int? smsQuietHoursStartMinute, int? smsQuietHoursEndMinute)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new FasApiErrorException("Contact name is required", 400);

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            throw new FasApiErrorException("Give the contact an e-mail address, a phone number, or both", 400);

        if (name.Trim().Length > 200 || email?.Trim().Length > 320 || phone?.Trim().Length > 32)
            throw new FasApiErrorException("Contact name, e-mail or phone exceeds the maximum length", 400);

        if (!string.IsNullOrWhiteSpace(email) && !AlertInputValidation.IsEmailAddress(email))
            throw new FasApiErrorException("Invalid e-mail address", 400);

        if (!smsQuietHoursEnabled)
            return;

        if (string.IsNullOrWhiteSpace(phone))
            throw new FasApiErrorException("SMS quiet hours require a phone number", 400);

        if (smsQuietHoursStartMinute is < 0 or > 1439 || smsQuietHoursEndMinute is < 0 or > 1439 ||
            smsQuietHoursStartMinute == null || smsQuietHoursEndMinute == null ||
            smsQuietHoursStartMinute == smsQuietHoursEndMinute)
            throw new FasApiErrorException("SMS quiet hours require different start and end times", 400);

    }

    private void CheckContactWrite(AlertContact contact, string scopeKind, Guid? scopeId)
    {
        if (string.IsNullOrWhiteSpace(scopeKind))
            throw new FasApiErrorException("ScopeKind is required", 400);

        var scope = new AlertScope(scopeKind, scopeId);
        EnsureScopeAllowed(scope.Kind);
        _authorization.CheckWrite(scope);
        var book = new AlertContactBook(contact.OwnerKind, contact.OwnerId);
        if (!_bookResolver.ResolveVisibleBooks(scope).Any(x => x.Kind == book.Kind && x.Id == book.Id))
            throw new FasApiErrorException("Contact is not available in this scope", 403);
        _bookResolver.CheckBookWrite(book);
    }

    private void EnsureScopeAllowed(string scopeKind)
    {
        if (!_alertContext.AllowsScope(scopeKind))
            throw new FasApiErrorException($"Scope '{scopeKind}' is not available in this application", 403);
    }
}
