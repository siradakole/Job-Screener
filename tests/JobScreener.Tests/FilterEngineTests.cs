using JobScreener.Core.Filtering;
using JobScreener.Core.Models;

namespace JobScreener.Tests;

public class FilterEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Job(
        string title = ".NET Developer", WorkType type = WorkType.Remote, string location = "Worldwide",
        int minutesAgo = 10, int? applicants = 5) =>
        new("Test", title, "Acme", location, type, Now.AddMinutes(-minutesAgo), applicants,
            "https://example.com/" + Guid.NewGuid(), "desc");

    private static SearchRequest Request(
        string[]? titles = null, WorkType[]? types = null, string location = "Worldwide",
        LookBack? lookBack = null, int? max = null, bool includeUnknown = true) =>
        new(titles ?? new[] { ".NET Developer" }, types ?? new[] { WorkType.Remote }, location,
            lookBack ?? new LookBack(1, LookBackUnit.Hours), max, includeUnknown);

    [Fact]
    public void Drops_postings_older_than_the_window()
    {
        var jobs = new[] { Job(minutesAgo: 30), Job(minutesAgo: 90) };
        Assert.Single(FilterEngine.Apply(jobs, Request(), Now));
    }

    [Fact]
    public void Tolerates_small_clock_skew_but_not_far_future_postings()
    {
        var jobs = new[] { Job(minutesAgo: -3), Job(minutesAgo: -120) };
        Assert.Single(FilterEngine.Apply(jobs, Request(), Now));
    }

    [Fact]
    public void Requires_a_title_match_when_titles_are_given()
    {
        var jobs = new[] { Job(title: "Senior Dotnet Engineer"), Job(title: "Pastry Chef") };
        var result = FilterEngine.Apply(jobs, Request(), Now);
        Assert.Equal("Senior Dotnet Engineer", Assert.Single(result).Title);
    }

    [Fact]
    public void Empty_titles_means_no_title_filter()
    {
        var jobs = new[] { Job(title: "Pastry Chef") };
        Assert.Single(FilterEngine.Apply(jobs, Request(titles: Array.Empty<string>()), Now));
    }

    [Fact]
    public void Filters_by_work_type_and_keeps_unknown()
    {
        var jobs = new[] { Job(type: WorkType.Remote), Job(type: WorkType.OnSite), Job(type: WorkType.Unknown) };
        var result = FilterEngine.Apply(jobs, Request(types: new[] { WorkType.Remote }), Now);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, j => j.WorkType == WorkType.OnSite);
    }

    [Fact]
    public void Empty_work_types_means_any()
    {
        var jobs = new[] { Job(type: WorkType.OnSite), Job(type: WorkType.Hybrid) };
        Assert.Equal(2, FilterEngine.Apply(jobs, Request(types: Array.Empty<WorkType>()), Now).Count);
    }

    [Theory]
    [InlineData("Worldwide", "Berlin, Germany", true)]
    [InlineData("", "Berlin, Germany", true)]
    [InlineData("Germany", "Berlin, Germany", true)]
    [InlineData("Germany", "Worldwide", true)]
    [InlineData("Germany", "Remote", true)]
    [InlineData("Germany", "Austin, TX", false)]
    [InlineData("Germany", "Anywhere", true)]
    [InlineData("Germany", "Globalville, NJ", false)]
    [InlineData("Germany", "Remoteville", false)]
    [InlineData("Nigeria", "Remote - US only", true)] // known limit: the word "remote" counts as open
    public void Location_rules(string wanted, string jobLocation, bool kept)
    {
        var result = FilterEngine.Apply(new[] { Job(location: jobLocation) }, Request(location: wanted), Now);
        Assert.Equal(kept, result.Count == 1);
    }

    [Fact]
    public void Max_applicants_keeps_counts_at_or_below_the_limit()
    {
        var jobs = new[] { Job(applicants: 100), Job(applicants: 101) };
        Assert.Single(FilterEngine.Apply(jobs, Request(max: 100), Now));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Unknown_applicant_counts_follow_the_toggle(bool includeUnknown, int expected)
    {
        var result = FilterEngine.Apply(new[] { Job(applicants: null) }, Request(max: 50, includeUnknown: includeUnknown), Now);
        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public void No_max_applicants_ignores_counts()
    {
        Assert.Single(FilterEngine.Apply(new[] { Job(applicants: 5000) }, Request(max: null), Now));
    }
}
