using System.Text.RegularExpressions;

namespace JobScreener.Core.Data;

public static class HandleNormalizer
{
    private static readonly Regex Valid = new(@"^[a-z0-9][a-z0-9\-_%]{0,99}$", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        var s = (input ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) throw new ArgumentException("Enter your LinkedIn handle.", nameof(input));

        var marker = s.IndexOf("linkedin.com/", StringComparison.Ordinal);
        if (marker >= 0) s = s[(marker + "linkedin.com/".Length)..];
        else if (s.Contains("://")) throw new ArgumentException("That is not a LinkedIn address.", nameof(input));

        if (s.StartsWith("in/")) s = s[3..];
        s = s.TrimStart('@');
        var cut = s.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) s = s[..cut];

        if (!Valid.IsMatch(s)) throw new ArgumentException("That does not look like a LinkedIn handle.", nameof(input));
        return s;
    }
}
