using JobScreener.Core.Text;

namespace JobScreener.Tests;

public class UrlSafetyTests
{
    [Theory]
    [InlineData("https://example.com/job/1")]
    [InlineData("http://example.com/job/1")]
    [InlineData("HTTPS://X.COM")]
    public void Accepts_http_and_https(string url) => Assert.True(UrlSafety.IsHttpUrl(url));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/x")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_everything_else(string? url) => Assert.False(UrlSafety.IsHttpUrl(url));
}
