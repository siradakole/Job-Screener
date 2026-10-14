using JobScreener.Core.Models;

namespace JobScreener.Core.Search;

public sealed record ScoredJob(JobPosting Job, int Match);
public sealed record SourceWarning(string Source, string Message);
public sealed record SearchResult(
    IReadOnlyList<ScoredJob> Jobs,
    IReadOnlyList<SourceWarning> Warnings,
    IReadOnlyDictionary<string, int> RawCountBySource);
