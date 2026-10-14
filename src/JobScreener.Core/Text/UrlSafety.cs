namespace JobScreener.Core.Text;

public static class UrlSafety
{
    /// <summary>True only for absolute http/https URLs, so third-party data can never become a javascript: or data: link.</summary>
    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
