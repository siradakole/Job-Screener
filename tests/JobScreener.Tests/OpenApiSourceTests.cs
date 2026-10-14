using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class OpenApiSourceTests
{
    private static SearchRequest Request(params string[] titles) =>
        new(titles, new[] { WorkType.Remote }, "Worldwide", new LookBack(24, LookBackUnit.Hours), null);

    private const string RemotiveJson = """
    {"job-count":1,"jobs":[{"id":1680495,"url":"https://remotive.com/remote-jobs/software-dev/dotnet-1680495",
    "title":".NET Developer","company_name":"Acme","candidate_required_location":"Worldwide",
    "publication_date":"2026-10-14T11:30:00","description":"<p>C# and Azure</p>"}]}
    """;

    private const string RemoteOkJson = """
    [{"last_updated":1,"legal":"Please link back"},
     {"id":"1137510","epoch":1791990000,"position":"Backend Engineer","company":"Globex","location":"",
      "tags":["c#","dotnet"],"description":"<p>Build APIs</p>","url":"https://remoteok.com/remote-jobs/1137510",
      "apply_url":"https://remoteok.com/remote-jobs/1137510"}]
    """;

    private const string ArbeitnowJson = """
    {"data":[{"slug":"dev-1","company_name":"Initech","title":"C# Developer",
    "description":"&lt;p&gt;Hello&lt;/p&gt;","remote":true,"url":"https://www.arbeitnow.com/jobs/dev-1",
    "tags":["c#"],"location":"Berlin","created_at":1791990000},
    {"slug":"dev-2","company_name":"Hooli","title":"Office Developer","description":"x","remote":false,
    "url":"https://www.arbeitnow.com/jobs/dev-2","tags":[],"location":"Munich","created_at":1791990000}],
    "links":{"next":null}}
    """;

    [Fact]
    public void Remotive_parse_maps_fields()
    {
        var job = Assert.Single(RemotiveSource.Parse(RemotiveJson));
        Assert.Equal("Remotive", job.Source);
        Assert.Equal(".NET Developer", job.Title);
        Assert.Equal("Acme", job.Company);
        Assert.Equal("Worldwide", job.Location);
        Assert.Equal(WorkType.Remote, job.WorkType);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 11, 30, 0, TimeSpan.Zero), job.PostedAt);
        Assert.Null(job.ApplicantCount);
    }

    [Fact]
    public async Task Remotive_search_requests_each_title_and_deduplicates()
    {
        var handler = FakeHandler.Json(RemotiveJson);
        var source = new RemotiveSource(new HttpClient(handler));
        var jobs = await source.SearchAsync(Request(".NET Developer", "C# Developer"), CancellationToken.None);
        Assert.Single(jobs);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("search=.NET%20Developer", handler.Requests[0]);
    }

    [Fact]
    public void RemoteOk_parse_skips_the_legal_notice_and_maps_fields()
    {
        var job = Assert.Single(RemoteOkSource.Parse(RemoteOkJson));
        Assert.Equal("Remote OK", job.Source);
        Assert.Equal("Backend Engineer", job.Title);
        Assert.Equal("Worldwide", job.Location);   // empty location means anywhere
        Assert.Equal(WorkType.Remote, job.WorkType);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791990000), job.PostedAt);
        Assert.Contains("c#", job.Description);    // tags are appended so skills can be detected
    }

    [Fact]
    public async Task RemoteOk_sends_a_user_agent()
    {
        string? agent = null;
        var handler = new FakeHandler(req =>
        {
            agent = req.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(RemoteOkJson) };
        });
        await new RemoteOkSource(new HttpClient(handler)).SearchAsync(Request("Backend"), CancellationToken.None);
        Assert.Contains("Job-Screener", agent);
    }

    [Fact]
    public void Arbeitnow_parse_maps_remote_flag_and_decodes_html()
    {
        var jobs = ArbeitnowSource.Parse(ArbeitnowJson);
        Assert.Equal(2, jobs.Count);
        Assert.Equal(WorkType.Remote, jobs[0].WorkType);
        Assert.Equal(WorkType.OnSite, jobs[1].WorkType);
        Assert.Equal("Berlin", jobs[0].Location);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791990000), jobs[0].PostedAt);
    }

    [Fact]
    public void Remote_only_request_drops_non_remote_arbeitnow_postings()
    {
        var jobs = ArbeitnowSource.Parse(ArbeitnowJson);
        var now = jobs[0].PostedAt.AddMinutes(5);
        var request = new SearchRequest(Array.Empty<string>(), new[] { WorkType.Remote }, "Worldwide",
            new LookBack(1, LookBackUnit.Hours), null);
        var kept = JobScreener.Core.Filtering.FilterEngine.Apply(jobs, request, now);
        Assert.All(kept, j => Assert.Equal(WorkType.Remote, j.WorkType));
        Assert.DoesNotContain(kept, j => j.Title == jobs[1].Title);
    }

    [Fact]
    public async Task Source_throws_on_http_error()
    {
        var source = new RemotiveSource(new HttpClient(FakeHandler.Status(System.Net.HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync(Request("x"), CancellationToken.None));
    }
}
