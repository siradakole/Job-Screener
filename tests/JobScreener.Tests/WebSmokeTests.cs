using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobScreener.Tests;

public class WebSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Home_page_renders_the_handle_form()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("LinkedIn handle", html);
        Assert.Contains("Upload", html);
    }

    [Fact]
    public async Task Search_page_renders()
    {
        var response = await factory.CreateClient().GetAsync("/search");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Default_sources_are_the_open_ones_only()
    {
        using var scope = factory.Services.CreateScope();
        var names = scope.ServiceProvider.GetRequiredService<JobScreener.Core.Search.SearchService>().SourceNames;
        Assert.Contains("Remotive", names);
        Assert.Contains("Remote OK", names);
        Assert.Contains("Arbeitnow", names);
        Assert.DoesNotContain(names, n => n.StartsWith("LinkedIn"));
        Assert.DoesNotContain(names, n => n.StartsWith("Adzuna"));
    }

    [Fact]
    public async Task Home_page_always_offers_data_deletion()
    {
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("Delete my data", html);
    }
}
