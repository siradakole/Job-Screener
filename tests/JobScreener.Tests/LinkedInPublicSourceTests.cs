using System.Net;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class LinkedInPublicSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ListHtml = """
    <ul><li><div class="base-card" data-entity-urn="urn:li:jobPosting:123">
      <a class="base-card__full-link" href="https://www.linkedin.com/jobs/view/123?trk=x"></a>
      <h3 class="base-search-card__title"> .NET Developer </h3>
      <h4 class="base-search-card__subtitle"><a>Acme</a></h4>
      <span class="job-search-card__location">Remote</span>
      <time class="job-search-card__listdate" datetime="2026-10-14"></time>
    </div></li></ul>
    """;

    private static SearchRequest Request(int? max = null) =>
        new(new[] { ".NET Developer" }, new[] { WorkType.Remote }, "Worldwide", new LookBack(1, LookBackUnit.Hours), max);

    private static LinkedInPublicSource Source(FakeHandler handler) =>
        new(new HttpClient(handler), new LinkedInOptions(TimeSpan.Zero), new FixedTime(Now));

    [Fact]
    public void ParseList_maps_cards()
    {
        var item = Assert.Single(LinkedInPublicSource.ParseList(ListHtml, Now.AddHours(-1)));
        Assert.Equal("123", item.Id);
        Assert.Equal(".NET Developer", item.Job.Title);
        Assert.Equal("Acme", item.Job.Company);
        Assert.Equal("Remote", item.Job.Location);
        Assert.Equal("https://www.linkedin.com/jobs/view/123", item.Job.Url);
    }

    [Fact]
    public void ParseList_uses_the_window_start_when_the_page_only_has_a_date()
    {
        // LinkedIn already applied the time window server-side (f_TPR), so the posting is inside it.
        var item = Assert.Single(LinkedInPublicSource.ParseList(ListHtml, Now.AddHours(-1)));
        Assert.True(item.Job.PostedAt >= Now.AddHours(-1));
        Assert.True(item.Job.PostedAt <= Now);
    }

    [Theory]
    [InlineData("<figcaption class=\"num-applicants__caption\">42 applicants</figcaption>", 42)]
    [InlineData("<figcaption class=\"num-applicants__caption\">Over 200 applicants</figcaption>", 201)]
    [InlineData("<figcaption class=\"num-applicants__caption\">Be among the first 25 applicants</figcaption>", 25)]
    [InlineData("<div>nothing here</div>", null)]
    public void ParseApplicantCount_reads_the_caption(string html, int? expected)
    {
        Assert.Equal(expected, LinkedInPublicSource.ParseApplicantCount(html));
    }

    [Fact]
    public async Task Search_requests_the_window_in_seconds_and_remote_filter()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsoluteUri.Contains("jobPosting/")
                ? "<figcaption class=\"num-applicants__caption\">7 applicants</figcaption>" : ListHtml)
        });
        var jobs = await Source(handler).SearchAsync(Request(max: 50), CancellationToken.None);
        Assert.Equal(7, Assert.Single(jobs).ApplicantCount);
        Assert.Contains("f_TPR=r3600", handler.Requests[0]);
        Assert.Contains("f_WT=2", handler.Requests[0]);
    }

    [Fact]
    public async Task Zero_detail_budget_skips_all_detail_lookups()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ListHtml) });
        var source = new LinkedInPublicSource(new HttpClient(handler),
            new LinkedInOptions(TimeSpan.Zero, 25, TimeSpan.Zero), new FixedTime(Now));
        var jobs = await source.SearchAsync(Request(max: 50), CancellationToken.None);
        Assert.Null(Assert.Single(jobs).ApplicantCount);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_429_raises_SourceBlockedException_without_retrying()
    {
        var handler = FakeHandler.Status((HttpStatusCode)429);
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(handler).SearchAsync(Request(), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_page_with_no_job_cards_is_treated_as_blocked()
    {
        var handler = FakeHandler.Json("<html><script>challenge</script></html>");
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(handler).SearchAsync(Request(), CancellationToken.None));
    }
}
