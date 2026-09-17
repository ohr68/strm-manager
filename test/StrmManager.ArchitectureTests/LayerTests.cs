using NetArchTest.Rules;
using StrmManager.Modules.Catalog.Application.Series.AddSeries;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.Catalog.Presentation;

namespace StrmManager.ArchitectureTests;

public class LayerTests
{
    private const string DomainNamespace = "StrmManager.Modules.Catalog.Domain";
    private const string ApplicationNamespace = "StrmManager.Modules.Catalog.Application";
    private const string InfrastructureNamespace = "StrmManager.Modules.Catalog.Infrastructure";
    private const string PresentationNamespace = "StrmManager.Modules.Catalog.Presentation";

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        TestResult result = Types.InAssembly(typeof(Episode).Assembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        TestResult result = Types.InAssembly(typeof(Episode).Assembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Presentation()
    {
        TestResult result = Types.InAssembly(typeof(Episode).Assembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOn(PresentationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        TestResult result = Types.InAssembly(typeof(AddSeriesCommand).Assembly)
            .That().ResideInNamespace(ApplicationNamespace)
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Presentation()
    {
        TestResult result = Types.InAssembly(typeof(AddSeriesCommand).Assembly)
            .That().ResideInNamespace(ApplicationNamespace)
            .ShouldNot().HaveDependencyOn(PresentationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Presentation_ShouldNotDependOn_Infrastructure()
    {
        TestResult result = Types.InAssembly(typeof(AssemblyReference).Assembly)
            .That().ResideInNamespace(PresentationNamespace)
            .ShouldNot().HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
