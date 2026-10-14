using JobScreener.Core.Filtering;
using JobScreener.Core.Matching;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Core.Search;

public sealed class SearchService(IEnumerable<IJobSource> sources, TimeProvider time)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<IJobSource> _sources = sources.ToList();

    public IReadOnlyList<string> SourceNames => _sources.Select(s => s.Name).ToList();

    public async Task<SearchResult> RunAsync(SearchRequest request, IReadOnlyCollection<string> resumeSkills,
        CancellationToken ct, TimeSpan? perSourceTimeout = null)
    {
        var timeout = perSourceTimeout ?? DefaultTimeout;
        var selected = _sources
            .Where(s => request.Sources is null || request.Sources.Count == 0 ||
                        request.Sources.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var outcomes = await Task.WhenAll(selected.Select(s => RunOneAsync(s, request, timeout, ct)));

        var warnings = outcomes.Where(o => o.Warning is not null).Select(o => new SourceWarning(o.Name, o.Warning!)).ToList();
        var raw = outcomes.ToDictionary(o => o.Name, o => o.Jobs.Count);
        var merged = outcomes.SelectMany(o => o.Jobs).GroupBy(j => j.Url.Length > 0 ? j.Url : $"{j.Source}|{j.Title}|{j.Company}").Select(g => g.First());

        var now = time.GetUtcNow();
        var scored = FilterEngine.Apply(merged, request, now)
            .Select(j => new ScoredJob(j, MatchScorer.Score(resumeSkills, request.Titles, j)))
            .OrderByDescending(s => s.Match).ThenByDescending(s => s.Job.PostedAt)
            .ToList();
        return new SearchResult(scored, warnings, raw);
    }

    private static async Task<(string Name, IReadOnlyList<JobPosting> Jobs, string? Warning)> RunOneAsync(
        IJobSource source, SearchRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return (source.Name, await source.SearchAsync(request, cts.Token), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (source.Name, Array.Empty<JobPosting>(), $"{source.Name} timed out after {timeout.TotalSeconds:0.#}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (source.Name, Array.Empty<JobPosting>(), $"{source.Name} failed: {ex.Message}");
        }
    }
}
