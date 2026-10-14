using System.Net;
using System.Text.RegularExpressions;

namespace JobScreener.Core.Text;

public static class HtmlText
{
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string ToPlain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = WebUtility.HtmlDecode(html);   // some feeds encode the markup itself
        text = Tags.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return Spaces.Replace(text, " ").Trim();
    }
}
