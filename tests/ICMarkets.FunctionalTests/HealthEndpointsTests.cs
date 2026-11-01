using System.Net;
using AwesomeAssertions;

namespace ICMarkets.FunctionalTests;

// Liveness and readiness are split so a DB outage can't get a healthy API
// restarted by the kubelet. Both must answer 200 when everything is up; the
// liveness endpoint additionally runs no checks at all (Predicate = _ => false).
public sealed class HealthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_ReturnOk_WhenEverythingIsUp(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
