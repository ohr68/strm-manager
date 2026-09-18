using NetArchTest.Rules;
using StrmManager.Modules.Catalog.Domain.Episodes;
using StrmManager.Modules.MediaProcessing.Application.Streams;
using StrmManager.Modules.MediaProcessing.Infrastructure;

namespace StrmManager.ArchitectureTests;

/// <summary>
/// MediaProcessing.Application is allowed exactly one dependency outward:
/// Catalog.Domain (for SourceAttemptResult - see ADR-011/MediaValidationResult). It must
/// never depend on Catalog.Application/Infrastructure/Presentation - that's the
/// dependency direction that lets Catalog.Application orchestrate MediaProcessing
/// (ProcessEpisodeCommandHandler), not the other way around.
/// </summary>
public class MediaProcessingBoundaryTests
{
    private const string CatalogApplicationNamespace = "StrmManager.Modules.Catalog.Application";
    private const string CatalogInfrastructureNamespace = "StrmManager.Modules.Catalog.Infrastructure";
    private const string CatalogPresentationNamespace = "StrmManager.Modules.Catalog.Presentation";
    private const string MediaProcessingApplicationNamespace = "StrmManager.Modules.MediaProcessing.Application";
    private const string MediaProcessingInfrastructureNamespace = "StrmManager.Modules.MediaProcessing.Infrastructure";

    [Fact]
    public void Domain_ShouldNotDependOn_MediaProcessing()
    {
        TestResult result = Types.InAssembly(typeof(Episode).Assembly)
            .That().ResideInNamespace("StrmManager.Modules.Catalog.Domain")
            .ShouldNot().HaveDependencyOnAny(MediaProcessingApplicationNamespace, MediaProcessingInfrastructureNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void MediaProcessingApplication_ShouldNotDependOn_CatalogApplication()
    {
        TestResult result = Types.InAssembly(typeof(StreamCandidate).Assembly)
            .That().ResideInNamespace(MediaProcessingApplicationNamespace)
            .ShouldNot().HaveDependencyOnAny(CatalogApplicationNamespace, CatalogInfrastructureNamespace, CatalogPresentationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void MediaProcessingInfrastructure_ShouldNotDependOn_Catalog()
    {
        TestResult result = Types.InAssembly(typeof(MediaProcessingModule).Assembly)
            .That().ResideInNamespace(MediaProcessingInfrastructureNamespace)
            .ShouldNot().HaveDependencyOnAny(CatalogApplicationNamespace, CatalogInfrastructureNamespace, CatalogPresentationNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void FrostStreamAndFfprobeDtos_AreNotPublic()
    {
        TestResult result = Types.InAssembly(typeof(MediaProcessingModule).Assembly)
            .That().ResideInNamespace(MediaProcessingInfrastructureNamespace)
            .And().HaveNameEndingWith("Dto")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
