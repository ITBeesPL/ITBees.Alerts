using System.Linq.Expressions;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.Configuration;
using ITBees.Alerts.DbModels;
using ITBees.Alerts.Interfaces;
using ITBees.Alerts.Services;
using ITBees.Interfaces.Repository;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

/// <summary>
/// Anti-noise behaviour of the publisher: the confirmation window (a blip that clears is
/// withdrawn), the flapping guard (a condition that keeps coming back is reported anyway) and the
/// burst digest (a flood to one address is held for a single summary).
/// </summary>
[TestFixture]
public class AlertConfirmationTests
{
    private const string ConditionKey = "device.connection.lost";
    private const string EventKey = "payment.lost";
    private static readonly Guid Parking = Guid.NewGuid();

    private InMemoryRepository<AlertRule> _rules = null!;
    private InMemoryRepository<AlertRuleRecipient> _recipients = null!;
    private InMemoryRepository<AlertOccurrence> _occurrences = null!;
    private InMemoryRepository<AlertDelivery> _deliveries = null!;
    private InMemoryRepository<AlertThrottleState> _throttle = null!;
    private AlertPublisher _publisher = null!;

    [SetUp]
    public void SetUp()
    {
        _rules = new InMemoryRepository<AlertRule>();
        _recipients = new InMemoryRepository<AlertRuleRecipient>();
        _occurrences = new InMemoryRepository<AlertOccurrence>();
        _deliveries = new InMemoryRepository<AlertDelivery>();
        _throttle = new InMemoryRepository<AlertThrottleState>();

        var services = new ServiceCollection();
        AddRepository(services, _rules);
        AddRepository(services, new InMemoryRepository<AlertRuleTarget>());
        AddRepository(services, _recipients);
        AddRepository(services, _throttle);
        AddRepository(services, _occurrences);
        AddRepository(services, _deliveries);
        services.AddSingleton(AlertDeliveryOptions.Default);

        var catalog = new Mock<IAlertCatalog>();
        catalog.Setup(x => x.Get(ConditionKey)).Returns(new AlertDefinition
        {
            Key = ConditionKey,
            ScopeKind = "parking",
            DefaultSeverity = AlertSeverity.Error,
            DefaultTitleTemplate = "Brak połączenia - {device}",
            DefaultMessageTemplate = "{message}",
            ConfirmationSeconds = 120
        });
        catalog.Setup(x => x.Get(EventKey)).Returns(new AlertDefinition
        {
            Key = EventKey,
            ScopeKind = "parking",
            DefaultSeverity = AlertSeverity.Critical,
            DefaultTitleTemplate = "Zgubiona płatność - {device}",
            DefaultMessageTemplate = "{message}"
        });

        _publisher = new AlertPublisher(services.BuildServiceProvider(), catalog.Object,
            NullLogger<AlertPublisher>.Instance);
    }

