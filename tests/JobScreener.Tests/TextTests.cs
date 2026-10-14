using JobScreener.Core.Text;

namespace JobScreener.Tests;

public class TextTests
{
    [Fact]
    public void ToPlain_strips_tags_and_collapses_spaces()
    {
        Assert.Equal("Hi there", HtmlText.ToPlain("<p>Hi <b>there</b></p>"));
    }

    [Fact]
    public void ToPlain_handles_double_encoded_html()
    {
        Assert.Equal("Hello & bye", HtmlText.ToPlain("&lt;p&gt;Hello &amp;amp; bye&lt;/p&gt;"));
    }

    [Fact]
    public void ToPlain_returns_empty_for_null()
    {
        Assert.Equal("", HtmlText.ToPlain(null));
    }

    [Theory]
    [InlineData("Full Stack Developer", "Senior Fullstack Engineer", true)]
    [InlineData(".NET Developer", "Senior Dotnet Engineer", true)]
    [InlineData("Backend Developer", "Back-End Engineer (C#)", true)]
    [InlineData("Android Developer", "iOS Developer", false)]
    [InlineData("", "Anything", true)]
    public void Matches_treats_role_words_and_spellings_as_equivalent(string requested, string job, bool expected)
    {
        Assert.Equal(expected, TitleMatcher.Matches(requested, job));
    }

    [Fact]
    public void Matches_can_use_extra_text_such_as_tags()
    {
        Assert.True(TitleMatcher.Matches("C# Developer", "Software Engineer", "c#, azure"));
    }
}
