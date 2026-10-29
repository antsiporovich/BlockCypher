using System.Net;
using AwesomeAssertions;

namespace ICMarkets.FunctionalTests;

public sealed class HealthAndCorsTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public HealthAndCorsTests(ApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_Returns200AndHealthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }

    [Fact]
    public async Task Cors_Preflight_ReturnsConfiguredHeaders()
    {
        // appsettings.json ships Cors:AllowedOrigins = [] -> AllowAnyOrigin(),
        // so any Origin should be echoed back as allowed on a preflight.
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/blockchains/networks");
        request.Headers.Add("Origin", "https://example.test");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeTrue();
    }
}
