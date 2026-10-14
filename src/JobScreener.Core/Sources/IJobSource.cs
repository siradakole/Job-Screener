using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public interface IJobSource
{
    string Name { get; }
    Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct);
}
