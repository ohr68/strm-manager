using System.Net;

namespace StrmManager.Modules.Catalog.IntegrationTests.Infrastructure;

public class HealthEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsOkWithoutReportingUnhealthy()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The ffprobe check reports Degraded (not Unhealthy) when the executable is
        // missing from the host - expected on a dev machine without ffprobe on PATH,
        // and mapped to a 200 OK by the default health-check status-code mapping. The
        // database check must still report Healthy regardless of ffprobe's presence.
        string status = await response.Content.ReadAsStringAsync();
        Assert.True(status is "Healthy" or "Degraded", $"Expected Healthy or Degraded but got '{status}'.");
    }
}
