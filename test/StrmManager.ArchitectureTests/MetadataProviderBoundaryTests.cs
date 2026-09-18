using NetArchTest.Rules;
using StrmManager.Modules.Catalog.Application.Series.AddSeries;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Infrastructure;

namespace StrmManager.ArchitectureTests;

/// <summary>
/// Restates the layering rules from LayerTests specifically for the Cinemeta provider
/// (Phase 2) - redundant with the generic Domain/Application-vs-Infrastructure checks,
/// but the explicit naming makes "Cinemeta stays swappable" a rule that shows up by name
/// when it breaks, not just as an unrelated-looking Infrastructure dependency failure.
/// </summary>
public class MetadataProviderBoundaryTests
{
    private const string CinemetaNamespace = "StrmManager.Modules.Catalog.Infrastructure.Metadata.Cinemeta";

    [Fact]
    public void Domain_ShouldNotDependOn_Cinemeta()
    {
        TestResult result = Types.InAssembly(typeof(Episode).Assembly)
            .That().ResideInNamespace("StrmManager.Modules.Catalog.Domain")
            .ShouldNot().HaveDependencyOn(CinemetaNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Cinemeta()
    {
        TestResult result = Types.InAssembly(typeof(AddSeriesCommand).Assembly)
            .That().ResideInNamespace("StrmManager.Modules.Catalog.Application")
            .ShouldNot().HaveDependencyOn(CinemetaNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void CinemetaDtos_AreNotPublic()
    {
        TestResult result = Types.InAssembly(typeof(CatalogModule).Assembly)
            .That().ResideInNamespace(CinemetaNamespace)
            .And().HaveNameEndingWith("Dto")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