    [Test]
    public void A_condition_alert_waits_out_its_confirmation_window()
    {
        AddRule(ConditionKey, throttleMinutes: null);

        Raise(ConditionKey);

        var delivery = _deliveries.Items.Single();
        Assert.That(delivery.NotBeforeUtc, Is.EqualTo(DateTime.UtcNow.AddSeconds(120)).Within(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void A_condition_that_clears_inside_the_window_leaves_no_trace()
    {
        AddRule(ConditionKey, throttleMinutes: 30);

        Raise(ConditionKey);
        Resolve(ConditionKey);

        Assert.Multiple(() =>
        {
            Assert.That(_occurrences.Items, Is.Empty, "nobody was told - the history must not show it");
            Assert.That(_deliveries.Items, Is.Empty);
            Assert.That(_throttle.Items, Is.Empty, "the cooldown must not swallow the next, real alert");
        });
    }

    [Test]
    public void After_a_withdrawn_blip_the_next_real_alert_is_not_treated_as_a_repeat()
    {
        AddRule(ConditionKey, throttleMinutes: 30);

        Raise(ConditionKey);
        Resolve(ConditionKey);
        Raise(ConditionKey);

        Assert.That(_occurrences.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_condition_that_keeps_coming_back_is_reported_at_once_as_unstable()
    {
        AddRule(ConditionKey, throttleMinutes: null);

        for (var i = 0; i < 3; i++)
        {
            Raise(ConditionKey);
            Resolve(ConditionKey);
        }

        Raise(ConditionKey);

        var occurrence = _occurrences.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(occurrence.Message, Does.Contain("Stan niestabilny"));
            Assert.That(_deliveries.Items.Single().NotBeforeUtc, Is.Null, "a flapping alert is not held");
        });

        Resolve(ConditionKey);
        Assert.That(_occurrences.Items, Has.Count.EqualTo(1), "an alert that was not held cannot be withdrawn");
    }

    [Test]
    public void Skip_confirmation_delivers_at_once()
    {
        AddRule(ConditionKey, throttleMinutes: null);

        Raise(ConditionKey, skipConfirmation: true);

        Assert.That(_deliveries.Items.Single().NotBeforeUtc, Is.Null);
    }

    [Test]
    public void A_burst_to_one_address_is_held_for_a_digest_after_the_first_alert()
    {
        var rule = AddRule(EventKey, throttleMinutes: null, AlertChannels.Email);
        AddEmailRecipient(rule, "kacper@octopark.net");

        Raise(EventKey, message: "RRN 1");
        Raise(EventKey, message: "RRN 2");
        Raise(EventKey, message: "RRN 3");

        var emails = _deliveries.Items.Where(x => x.Channel == AlertChannels.Email).OrderBy(x => x.CreatedUtc).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(emails, Has.Count.EqualTo(3));
            Assert.That(emails[0].NotBeforeUtc, Is.Null, "the first alert of a quiet period is never delayed");
            Assert.That(emails[1].NotBeforeUtc, Is.Not.Null);
            Assert.That(emails[2].NotBeforeUtc, Is.EqualTo(emails[1].NotBeforeUtc), "both wait for the same digest");
        });
    }

    [Test]
    public void Resolving_an_event_alert_without_a_window_does_nothing()
    {
        AddRule(EventKey, throttleMinutes: null);

        Raise(EventKey);
        Resolve(EventKey);

        Assert.That(_occurrences.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void Resolving_one_discriminator_does_not_withdraw_another_discriminators_alert()
    {
        AddRule(ConditionKey, throttleMinutes: 30, discriminator: "admin");
        AddRule(ConditionKey, throttleMinutes: 30, discriminator: "operator");

        Raise(ConditionKey, discriminator: null);
        Resolve(ConditionKey, "admin");

        Assert.Multiple(() =>
        {
            Assert.That(_occurrences.Items, Has.Count.EqualTo(1));
            Assert.That(_deliveries.Items, Has.Count.EqualTo(1));
            Assert.That(_deliveries.Items.Single().Discriminator, Is.EqualTo("operator"));
            Assert.That(_throttle.Items.Select(x => x.Discriminator), Is.EquivalentTo(new[] { "operator" }));
        });

        Resolve(ConditionKey, "operator");
        Assert.Multiple(() =>
        {
            Assert.That(_occurrences.Items, Is.Empty);
            Assert.That(_deliveries.Items, Is.Empty);
            Assert.That(_throttle.Items, Is.Empty);
        });
    }

    [Test]
    public void A_condition_raised_without_a_discriminator_is_reported_as_unstable_in_every_application()
    {
        // The Octopark case: device alerts reach both panels and recoveries are resolved per panel.
        AddRule(ConditionKey, throttleMinutes: 30, discriminator: "admin");
        AddRule(ConditionKey, throttleMinutes: 30, discriminator: "operator");

        for (var i = 0; i < 3; i++)
        {
            Raise(ConditionKey, discriminator: null);
            Resolve(ConditionKey, "admin");
            Resolve(ConditionKey, "operator");
        }

        Raise(ConditionKey, discriminator: null);

        Assert.Multiple(() =>
        {
            Assert.That(_occurrences.Items, Has.Count.EqualTo(1));
            Assert.That(_occurrences.Items.Single().Message, Does.Contain("Stan niestabilny"));
            Assert.That(_deliveries.Items.Select(x => x.Discriminator), Is.EquivalentTo(new[] { "admin", "operator" }));
            Assert.That(_deliveries.Items.Select(x => x.NotBeforeUtc), Is.All.Null, "a flapping alert is not held");
        });
    }

    [Test]
    public void Flapping_in_one_application_does_not_skip_the_confirmation_of_another()
    {
        AddRule(ConditionKey, throttleMinutes: null, discriminator: "admin");
        AddRule(ConditionKey, throttleMinutes: null, discriminator: "operator");

        // Only the admin side ever sees the condition clear.
        for (var i = 0; i < 3; i++)
        {
            Raise(ConditionKey, discriminator: null);
            Resolve(ConditionKey, "admin");
        }

        Raise(ConditionKey, discriminator: null);

        var latest = _occurrences.Items.OrderBy(x => x.CreatedUtc).Last();
        var admin = _deliveries.Items.Single(x => x.AlertOccurrenceGuid == latest.Guid && x.Discriminator == "admin");
        var operatorDelivery = _deliveries.Items.Single(x =>
            x.AlertOccurrenceGuid == latest.Guid && x.Discriminator == "operator");
        Assert.Multiple(() =>
        {
            Assert.That(admin.NotBeforeUtc, Is.Null);
            Assert.That(admin.Body, Does.Contain("Stan niestabilny"));
            Assert.That(operatorDelivery.NotBeforeUtc, Is.Not.Null, "the operator side still waits for confirmation");
            Assert.That(operatorDelivery.Body, Does.Not.Contain("Stan niestabilny"));
        });
    }

    [Test]
    public void An_application_without_deliveries_keeps_the_occurrence_until_it_resolves_too()
    {
        AddRule(ConditionKey, throttleMinutes: null, discriminator: "admin");
        AddRule(ConditionKey, throttleMinutes: null, channels: AlertChannels.None, discriminator: "operator");

        Raise(ConditionKey, discriminator: null);
        Resolve(ConditionKey, "admin");

        Assert.That(_occurrences.Items, Has.Count.EqualTo(1), "the operator side still holds it");

        Resolve(ConditionKey, "operator");

        Assert.That(_occurrences.Items, Is.Empty);
    }

    private AlertRule AddRule(string key, int? throttleMinutes, AlertChannels channels = AlertChannels.InApp,
        string discriminator = "admin")
    {
        var rule = new AlertRule
        {
            Guid = Guid.NewGuid(),
            Discriminator = discriminator,
            ScopeKind = "parking",
            AlertKey = key,
            IsPlatformRule = true,
            Enabled = true,
            Channels = channels,
            MinSeverity = AlertSeverity.Info,
            ThrottleMinutes = throttleMinutes
        };
        _rules.Items.Add(rule);
        return rule;
    }

    private void AddEmailRecipient(AlertRule rule, string email) => _recipients.Items.Add(new AlertRuleRecipient
    {
        AlertRuleGuid = rule.Guid,
        Channels = AlertChannels.Email,
        AlertContact = new AlertContact
        {
            Guid = Guid.NewGuid(),
            Discriminator = rule.Discriminator,
            Enabled = true,
            Email = email
        }
    });

    private void Raise(string key, bool skipConfirmation = false, string message = "gateway unreachable",
        string? discriminator = "admin") =>
        _publisher.RaiseAsync(new AlertEvent(key, AlertScope.For("parking", Parking))
        {
            Discriminator = discriminator,
            SourceId = "kasa-3",
            SkipConfirmation = skipConfirmation,
            Values = new Dictionary<string, string>
            {
                ["device"] = "Kasa 3",
                ["message"] = message
            }
        }).GetAwaiter().GetResult();

    private void Resolve(string key, string discriminator = "admin") =>
        _publisher.ResolveAsync(key, AlertScope.For("parking", Parking), "kasa-3", discriminator)
            .GetAwaiter().GetResult();

    private static void AddRepository<T>(IServiceCollection services, InMemoryRepository<T> repository)
    {
        services.AddSingleton<IReadOnlyRepository<T>>(repository);
        services.AddSingleton<IWriteOnlyRepository<T>>(repository);
    }

    private sealed class InMemoryRepository<T> : IReadOnlyRepository<T>, IWriteOnlyRepository<T>
    {
        public List<T> Items { get; } = new();

        public bool HasData(Expression<Func<T, bool>> predicate) => Items.AsQueryable().Any(predicate);

        public T GetFirst(Expression<Func<T, bool>> predicate, params Expression<Func<T, object>>[] includeProperties) =>
            Items.AsQueryable().FirstOrDefault(predicate)!;

        public ICollection<T> GetData(Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includeProperties) =>
            Items.AsQueryable().Where(predicate).ToList();

        public PaginatedResult<T> GetDataPaginated(Expression<Func<T, bool>> predicate, int page,
            int elementsPerPage, string sortColumn, SortOrder sortOrder,
            params Expression<Func<T, object>>[] includeProperties) => throw new NotSupportedException();

        public PaginatedResult<T> GetDataPaginated(Expression<Func<T, bool>> predicate, SortOptions sortOptions,
            params Expression<Func<T, object>>[] includeProperties) => throw new NotSupportedException();

        public int GetDataCount(Expression<Func<T, bool>> predicate) => Items.AsQueryable().Count(predicate);

        public IQueryable<T> GetDataQueryable(Expression<Func<T, bool>> predicate) =>
            Items.AsQueryable().Where(predicate);

        public IQueryable<T> GetDataQueryable(Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includeProperties) => Items.AsQueryable().Where(predicate);

        public ICollection<T> GetDataFromStoredProcedure(string procedureName, params object[] procedureArgument) =>
            throw new NotSupportedException();

        public ICollection<T2> Sql<T2>(string sql) where T2 : class => throw new NotSupportedException();

        public T InsertData(T entity)
        {
            Items.Add(entity);
            return entity;
        }

        public ICollection<T> InsertData(ICollection<T> includeProperty)
        {
            Items.AddRange(includeProperty);
            return includeProperty;
        }

        public ICollection<T> UpdateData(Expression<Func<T, bool>> predicate, Action<T> updateAction,
            params Expression<Func<T, object>>[] includeProperties)
        {
            var matching = Items.AsQueryable().Where(predicate).ToList();
            matching.ForEach(updateAction);
            return matching;
        }

        public int DeleteData(Expression<Func<T, bool>> predicate) => Items.RemoveAll(predicate.Compile().Invoke);

        public void DeleteData(Expression<Func<T, bool>> predicate,
            params Expression<Func<T, object>>[] includeProperties) => DeleteData(predicate);

        public void Sql(string sql) => throw new NotSupportedException();
    }
}
