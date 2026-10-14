using JobScreener.Core.Matching;
using JobScreener.Core.Models;

namespace JobScreener.Tests;

public class MatchScorerTests
{
    private static JobPosting Job(string title, string description) =>
        new("Test", title, "Acme", "Worldwide", WorkType.Remote, DateTimeOffset.UtcNow, null, "https://x", description);

    [Fact]
    public void Full_overlap_and_title_match_scores_100()
    {
        var job = Job(".NET Developer", "We use C# and Azure.");
        var score = MatchScorer.Score(new[] { "C#", ".NET", "Azure" }, new[] { ".NET Developer" }, job);
        Assert.Equal(100, score);
    }

    [Fact]
    public void Partial_overlap_scores_proportionally()
    {
        var job = Job("Pastry Chef", "Needs C# and Java.");   // job skills: C#, Java
        var score = MatchScorer.Score(new[] { "C#" }, new[] { ".NET Developer" }, job);
        Assert.Equal(43, score);   // 85 * 1/2 = 42.5 -> 43, no title bonus
    }

    [Fact]
    public void No_overlap_and_no_title_match_scores_zero()
    {
        var job = Job("Pastry Chef", "Needs Python.");
        Assert.Equal(0, MatchScorer.Score(new[] { "C#" }, new[] { ".NET Developer" }, job));
    }

    [Fact]
    public void Job_with_no_recognised_skills_gets_only_the_title_bonus()
    {
        var job = Job("Senior Dotnet Engineer", "Great team, great pay.");
        Assert.Equal(15, MatchScorer.Score(new[] { "C#" }, new[] { ".NET Developer" }, job));
    }

    [Fact]
    public void Html_in_the_description_is_ignored()
    {
        var job = Job("Dev", "<p>Needs <b>C#</b></p>");
        Assert.Equal(85, MatchScorer.Score(new[] { "C#" }, Array.Empty<string>(), job));
    }
}
