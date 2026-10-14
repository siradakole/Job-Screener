using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobScreener.Core.Text;
using UglyToad.PdfPig;

namespace JobScreener.Core.Resume;

public sealed record ParsedResume(
    string Text,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> SuggestedTitles,
    int? YearsExperience);

public static class ResumeParser
{
    private static readonly string[] KnownTitles =
    {
        "Software Engineer", "Backend Developer", "Full Stack Developer", "Frontend Developer",
        "Android Developer", "Mobile Developer", ".NET Developer", "C# Developer", "Data Engineer",
        "DevOps Engineer", "Web Developer", "QA Engineer"
    };

    private static readonly Regex Years = new(@"(\d{1,2})\s*\+?\s*years?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ExtractText(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx"))
            throw new NotSupportedException("Upload a PDF or DOCX file.");

        // PdfPig and OpenXml need a seekable stream; browser upload streams are not.
        var seekable = stream;
        if (!stream.CanSeek)
        {
            seekable = new MemoryStream();
            stream.CopyTo(seekable);
            seekable.Position = 0;
        }
        return extension == ".pdf" ? FromPdf(seekable) : FromDocx(seekable);
    }

    private static string FromPdf(Stream stream)
    {
        using var doc = PdfDocument.Open(stream);
        return string.Join("\n", doc.GetPages().Select(p => p.Text));
    }

    private static string FromDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        return body is null ? "" : string.Join("\n", body.Descendants<Paragraph>().Select(p => p.InnerText));
    }

    public static ParsedResume Parse(string text)
    {
        var skills = SkillsDictionary.Extract(text);
        return new ParsedResume(text, skills, SuggestTitles(text, skills), YearsOfExperience(text));
    }

    private static List<string> SuggestTitles(string text, IReadOnlyList<string> skills)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length is > 0 and <= 100).ToList();
        var titles = KnownTitles.Where(t => lines.Any(l => TitleMatcher.Matches(t, l))).ToList();
        void Add(string title) { if (!titles.Contains(title)) titles.Add(title); }
        if (skills.Contains(".NET") || skills.Contains("C#")) Add(".NET Developer");
        if (skills.Contains("Android")) Add("Android Developer");
        return titles;
    }

    private static int? YearsOfExperience(string text)
    {
        var values = Years.Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Where(v => v is > 0 and <= 40).ToList();
        return values.Count == 0 ? null : values.Max();
    }
}
