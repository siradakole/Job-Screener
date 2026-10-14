using JobScreener.Core.Models;
using JobScreener.Core.Resume;
using JobScreener.Core.Text;

namespace JobScreener.Core.Matching;

public static class MatchScorer
{
    public static int Score(IReadOnlyCollection<string> resumeSkills, IReadOnlyCollection<string> searchTitles, JobPosting job)
    {
        var jobSkills = SkillsDictionary.Extract(job.Title + " " + HtmlText.ToPlain(job.Description));
        double skillsPart = 0;
        if (jobSkills.Count > 0)
        {
            var overlap = jobSkills.Count(s => resumeSkills.Contains(s, StringComparer.OrdinalIgnoreCase));
            skillsPart = 85.0 * overlap / jobSkills.Count;
        }
        var titlePart = searchTitles.Any(t => TitleMatcher.Matches(t, job.Title)) ? 15 : 0;
        return (int)Math.Min(100, Math.Round(skillsPart + titlePart, MidpointRounding.AwayFromZero));
    }
}
