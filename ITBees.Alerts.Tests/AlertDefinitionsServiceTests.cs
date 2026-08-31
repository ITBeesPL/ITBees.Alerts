using ITBees.Alerts.Abstractions;
using ITBees.Alerts.Catalog;
using ITBees.Alerts.Services;
using ITBees.RestfulApiControllers.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ITBees.Alerts.Tests;

[TestFixture]
public class AlertDefinitionsServiceTests
{
    private sealed class Source(params AlertDefinition[] definitions) : IAlertCatalogSource
    {
        public IEnumerable<AlertDefinition> GetDefinitions() => definitions;
    }

    private static AlertDefinitionsService Build(params string[] allowedScopeKinds)
    {
        var catalog = new AlertCatalog(
            new[]
            {
                new Source(
                    new AlertDefinition { Key = "parking.barrier.stuck", ScopeKind = "parking" },
                    new AlertDefinition { Key = "server.disk.full", ScopeKind = "global" },
                    new AlertDefinition { Key = "billing.export.failed", ScopeKind = "company" })
            },
            NullLogger<AlertCatalog>.Instance);

        return new AlertDefinitionsService(catalog, new AlertContext("octopark", allowedScopeKinds));
    }

    /// <summary>
    /// The controller declares scopeKind optional and the catalog documents null as
    /// "everything for admin", so the whole-catalog listing has to be reachable.
    /// </summary>
    [Test]
    public void Omitting_the_scope_kind_lists_the_whole_catalog()
    {
        var service = Build("parking", "global");

        var result = service.GetAll(null!);

        Assert.That(result.Select(x => x.Key),
            Is.EquivalentTo(new[] { "parking.barrier.stuck", "server.disk.full" }));
    }

    [Test]
    public void Omitting_the_scope_kind_never_widens_beyond_the_hosts_own_scopes()
    {
        var service = Build("parking");

        var result = service.GetAll("  ");

        Assert.That(result.Select(x => x.Key), Is.EquivalentTo(new[] { "parking.barrier.stuck" }),
            "a scope the application does not declare must stay invisible");
    }

    [Test]
    public void An_explicit_scope_kind_filters_the_catalog()
    {
        var service = Build("parking", "global");

        Assert.That(service.GetAll("parking").Select(x => x.Key),
            Is.EquivalentTo(new[] { "parking.barrier.stuck" }));
    }

    [Test]
    public void An_explicit_scope_kind_is_normalised_before_the_check()
    {
        var service = Build("parking");

        Assert.That(service.GetAll(" Parking ").Select(x => x.Key),
            Is.EquivalentTo(new[] { "parking.barrier.stuck" }));
    }

    [Test]
    public void A_scope_kind_the_application_does_not_declare_is_rejected()
    {
        var service = Build("parking");

        Assert.Throws<FasApiErrorException>(() => service.GetAll("company"));
    }
}
