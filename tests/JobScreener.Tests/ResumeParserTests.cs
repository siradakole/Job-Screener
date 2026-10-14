using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobScreener.Core.Resume;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace JobScreener.Tests;

public class ResumeParserTests
{
    private static MemoryStream MakeDocx(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(p => new Paragraph(new Run(new Text(p))))));
            main.Document.Save();
        }
        ms.Position = 0;
        return ms;
    }

    private static MemoryStream MakePdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText(text, 12, new PdfPoint(25, 700), font);
        return new MemoryStream(builder.Build());
    }

    [Fact]
    public void Extracts_text_from_docx()
    {
        var text = ResumeParser.ExtractText(MakeDocx("Software Engineer", "C# and Azure"), "cv.docx");
        Assert.Contains("C# and Azure", text);
    }

    [Fact]
    public void Extracts_text_from_pdf()
    {
        var text = ResumeParser.ExtractText(MakePdf("Backend Developer with Hangfire"), "cv.pdf");
        Assert.Contains("Hangfire", text);
    }

    [Fact]
    public void Rejects_other_file_types()
    {
        Assert.Throws<NotSupportedException>(() => ResumeParser.ExtractText(new MemoryStream(), "cv.txt"));
    }

    [Fact]
    public void Extract_finds_skills_with_word_boundaries()
    {
        var skills = SkillsDictionary.Extract("Built ASP.NET Core APIs in C#, used MySQL, JavaScript and RabbitMQ.");
        Assert.Contains("C#", skills);
        Assert.Contains(".NET", skills);
        Assert.Contains("ASP.NET Core", skills);
        Assert.Contains("MySQL", skills);
        Assert.Contains("RabbitMQ", skills);
        Assert.Contains("JavaScript", skills);
        Assert.DoesNotContain("Java", skills);   // "JavaScript" must not count as Java
        Assert.DoesNotContain("SQL", skills);    // "MySQL" must not count as SQL
    }

    [Fact]
    public void Parse_returns_skills_titles_and_years()
    {
        var text = "Jane Doe\nSoftware Engineer\nSoftware engineer with 7+ years of experience in C# and Android.";
        var parsed = ResumeParser.Parse(text);
        Assert.Contains("C#", parsed.Skills);
        Assert.Contains("Android", parsed.Skills);
        Assert.Contains("Software Engineer", parsed.SuggestedTitles);
        Assert.Contains(".NET Developer", parsed.SuggestedTitles);
        Assert.Contains("Android Developer", parsed.SuggestedTitles);
        Assert.Equal(7, parsed.YearsExperience);
    }

    [Fact]
    public void Parse_handles_text_with_no_known_skills()
    {
        var parsed = ResumeParser.Parse("Gardener and baker");
        Assert.Empty(parsed.Skills);
        Assert.Null(parsed.YearsExperience);
    }
}
