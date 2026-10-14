using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class AdzunaSourceTests
{
    private const string Json = """
    {"results":[
     {"title":"C# Developer","company":{"display_name":"Acme"},"location":{"display_name":"London"},
      "created":"2026-10-14T11:00:00Z","redirect_url":"https://adzuna.example/1","description":"Fully remote role"},
     {"title":"Java Developer","company":{"display_name":"Globex"},"location":{"display_name":"Leeds"},
      "created":"2026-10-14T10:00:00Z","redirect_url":"https://adzuna.example/2","description":"Office based"}]}
    """;

    [Fact]
    public void Parse_maps_fields_and_detects_remote_wording()
    {
        var jobs = AdzunaSource.Parse(Json);
        Assert.Equal(2, jobs.Count);
        Assert.Equal("Adzuna", jobs[0].Source);
        Assert.Equal("London", jobs[0].Location);
        Assert.Equal(WorkType.Remote, jobs[0].WorkType);
        Assert.Equal(WorkType.Unknown, jobs[1].WorkType);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.Zero), jobs[0].PostedAt);
    }

    [Fact]
    public async Task Search_builds_the_url_with_keys_days_and_location()
    {
        var handler = FakeHandler.Json(Json);
        var source = new AdzunaSource(new HttpClient(handler), new AdzunaOptions("id1", "key1", "us"));
        var request = new SearchRequest(new[] { "C# Developer" }, Array.Empty<WorkType>(), "Austin",
            new LookBack(2, LookBackUnit.Days), null);
        await source.SearchAsync(request, CancellationToken.None);
        var url = Assert.Single(handler.Requests);
        Assert.Contains("/jobs/us/search/1", url);
        Assert.Contains("app_id=id1", url);
        Assert.Contains("max_days_old=2", url);
        Assert.Contains("where=Austin", url);
    }

    [Fact]
    public async Task Search_omits_where_for_worldwide_and_rounds_hours_up_to_a_day()
    {
        var handler = FakeHandler.Json(Json);
        var source = new AdzunaSource(new HttpClient(handler), new AdzunaOptions("id1", "key1"));
        var request = new SearchRequest(new[] { "C#" }, Array.Empty<WorkType>(), "Worldwide",
            new LookBack(3, LookBackUnit.Hours), null);
        await source.SearchAsync(request, CancellationToken.None);
        var url = Assert.Single(handler.Requests);
        Assert.DoesNotContain("where=", url);
        Assert.Contains("max_days_old=1", url);
    }
}
