using JobScreener.Core.Models;
using JobScreener.Core.Text;

namespace JobScreener.Core.Filtering;

public static class FilterEngine
{
    private static readonly string[] OpenLocations = { "worldwide", "anywhere", "global", "remote" };

    public static IReadOnlyList<JobPosting> Apply(IEnumerable<JobPosting> jobs, SearchRequest request, DateTimeOffset now)
    {
        var cutoff = request.LookBack.CutoffFrom(now);
        var latest = now.AddMinutes(5);
        return jobs
            .Where(j => j.PostedAt >= cutoff && j.PostedAt <= latest)
            .Where(j => TitleOk(j, request))
            .Where(j => TypeOk(j, request))
            .Where(j => LocationOk(j, request))
            .Where(j => ApplicantsOk(j, request))
            .ToList();
    }

    private static bool TitleOk(JobPosting job, SearchRequest r) =>
        r.Titles.Count == 0 || r.Titles.Any(t => TitleMatcher.Matches(t, job.Title));

    private static bool TypeOk(JobPosting job, SearchRequest r) =>
        r.WorkTypes.Count == 0 || job.WorkType == WorkType.Unknown || r.WorkTypes.Contains(job.WorkType);

    private static bool LocationOk(JobPosting job, SearchRequest r)
    {
        var wanted = r.Location.Trim().ToLowerInvariant();
        if (wanted.Length == 0 || OpenLocations.Contains(wanted)) return true;
        var have = job.Location.ToLowerInvariant();
        return have.Contains(wanted) || OpenLocations.Any(have.Contains);
    }

    private static bool ApplicantsOk(JobPosting job, SearchRequest r)
    {
        if (r.MaxApplicants is null) return true;
        if (job.ApplicantCount is null) return r.IncludeUnknownApplicants;
        return job.ApplicantCount <= r.MaxApplicants;
    }
}
