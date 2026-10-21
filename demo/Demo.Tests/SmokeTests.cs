using Microsoft.AspNetCore.Mvc.Testing;

namespace Demo.Tests;

public sealed class SmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_page_has_a_stable_title()
    {
        using HttpClient client = factory.CreateClient();
        string html = await client.GetStringAsync("/");
        Assert.Contains("data-testid=\"page-title\"", html);
        Assert.Contains("Демо регистрации", html);
    }

    [Fact]
    public async Task Healthcheck_is_ready()
    {
        using HttpClient client = factory.CreateClient();
        string health = await client.GetStringAsync("/healthz");
        Assert.Contains("ready", health);
    }
}
