using System.Text.RegularExpressions;

namespace JobScreener.Core.Text;

public static class TitleMatcher
{
    private static readonly HashSet<string> RoleWords = new() { "developer", "engineer", "programmer", "dev" };
    private static readonly Regex Word = new(@"[a-z0-9#+.]+", RegexOptions.Compiled);

    /// <summary>True when every word of the requested title appears in the job title (plus extra text).</summary>
    public static bool Matches(string requested, string jobTitle, string extra = "")
    {
        var want = Tokens(requested);
        if (want.Count == 0) return true;
        var have = Tokens(jobTitle + " " + extra);
        return want.All(have.Contains);
    }

    private static HashSet<string> Tokens(string text)
    {
        var t = text.ToLowerInvariant()
            .Replace("full stack", "fullstack").Replace("full-stack", "fullstack")
            .Replace("back end", "backend").Replace("back-end", "backend")
            .Replace("front end", "frontend").Replace("front-end", "frontend");
        var set = new HashSet<string>();
        foreach (Match m in Word.Matches(t))
        {
            var w = m.Value.Trim('.');
            if (w.Length == 0) continue;
            if (w == "dotnet") w = "net";
            set.Add(RoleWords.Contains(w) ? "engineer" : w);
        }
        return set;
    }
}
