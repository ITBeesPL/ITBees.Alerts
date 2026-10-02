using System.Linq.Expressions;
using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
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
/// An event raised without a discriminator reaches the rules of every application. The admin
/// panel and the operator panel are configured independently, so the cooldown of one of them
/// must neither silence nor unsilence the other.
/// </summary>
[TestFixture]
public class AlertPublisherThrottleTests
{
    private const string AlertKey = "device.connection.lost";
    private static readonly Guid Parking = Guid.NewGuid();

    private InMemoryRepository<AlertRule> _rules = null!;
    private InMemoryRepository<AlertOccurrence> _occurrences = null!;
    private InMemoryRepository<AlertDelivery> _deliveries = null!;
    private AlertPublisher _publisher = null!;

    [SetUp]
    public void SetUp()
    {
        _rules = new InMemoryRepository<AlertRule>();
        _occurrences = new InMemoryRepository<AlertOccurrence>();
        _deliveries = new InMemoryRepository<AlertDelivery>();

        var services = new ServiceCollection();
        AddRepository(services, _rules);
        AddRepository(services, new InMemoryRepository<AlertRuleTarget>());
        AddRepository(services, new InMemoryRepository<AlertRuleRecipient>());
        AddRepository(services, new InMemoryRepository<AlertThrottleState>());
        AddRepository(services, _occurrences);
        AddRepository(services, _deliveries);

        var catalog = new Mock<IAlertCatalog>();
        catalog.Setup(x => x.Get(AlertKey)).Returns(new AlertDefinition
        {
            Key = AlertKey,
            ScopeKind = "parking",
            DefaultSeverity = AlertSeverity.Error,
            DefaultTitleTemplate = "Brak połączenia z internetem/serwerem - {device}",
            DefaultMessageTemplate = "{message}"
        });

        _publisher = new AlertPublisher(services.BuildServiceProvider(), catalog.Object,
            NullLogger<AlertPublisher>.Instance);
    }

    [Test]
    public void An_operator_rule_without_a_cooldown_does_not_switch_the_admin_window_off()
    {
        AddRule("admin", 30);
        AddRule("operator", null);

        Raise();
        Raise();

        Assert.Multiple(() =>
        {
            Assert.That(DeliveriesTo("admin"), Is.EqualTo(1), "the repeat falls inside the admin window");
            Assert.That(DeliveriesTo("operator"), Is.EqualTo(2), "the operator asked for every repeat");
            Assert.That(_occurrences.Items, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void An_operator_cooldown_does_not_silence_the_admin_panel()
    {
        AddRule("admin", 0);
        AddRule("operator", 60);

        Raise();
        Raise();

        Assert.Multiple(() =>
        {
            Assert.That(DeliveriesTo("admin"), Is.EqualTo(2));
            Assert.That(DeliveriesTo("operator"), Is.EqualTo(1));
        });
    }

    [Test]
    public void A_repeat_every_application_suppresses_leaves_no_occurrence()
    {
        AddRule("admin", 30);
        AddRule("operator", 60);

        Raise();
        Raise();

        Assert.Multiple(() =>
        {
            Assert.That(_occurrences.Items, Has.Count.EqualTo(1));
            Assert.That(_deliveries.Items, Has.Count.EqualTo(2));
        });
    }

    private void AddRule(string discriminator, int? throttleMinutes) => _rules.Items.Add(new AlertRule
    {
        Guid = Guid.NewGuid(),
        Discriminator = discriminator,
        ScopeKind = "parking",
        AlertKey = AlertKey,
        IsPlatformRule = true,
        Enabled = true,
        Channels = AlertChannels.InApp,
        MinSeverity = AlertSeverity.Error,
        ThrottleMinutes = throttleMinutes
    });

    private void Raise() => _publisher.RaiseAsync(new AlertEvent(AlertKey, AlertScope.For("parking", Parking))
    {
        SourceId = "kasa-3",
        Values = new Dictionary<string, string>
        {
            ["device"] = "Kasa 3",
            ["message"] = "Octopark API: api.octopark.net unreachable"
        }
    }).GetAwaiter().GetResult();

    private int DeliveriesTo(string discriminator) =>
        _deliveries.Items.Count(x => x.Discriminator == discriminator);

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
