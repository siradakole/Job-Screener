using JobScreener.Core.Models;
using JobScreener.Core.Search;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class SearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Job(string url, string title = ".NET Developer", string description = "C# Azure", int minutesAgo = 5) =>
        new("S", title, "Acme", "Worldwide", WorkType.Remote, Now.AddMinutes(-minutesAgo), null, url, description);

    private static SearchRequest Request(IReadOnlyList<string>? sources = null) =>
        new(new[] { ".NET Developer" }, new[] { WorkType.Remote }, "Worldwide", new LookBack(1, LookBackUnit.Hours), null, true, sources);

    private static SearchService Service(params IJobSource[] sources) => new(sources, new FixedTime(Now));

    [Fact]
    public async Task Jobs_with_empty_urls_are_not_collapsed_but_same_urls_are()
    {
        var a = FakeSource.Returning("A", Job("", title: ".NET Developer"), Job("", title: ".NET Developer II"),
            Job("https://x/9"), Job("https://x/9"));
        var result = await Service(a).RunAsync(Request(), new[] { "C#" }, CancellationToken.None);
        Assert.Equal(3, result.Jobs.Count);
    }

    [Fact]
    public async Task Merges_filters_deduplicates_and_sorts_by_match()
    {
        var a = FakeSource.Returning("A", Job("https://x/1", description: "Python"), Job("https://x/2", description: "C# Azure"));
        var b = FakeSource.Returning("B", Job("https://x/2", description: "C# Azure"), Job("https://x/3", minutesAgo: 500));
        var result = await Service(a, b).RunAsync(Request(), new[] { "C#", ".NET", "Azure" }, CancellationToken.None);

        Assert.Equal(new[] { "https://x/2", "https://x/1" }, result.Jobs.Select(j => j.Job.Url));
        Assert.Equal(100, result.Jobs[0].Match);
        Assert.Equal(2, result.RawCountBySource["A"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task A_failing_source_becomes_a_warning_and_others_still_return()
    {
        var good = FakeSource.Returning("Good", Job("https://x/1"));
        var bad = new FakeSource("Bad", (_, _) => throw new HttpRequestException("boom"));
        var blocked = new FakeSource("Blocked", (_, _) => throw new SourceBlockedException("HTTP 429"));
        var result = await Service(good, bad, blocked).RunAsync(Request(), Array.Empty<string>(), CancellationToken.None);

        Assert.Single(result.Jobs);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Source == "Bad" && w.Message.Contains("boom"));
        Assert.Contains(result.Warnings, w => w.Source == "Blocked" && w.Message.Contains("429"));
    }

    [Fact]
    public async Task A_slow_source_times_out_with_a_warning()
    {
        var slow = new FakeSource("Slow", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Array.Empty<JobPosting>(); });
        var fast = FakeSource.Returning("Fast", Job("https://x/1"));
        var result = await Service(slow, fast).RunAsync(Request(), Array.Empty<string>(), CancellationToken.None,
            perSourceTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Single(result.Jobs);
        Assert.Contains(result.Warnings, w => w.Source == "Slow" && w.Message.Contains("timed out"));
    }

    [Fact]
    public async Task Only_selected_sources_run()
    {
        var a = FakeSource.Returning("A", Job("https://x/1"));
        var b = FakeSource.Returning("B", Job("https://x/2"));
        var result = await Service(a, b).RunAsync(Request(new[] { "B" }), Array.Empty<string>(), CancellationToken.None);
        Assert.Equal("https://x/2", Assert.Single(result.Jobs).Job.Url);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeSource("Slow", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Array.Empty<JobPosting>(); });
        var task = Service(slow).RunAsync(Request(), Array.Empty<string>(), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
