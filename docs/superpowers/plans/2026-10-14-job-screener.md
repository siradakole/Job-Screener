# Job-Screener Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A public ASP.NET Core 8 + Blazor Server app where an applicant enters a LinkedIn handle, uploads a resume, sets five filters and gets a ranked list of jobs from several sources.

**Architecture:** `JobScreener.Core` class library (models, filtering, resume parsing, scoring, job sources, search orchestration, SQLite persistence) plus `JobScreener.Web` (Blazor Server UI) and `JobScreener.Tests` (xUnit, no live network). Every source implements `IJobSource` and returns normalized `JobPosting` records.

**Tech Stack:** .NET 8 (built with the installed .NET 10 SDK, `net8.0` target), Blazor Server, EF Core 8 + SQLite, PdfPig, DocumentFormat.OpenXml, HtmlAgilityPack, xUnit, Docker.

**Spec:** `docs/superpowers/specs/2026-10-14-job-screener-design.md`

## Global Constraints

- Target framework `net8.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` in every project.
- Create the solution with `--format sln` (the .NET 10 SDK defaults to `.slnx`).
- Tests never make live network calls. Sources are tested with fixture JSON/HTML and a fake `HttpMessageHandler`.
- No secrets in the repo. Adzuna keys come from environment variables or user-secrets.
- The LinkedIn plug-in is disabled by default, uses only logged-out public pages, rate limits itself, and stops on HTTP 429. It must never try to bypass a block or challenge page.
- Look-back unit is hours, days or weeks. "Worldwide" means no location restriction.
- The match score is labelled "skills overlap" in the UI, never "AI match".
- Resumes are personal data: never logged, deletable by the applicant.
- Commit after every task. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Repo root: `C:\Users\DELL\source\repos\siradakole\Job-Screener` (remote `https://github.com/siradakole/Job-Screener`, branch `main`).

## File Structure

```
Job-Screener/
  JobScreener.sln
  .gitignore                      (dotnet new gitignore)
  README.md
  Dockerfile
  .github/workflows/ci.yml
  src/JobScreener.Core/
    Models/        WorkType.cs LookBack.cs JobPosting.cs SearchRequest.cs
    Text/          HtmlText.cs TitleMatcher.cs
    Filtering/     FilterEngine.cs
    Resume/        SkillsDictionary.cs ResumeParser.cs
    Matching/      MatchScorer.cs
    Sources/       IJobSource.cs SourceBlockedException.cs JsonRead.cs
                   RemotiveSource.cs RemoteOkSource.cs ArbeitnowSource.cs
                   AdzunaSource.cs LinkedInPublicSource.cs
    Search/        SearchService.cs SearchResult.cs
    Export/        CsvExport.cs
    Data/          AppDbContext.cs Applicant.cs SearchRun.cs ApplicantStore.cs HandleNormalizer.cs
  src/JobScreener.Web/
    Program.cs  appsettings.json
    State/AppState.cs
    Components/    App.razor Routes.razor _Imports.razor
    Components/Layout/MainLayout.razor
    Components/Pages/Home.razor Search.razor
    wwwroot/css/app.css  wwwroot/js/app.js
  tests/JobScreener.Tests/  (one test file per Core unit, plus Fakes.cs)
```

---

### Task 1: Solution scaffold and domain models

**Files:**
- Create: `JobScreener.sln`, `src/JobScreener.Core/*`, `tests/JobScreener.Tests/*`, `.gitignore`
- Create: `src/JobScreener.Core/Models/WorkType.cs`, `LookBack.cs`, `JobPosting.cs`, `SearchRequest.cs`
- Test: `tests/JobScreener.Tests/LookBackTests.cs`

**Interfaces:**
- Produces: `WorkType { Unknown, Remote, OnSite, Hybrid }`, `LookBackUnit { Hours, Days, Weeks }`, `LookBack(int amount, LookBackUnit unit)` with `Amount`, `Unit`, `TimeSpan ToTimeSpan()`, `DateTimeOffset CutoffFrom(DateTimeOffset now)`; `JobPosting(string Source, string Title, string Company, string Location, WorkType WorkType, DateTimeOffset PostedAt, int? ApplicantCount, string Url, string Description)`; `SearchRequest(IReadOnlyList<string> Titles, IReadOnlyList<WorkType> WorkTypes, string Location, LookBack LookBack, int? MaxApplicants, bool IncludeUnknownApplicants = true, IReadOnlyList<string>? Sources = null)`.

- [ ] **Step 1: Scaffold the solution**

```bash
cd /c/Users/DELL/source/repos/siradakole/Job-Screener
dotnet new sln -n JobScreener --format sln
dotnet new classlib -n JobScreener.Core -o src/JobScreener.Core -f net8.0
dotnet new xunit -n JobScreener.Tests -o tests/JobScreener.Tests -f net8.0
dotnet new gitignore
dotnet sln add src/JobScreener.Core tests/JobScreener.Tests
dotnet add tests/JobScreener.Tests reference src/JobScreener.Core
rm src/JobScreener.Core/Class1.cs tests/JobScreener.Tests/UnitTest1.cs
dotnet build
```

Expected: `Build succeeded.` Confirm both `.csproj` files contain `<TargetFramework>net8.0</TargetFramework>`, `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.

- [ ] **Step 2: Write the failing test**

`tests/JobScreener.Tests/LookBackTests.cs`:

```csharp
using JobScreener.Core.Models;

namespace JobScreener.Tests;

public class LookBackTests
{
    [Theory]
    [InlineData(1, LookBackUnit.Hours, 1)]
    [InlineData(2, LookBackUnit.Days, 48)]
    [InlineData(1, LookBackUnit.Weeks, 168)]
    public void ToTimeSpan_converts_units_to_hours(int amount, LookBackUnit unit, double expectedHours)
    {
        var span = new LookBack(amount, unit).ToTimeSpan();
        Assert.Equal(expectedHours, span.TotalHours);
    }

    [Fact]
    public void CutoffFrom_subtracts_the_window()
    {
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var cutoff = new LookBack(3, LookBackUnit.Hours).CutoffFrom(now);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), cutoff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_rejects_amounts_below_one(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LookBack(amount, LookBackUnit.Hours));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --filter LookBackTests`
Expected: build FAIL, `The type or namespace name 'LookBack' could not be found`.

- [ ] **Step 4: Write the models**

`src/JobScreener.Core/Models/WorkType.cs`:

```csharp
namespace JobScreener.Core.Models;

public enum WorkType { Unknown = 0, Remote = 1, OnSite = 2, Hybrid = 3 }
```

`src/JobScreener.Core/Models/LookBack.cs`:

```csharp
namespace JobScreener.Core.Models;

public enum LookBackUnit { Hours, Days, Weeks }

public sealed record LookBack
{
    public LookBack(int amount, LookBackUnit unit)
    {
        if (amount < 1) throw new ArgumentOutOfRangeException(nameof(amount), "Must be at least 1.");
        Amount = amount;
        Unit = unit;
    }

    public int Amount { get; }
    public LookBackUnit Unit { get; }

    public TimeSpan ToTimeSpan() => Unit switch
    {
        LookBackUnit.Hours => TimeSpan.FromHours(Amount),
        LookBackUnit.Days => TimeSpan.FromDays(Amount),
        LookBackUnit.Weeks => TimeSpan.FromDays(7 * Amount),
        _ => throw new ArgumentOutOfRangeException(nameof(Unit))
    };

    public DateTimeOffset CutoffFrom(DateTimeOffset now) => now - ToTimeSpan();
}
```

`src/JobScreener.Core/Models/JobPosting.cs`:

```csharp
namespace JobScreener.Core.Models;

public sealed record JobPosting(
    string Source,
    string Title,
    string Company,
    string Location,
    WorkType WorkType,
    DateTimeOffset PostedAt,
    int? ApplicantCount,
    string Url,
    string Description);
```

`src/JobScreener.Core/Models/SearchRequest.cs`:

```csharp
namespace JobScreener.Core.Models;

public sealed record SearchRequest(
    IReadOnlyList<string> Titles,
    IReadOnlyList<WorkType> WorkTypes,
    string Location,
    LookBack LookBack,
    int? MaxApplicants,
    bool IncludeUnknownApplicants = true,
    IReadOnlyList<string>? Sources = null);
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter LookBackTests`
Expected: `Passed: 5`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): scaffold solution and add domain models

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: HTML text cleaning and title matching

**Files:**
- Create: `src/JobScreener.Core/Text/HtmlText.cs`, `src/JobScreener.Core/Text/TitleMatcher.cs`
- Test: `tests/JobScreener.Tests/TextTests.cs`

**Interfaces:**
- Produces: `HtmlText.ToPlain(string? html) : string`; `TitleMatcher.Matches(string requested, string jobTitle, string extra = "") : bool`.

- [ ] **Step 1: Write the failing tests**

`tests/JobScreener.Tests/TextTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter TextTests`
Expected: build FAIL, `HtmlText` not found.

- [ ] **Step 3: Implement**

`src/JobScreener.Core/Text/HtmlText.cs`:

```csharp
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
```

`src/JobScreener.Core/Text/TitleMatcher.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter TextTests`
Expected: all pass. If `Matches` for `Backend Developer` vs `Back-End Engineer (C#)` fails, check the `back-end` replacement ran before tokenizing.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): HTML cleaning and title matching

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: FilterEngine

**Files:**
- Create: `src/JobScreener.Core/Filtering/FilterEngine.cs`
- Test: `tests/JobScreener.Tests/FilterEngineTests.cs`

**Interfaces:**
- Consumes: `JobPosting`, `SearchRequest`, `WorkType`, `TitleMatcher.Matches`.
- Produces: `FilterEngine.Apply(IEnumerable<JobPosting> jobs, SearchRequest request, DateTimeOffset now) : IReadOnlyList<JobPosting>`.

Rules: look-back window (a posting up to 5 minutes in the future is tolerated for clock skew); title must match at least one requested title (no titles = no title filter); work type must be in `WorkTypes` or be `Unknown` (empty `WorkTypes` = any); location: empty, "worldwide", "anywhere", "global" or "remote" means no restriction, otherwise the posting's location contains the requested text or is itself open ("worldwide", "anywhere", "global", "remote"); applicants: no `MaxApplicants` = pass, unknown count follows `IncludeUnknownApplicants`, known count must be `<= MaxApplicants`.

- [ ] **Step 1: Write the failing tests**

`tests/JobScreener.Tests/FilterEngineTests.cs`:

```csharp
using JobScreener.Core.Filtering;
using JobScreener.Core.Models;

namespace JobScreener.Tests;

public class FilterEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Job(
        string title = ".NET Developer", WorkType type = WorkType.Remote, string location = "Worldwide",
        int minutesAgo = 10, int? applicants = 5) =>
        new("Test", title, "Acme", location, type, Now.AddMinutes(-minutesAgo), applicants,
            "https://example.com/" + Guid.NewGuid(), "desc");

    private static SearchRequest Request(
        string[]? titles = null, WorkType[]? types = null, string location = "Worldwide",
        LookBack? lookBack = null, int? max = null, bool includeUnknown = true) =>
        new(titles ?? new[] { ".NET Developer" }, types ?? new[] { WorkType.Remote }, location,
            lookBack ?? new LookBack(1, LookBackUnit.Hours), max, includeUnknown);

    [Fact]
    public void Drops_postings_older_than_the_window()
    {
        var jobs = new[] { Job(minutesAgo: 30), Job(minutesAgo: 90) };
        Assert.Single(FilterEngine.Apply(jobs, Request(), Now));
    }

    [Fact]
    public void Tolerates_small_clock_skew_but_not_far_future_postings()
    {
        var jobs = new[] { Job(minutesAgo: -3), Job(minutesAgo: -120) };
        Assert.Single(FilterEngine.Apply(jobs, Request(), Now));
    }

    [Fact]
    public void Requires_a_title_match_when_titles_are_given()
    {
        var jobs = new[] { Job(title: "Senior Dotnet Engineer"), Job(title: "Pastry Chef") };
        var result = FilterEngine.Apply(jobs, Request(), Now);
        Assert.Equal("Senior Dotnet Engineer", Assert.Single(result).Title);
    }

    [Fact]
    public void Empty_titles_means_no_title_filter()
    {
        var jobs = new[] { Job(title: "Pastry Chef") };
        Assert.Single(FilterEngine.Apply(jobs, Request(titles: Array.Empty<string>()), Now));
    }

    [Fact]
    public void Filters_by_work_type_and_keeps_unknown()
    {
        var jobs = new[] { Job(type: WorkType.Remote), Job(type: WorkType.OnSite), Job(type: WorkType.Unknown) };
        var result = FilterEngine.Apply(jobs, Request(types: new[] { WorkType.Remote }), Now);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, j => j.WorkType == WorkType.OnSite);
    }

    [Fact]
    public void Empty_work_types_means_any()
    {
        var jobs = new[] { Job(type: WorkType.OnSite), Job(type: WorkType.Hybrid) };
        Assert.Equal(2, FilterEngine.Apply(jobs, Request(types: Array.Empty<WorkType>()), Now).Count);
    }

    [Theory]
    [InlineData("Worldwide", "Berlin, Germany", true)]
    [InlineData("", "Berlin, Germany", true)]
    [InlineData("Germany", "Berlin, Germany", true)]
    [InlineData("Germany", "Worldwide", true)]
    [InlineData("Germany", "Remote", true)]
    [InlineData("Germany", "Austin, TX", false)]
    public void Location_rules(string wanted, string jobLocation, bool kept)
    {
        var result = FilterEngine.Apply(new[] { Job(location: jobLocation) }, Request(location: wanted), Now);
        Assert.Equal(kept, result.Count == 1);
    }

    [Fact]
    public void Max_applicants_keeps_counts_at_or_below_the_limit()
    {
        var jobs = new[] { Job(applicants: 100), Job(applicants: 101) };
        Assert.Single(FilterEngine.Apply(jobs, Request(max: 100), Now));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Unknown_applicant_counts_follow_the_toggle(bool includeUnknown, int expected)
    {
        var result = FilterEngine.Apply(new[] { Job(applicants: null) }, Request(max: 50, includeUnknown: includeUnknown), Now);
        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public void No_max_applicants_ignores_counts()
    {
        Assert.Single(FilterEngine.Apply(new[] { Job(applicants: 5000) }, Request(max: null), Now));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter FilterEngineTests`
Expected: build FAIL, `FilterEngine` not found.

- [ ] **Step 3: Implement**

`src/JobScreener.Core/Filtering/FilterEngine.cs`:

```csharp
using JobScreener.Core.Models;
using JobScreener.Core.Text;

namespace JobScreener.Core.Filtering;

public static class FilterEngine
{
    private static readonly string[] OpenLocations = { "worldwide", "anywhere", "global", "remote" };

    public static IReadOnlyList<JobPosting> Apply(IEnumerable<JobPosting> jobs, SearchRequest request, DateTimeOffset now)
    {
        var cutoff = request.LookBack.CutoffFrom(now);
        var latest = now.AddMinutes(5);
        return jobs
            .Where(j => j.PostedAt >= cutoff && j.PostedAt <= latest)
            .Where(j => TitleOk(j, request))
            .Where(j => TypeOk(j, request))
            .Where(j => LocationOk(j, request))
            .Where(j => ApplicantsOk(j, request))
            .ToList();
    }

    private static bool TitleOk(JobPosting job, SearchRequest r) =>
        r.Titles.Count == 0 || r.Titles.Any(t => TitleMatcher.Matches(t, job.Title));

    private static bool TypeOk(JobPosting job, SearchRequest r) =>
        r.WorkTypes.Count == 0 || job.WorkType == WorkType.Unknown || r.WorkTypes.Contains(job.WorkType);

    private static bool LocationOk(JobPosting job, SearchRequest r)
    {
        var wanted = r.Location.Trim().ToLowerInvariant();
        if (wanted.Length == 0 || OpenLocations.Contains(wanted)) return true;
        var have = job.Location.ToLowerInvariant();
        return have.Contains(wanted) || OpenLocations.Any(have.Contains);
    }

    private static bool ApplicantsOk(JobPosting job, SearchRequest r)
    {
        if (r.MaxApplicants is null) return true;
        if (job.ApplicantCount is null) return r.IncludeUnknownApplicants;
        return job.ApplicantCount <= r.MaxApplicants;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter FilterEngineTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): FilterEngine for look-back, title, type, location, applicants

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Skills dictionary and resume parser

**Files:**
- Create: `src/JobScreener.Core/Resume/SkillsDictionary.cs`, `src/JobScreener.Core/Resume/ResumeParser.cs`
- Modify: `src/JobScreener.Core/JobScreener.Core.csproj` (packages)
- Test: `tests/JobScreener.Tests/ResumeParserTests.cs`

**Interfaces:**
- Produces: `SkillsDictionary.Extract(string text) : IReadOnlyList<string>` (canonical names such as `C#`, `.NET`), `SkillsDictionary.All : IReadOnlyList<string>`; `ParsedResume(string Text, IReadOnlyList<string> Skills, IReadOnlyList<string> SuggestedTitles, int? YearsExperience)`; `ResumeParser.ExtractText(Stream stream, string fileName) : string`; `ResumeParser.Parse(string text) : ParsedResume`.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/JobScreener.Core package UglyToad.PdfPig
dotnet add src/JobScreener.Core package DocumentFormat.OpenXml
```

- [ ] **Step 2: Write the failing tests**

`tests/JobScreener.Tests/ResumeParserTests.cs`:

```csharp
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
        var page = builder.AddPage(PageSize.A4);
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
        var text = "Samuel Adakole\nSoftware Engineer\nSoftware engineer with 7+ years of experience in C# and Android.";
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter ResumeParserTests`
Expected: build FAIL, `SkillsDictionary` not found.

- [ ] **Step 4: Implement**

`src/JobScreener.Core/Resume/SkillsDictionary.cs`:

```csharp
using System.Text.RegularExpressions;

namespace JobScreener.Core.Resume;

public static class SkillsDictionary
{
    private static readonly (string Canonical, string[] Aliases)[] Entries =
    {
        ("C#", new[] { "c#", "csharp" }),
        (".NET", new[] { ".net", "dotnet", "asp.net" }),
        ("ASP.NET Core", new[] { "asp.net core", "aspnet core" }),
        ("Blazor", new[] { "blazor" }),
        ("Entity Framework", new[] { "entity framework", "ef core" }),
        ("SQL Server", new[] { "sql server", "mssql", "t-sql" }),
        ("MySQL", new[] { "mysql" }),
        ("PostgreSQL", new[] { "postgresql", "postgres" }),
        ("SQLite", new[] { "sqlite" }),
        ("SQL", new[] { "sql" }),
        ("Hangfire", new[] { "hangfire" }),
        ("RabbitMQ", new[] { "rabbitmq" }),
        ("Azure", new[] { "azure" }),
        ("AWS", new[] { "aws", "amazon web services" }),
        ("Docker", new[] { "docker" }),
        ("Kubernetes", new[] { "kubernetes", "k8s" }),
        ("Git", new[] { "git", "github" }),
        ("REST API", new[] { "rest api", "rest apis", "restful" }),
        ("Webhooks", new[] { "webhook", "webhooks" }),
        ("Microservices", new[] { "microservice", "microservices" }),
        ("JWT", new[] { "jwt" }),
        ("CI/CD", new[] { "ci/cd", "cicd" }),
        ("Automation", new[] { "automation", "automate", "automated" }),
        ("Android", new[] { "android" }),
        ("Java", new[] { "java" }),
        ("Kotlin", new[] { "kotlin" }),
        ("React", new[] { "react", "reactjs", "react.js" }),
        ("Node.js", new[] { "node.js", "nodejs", "node js" }),
        ("JavaScript", new[] { "javascript" }),
        ("TypeScript", new[] { "typescript" }),
        ("Python", new[] { "python" }),
        ("Redis", new[] { "redis" }),
        ("MongoDB", new[] { "mongodb" }),
        ("GraphQL", new[] { "graphql" }),
        ("Unity", new[] { "unity" }),
        ("Firebase", new[] { "firebase" }),
        ("OpenAI", new[] { "openai" }),
    };

    private static readonly (string Canonical, Regex[] Patterns)[] Compiled = Entries
        .Select(e => (e.Canonical, e.Aliases.Select(a =>
            new Regex($@"(?<![a-z0-9]){Regex.Escape(a)}(?![a-z0-9])", RegexOptions.Compiled)).ToArray()))
        .ToArray();

    public static IReadOnlyList<string> All { get; } = Entries.Select(e => e.Canonical).ToList();

    public static IReadOnlyList<string> Extract(string text)
    {
        var lower = (text ?? "").ToLowerInvariant();
        return Compiled.Where(e => e.Patterns.Any(p => p.IsMatch(lower))).Select(e => e.Canonical).ToList();
    }
}
```

`src/JobScreener.Core/Resume/ResumeParser.cs`:

```csharp
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
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter ResumeParserTests`
Expected: all pass. If the PDF test fails to extract text, check the PdfPig version's `PdfDocumentBuilder.AddText` signature and adjust the test helper only.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): skills dictionary and resume parser (PDF, DOCX)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: MatchScorer

**Files:**
- Create: `src/JobScreener.Core/Matching/MatchScorer.cs`
- Test: `tests/JobScreener.Tests/MatchScorerTests.cs`

**Interfaces:**
- Consumes: `JobPosting`, `SkillsDictionary.Extract`, `TitleMatcher.Matches`, `HtmlText.ToPlain`.
- Produces: `MatchScorer.Score(IReadOnlyCollection<string> resumeSkills, IReadOnlyCollection<string> searchTitles, JobPosting job) : int` (0 to 100).

Formula: `jobSkills` = skills found in title + plain description. If `jobSkills` is empty the skills part is 0. Skills part = `85 * |resumeSkills ∩ jobSkills| / |jobSkills|`. Title bonus = 15 if any search title matches the job title. Result = rounded sum, capped at 100.

- [ ] **Step 1: Write the failing tests**

`tests/JobScreener.Tests/MatchScorerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter MatchScorerTests`
Expected: build FAIL, `MatchScorer` not found.

- [ ] **Step 3: Implement**

`src/JobScreener.Core/Matching/MatchScorer.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter MatchScorerTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): skills-overlap MatchScorer

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Source contract and the three open API sources

**Files:**
- Create: `src/JobScreener.Core/Sources/IJobSource.cs`, `SourceBlockedException.cs`, `JsonRead.cs`, `RemotiveSource.cs`, `RemoteOkSource.cs`, `ArbeitnowSource.cs`
- Test: `tests/JobScreener.Tests/Fakes.cs`, `tests/JobScreener.Tests/OpenApiSourceTests.cs`

**Interfaces:**
- Produces:
  - `interface IJobSource { string Name { get; } Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct); }`
  - `class SourceBlockedException : Exception` (ctor `(string message)`).
  - `RemotiveSource(HttpClient http)`, `RemoteOkSource(HttpClient http)`, `ArbeitnowSource(HttpClient http)`, each with a `public static IReadOnlyList<JobPosting> Parse(string json)` used by tests.
  - Test helper `FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)` and `FixedTime(DateTimeOffset now) : TimeProvider`.
- Names: `"Remotive"`, `"Remote OK"`, `"Arbeitnow"`.

API facts (verified 2026-10-14): Remotive `GET https://remotive.com/api/remote-jobs?search=&limit=` returns `{"jobs":[{id,url,title,company_name,candidate_required_location,publication_date,description,...}]}` (date has no timezone, treat as UTC). Remote OK `GET https://remoteok.com/api` returns an array whose first element is a legal notice, then `{id,epoch,position,company,location,tags[],description,url,apply_url}`; it requires a `User-Agent`. Arbeitnow `GET https://www.arbeitnow.com/api/job-board-api?page=N` returns `{"data":[{slug,company_name,title,description(HTML-encoded),remote(bool),url,tags,location,created_at(epoch seconds)}]}`.

- [ ] **Step 1: Write the test helpers and failing tests**

`tests/JobScreener.Tests/Fakes.cs`:

```csharp
using System.Net;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(respond(request));
    }

    public static FakeHandler Json(string body) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    });

    public static FakeHandler Status(HttpStatusCode code) => new(_ => new HttpResponseMessage(code));
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class FakeSource(string name, Func<SearchRequest, CancellationToken, Task<IReadOnlyList<JobPosting>>> run) : IJobSource
{
    public string Name => name;
    public Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct) => run(request, ct);

    public static FakeSource Returning(string name, params JobPosting[] jobs) =>
        new(name, (_, _) => Task.FromResult<IReadOnlyList<JobPosting>>(jobs));
}
```

`tests/JobScreener.Tests/OpenApiSourceTests.cs`:

```csharp
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class OpenApiSourceTests
{
    private static SearchRequest Request(params string[] titles) =>
        new(titles, new[] { WorkType.Remote }, "Worldwide", new LookBack(24, LookBackUnit.Hours), null);

    private const string RemotiveJson = """
    {"job-count":1,"jobs":[{"id":1680495,"url":"https://remotive.com/remote-jobs/software-dev/dotnet-1680495",
    "title":".NET Developer","company_name":"Acme","candidate_required_location":"Worldwide",
    "publication_date":"2026-10-14T11:30:00","description":"<p>C# and Azure</p>"}]}
    """;

    private const string RemoteOkJson = """
    [{"last_updated":1,"legal":"Please link back"},
     {"id":"1137510","epoch":1791990000,"position":"Backend Engineer","company":"Globex","location":"",
      "tags":["c#","dotnet"],"description":"<p>Build APIs</p>","url":"https://remoteok.com/remote-jobs/1137510",
      "apply_url":"https://remoteok.com/remote-jobs/1137510"}]
    """;

    private const string ArbeitnowJson = """
    {"data":[{"slug":"dev-1","company_name":"Initech","title":"C# Developer",
    "description":"&lt;p&gt;Hello&lt;/p&gt;","remote":true,"url":"https://www.arbeitnow.com/jobs/dev-1",
    "tags":["c#"],"location":"Berlin","created_at":1791990000},
    {"slug":"dev-2","company_name":"Hooli","title":"Office Developer","description":"x","remote":false,
    "url":"https://www.arbeitnow.com/jobs/dev-2","tags":[],"location":"Munich","created_at":1791990000}],
    "links":{"next":null}}
    """;

    [Fact]
    public void Remotive_parse_maps_fields()
    {
        var job = Assert.Single(RemotiveSource.Parse(RemotiveJson));
        Assert.Equal("Remotive", job.Source);
        Assert.Equal(".NET Developer", job.Title);
        Assert.Equal("Acme", job.Company);
        Assert.Equal("Worldwide", job.Location);
        Assert.Equal(WorkType.Remote, job.WorkType);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 11, 30, 0, TimeSpan.Zero), job.PostedAt);
        Assert.Null(job.ApplicantCount);
    }

    [Fact]
    public async Task Remotive_search_requests_each_title_and_deduplicates()
    {
        var handler = FakeHandler.Json(RemotiveJson);
        var source = new RemotiveSource(new HttpClient(handler));
        var jobs = await source.SearchAsync(Request(".NET Developer", "C# Developer"), CancellationToken.None);
        Assert.Single(jobs);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("search=%2ENET%20Developer", handler.Requests[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteOk_parse_skips_the_legal_notice_and_maps_fields()
    {
        var job = Assert.Single(RemoteOkSource.Parse(RemoteOkJson));
        Assert.Equal("Remote OK", job.Source);
        Assert.Equal("Backend Engineer", job.Title);
        Assert.Equal("Worldwide", job.Location);   // empty location means anywhere
        Assert.Equal(WorkType.Remote, job.WorkType);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791990000), job.PostedAt);
        Assert.Contains("c#", job.Description);    // tags are appended so skills can be detected
    }

    [Fact]
    public async Task RemoteOk_sends_a_user_agent()
    {
        string? agent = null;
        var handler = new FakeHandler(req =>
        {
            agent = req.Headers.UserAgent.ToString();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(RemoteOkJson) };
        });
        await new RemoteOkSource(new HttpClient(handler)).SearchAsync(Request("Backend"), CancellationToken.None);
        Assert.Contains("Job-Screener", agent);
    }

    [Fact]
    public void Arbeitnow_parse_maps_remote_flag_and_decodes_html()
    {
        var jobs = ArbeitnowSource.Parse(ArbeitnowJson);
        Assert.Equal(2, jobs.Count);
        Assert.Equal(WorkType.Remote, jobs[0].WorkType);
        Assert.Equal(WorkType.OnSite, jobs[1].WorkType);
        Assert.Equal("Berlin", jobs[0].Location);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791990000), jobs[0].PostedAt);
    }

    [Fact]
    public async Task Source_throws_on_http_error()
    {
        var source = new RemotiveSource(new HttpClient(FakeHandler.Status(System.Net.HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync(Request("x"), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter OpenApiSourceTests`
Expected: build FAIL, `RemotiveSource` not found.

- [ ] **Step 3: Implement the contract and helpers**

`src/JobScreener.Core/Sources/IJobSource.cs`:

```csharp
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public interface IJobSource
{
    string Name { get; }
    Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct);
}
```

`src/JobScreener.Core/Sources/SourceBlockedException.cs`:

```csharp
namespace JobScreener.Core.Sources;

/// <summary>Raised when a source refuses us (HTTP 429, challenge page). Never retried in a loop.</summary>
public sealed class SourceBlockedException(string message) : Exception(message);
```

`src/JobScreener.Core/Sources/JsonRead.cs`:

```csharp
using System.Text.Json;

namespace JobScreener.Core.Sources;

internal static class JsonRead
{
    public static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.ToString(),
            _ => ""
        };
    }

    public static long Long(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
        return long.TryParse(v.GetString(), out var l) ? l : 0;
    }

    public static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static IEnumerable<string> Strings(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) yield return item.GetString() ?? "";
    }
}
```

- [ ] **Step 4: Implement the three sources**

`src/JobScreener.Core/Sources/RemotiveSource.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed class RemotiveSource(HttpClient http) : IJobSource
{
    public string Name => "Remotive";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var byUrl = new Dictionary<string, JobPosting>();
        foreach (var title in request.Titles.DefaultIfEmpty(""))
        {
            var url = $"https://remotive.com/api/remote-jobs?limit=100&search={Uri.EscapeDataString(title)}";
            using var response = await http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429) throw new SourceBlockedException("Remotive rate limit reached.");
            response.EnsureSuccessStatusCode();
            foreach (var job in Parse(await response.Content.ReadAsStringAsync(ct)))
                byUrl[job.Url] = job;
        }
        return byUrl.Values.ToList();
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return Array.Empty<JobPosting>();
        var list = new List<JobPosting>();
        foreach (var j in jobs.EnumerateArray())
        {
            var posted = DateTimeOffset.TryParse(JsonRead.Str(j, "publication_date"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var p) ? p : DateTimeOffset.MinValue;
            var location = JsonRead.Str(j, "candidate_required_location");
            list.Add(new JobPosting("Remotive", JsonRead.Str(j, "title"), JsonRead.Str(j, "company_name"),
                location.Length == 0 ? "Worldwide" : location, WorkType.Remote, posted, null,
                JsonRead.Str(j, "url"), JsonRead.Str(j, "description")));
        }
        return list;
    }
}
```

`src/JobScreener.Core/Sources/RemoteOkSource.cs`:

```csharp
using System.Text.Json;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed class RemoteOkSource(HttpClient http) : IJobSource
{
    public string Name => "Remote OK";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://remoteok.com/api");
        message.Headers.UserAgent.ParseAdd("Job-Screener/1.0 (+https://github.com/siradakole/Job-Screener)");
        using var response = await http.SendAsync(message, ct);
        if ((int)response.StatusCode == 429) throw new SourceBlockedException("Remote OK rate limit reached.");
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<JobPosting>();
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            if (!j.TryGetProperty("position", out _)) continue;   // the first element is a legal notice
            var tags = string.Join(", ", JsonRead.Strings(j, "tags"));
            var location = JsonRead.Str(j, "location");
            var description = JsonRead.Str(j, "description") + (tags.Length > 0 ? " Tags: " + tags : "");
            list.Add(new JobPosting("Remote OK", JsonRead.Str(j, "position"), JsonRead.Str(j, "company"),
                location.Length == 0 ? "Worldwide" : location, WorkType.Remote,
                DateTimeOffset.FromUnixTimeSeconds(JsonRead.Long(j, "epoch")), null,
                JsonRead.Str(j, "url"), description));
        }
        return list;
    }
}
```

`src/JobScreener.Core/Sources/ArbeitnowSource.cs`:

```csharp
using System.Text.Json;
using JobScreener.Core.Models;
using JobScreener.Core.Text;

namespace JobScreener.Core.Sources;

public sealed class ArbeitnowSource(HttpClient http) : IJobSource
{
    private const int Pages = 3;

    public string Name => "Arbeitnow";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var all = new List<JobPosting>();
        for (var page = 1; page <= Pages; page++)
        {
            using var response = await http.GetAsync($"https://www.arbeitnow.com/api/job-board-api?page={page}", ct);
            if ((int)response.StatusCode == 429) throw new SourceBlockedException("Arbeitnow rate limit reached.");
            response.EnsureSuccessStatusCode();
            var jobs = Parse(await response.Content.ReadAsStringAsync(ct));
            if (jobs.Count == 0) break;
            all.AddRange(jobs);
        }
        return all;
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<JobPosting>();
        var list = new List<JobPosting>();
        foreach (var j in data.EnumerateArray())
        {
            var tags = string.Join(", ", JsonRead.Strings(j, "tags"));
            var description = HtmlText.ToPlain(JsonRead.Str(j, "description")) + (tags.Length > 0 ? " Tags: " + tags : "");
            list.Add(new JobPosting("Arbeitnow", JsonRead.Str(j, "title"), JsonRead.Str(j, "company_name"),
                JsonRead.Str(j, "location"), JsonRead.Bool(j, "remote") ? WorkType.Remote : WorkType.OnSite,
                DateTimeOffset.FromUnixTimeSeconds(JsonRead.Long(j, "created_at")), null,
                JsonRead.Str(j, "url"), description));
        }
        return list;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter OpenApiSourceTests`
Expected: all pass. If the Remotive request-URL assertion fails, print `handler.Requests[0]`. `Uri.EscapeDataString(".NET Developer")` gives `.NET%20Developer`, so relax the assertion to `Contains("search=")` plus `Contains("Developer")` rather than changing the source.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): IJobSource and Remotive, Remote OK, Arbeitnow sources

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Adzuna source

**Files:**
- Create: `src/JobScreener.Core/Sources/AdzunaSource.cs`
- Test: `tests/JobScreener.Tests/AdzunaSourceTests.cs`

**Interfaces:**
- Consumes: `IJobSource`, `JsonRead`, `SourceBlockedException`.
- Produces: `AdzunaOptions(string AppId, string AppKey, string Country = "gb")`; `AdzunaSource(HttpClient http, AdzunaOptions options)` with `Name == "Adzuna"` and `static Parse(string json)`.

Adzuna facts: `GET https://api.adzuna.com/v1/api/jobs/{country}/search/1?app_id=&app_key=&results_per_page=50&what=&where=&max_days_old=` returns `{"results":[{title,company:{display_name},location:{display_name},created(ISO UTC),redirect_url,description}]}`. There is no remote flag, so work type is `Remote` only when the title or description says "remote", else `Unknown`.

- [ ] **Step 1: Write the failing tests**

`tests/JobScreener.Tests/AdzunaSourceTests.cs`:

```csharp
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class AdzunaSourceTests
{
    private const string Json = """
    {"results":[
     {"title":"C# Developer","company":{"display_name":"Acme"},"location":{"display_name":"London"},
      "created":"2026-10-14T11:00:00Z","redirect_url":"https://adzuna.example/1","description":"Fully remote role"},
     {"title":"Java Developer","company":{"display_name":"Globex"},"location":{"display_name":"Leeds"},
      "created":"2026-10-14T10:00:00Z","redirect_url":"https://adzuna.example/2","description":"Office based"}]}
    """;

    [Fact]
    public void Parse_maps_fields_and_detects_remote_wording()
    {
        var jobs = AdzunaSource.Parse(Json);
        Assert.Equal(2, jobs.Count);
        Assert.Equal("Adzuna", jobs[0].Source);
        Assert.Equal("London", jobs[0].Location);
        Assert.Equal(WorkType.Remote, jobs[0].WorkType);
        Assert.Equal(WorkType.Unknown, jobs[1].WorkType);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.Zero), jobs[0].PostedAt);
    }

    [Fact]
    public async Task Search_builds_the_url_with_keys_days_and_location()
    {
        var handler = FakeHandler.Json(Json);
        var source = new AdzunaSource(new HttpClient(handler), new AdzunaOptions("id1", "key1", "us"));
        var request = new SearchRequest(new[] { "C# Developer" }, Array.Empty<WorkType>(), "Austin",
            new LookBack(2, LookBackUnit.Days), null);
        await source.SearchAsync(request, CancellationToken.None);
        var url = Assert.Single(handler.Requests);
        Assert.Contains("/jobs/us/search/1", url);
        Assert.Contains("app_id=id1", url);
        Assert.Contains("max_days_old=2", url);
        Assert.Contains("where=Austin", url);
    }

    [Fact]
    public async Task Search_omits_where_for_worldwide_and_rounds_hours_up_to_a_day()
    {
        var handler = FakeHandler.Json(Json);
        var source = new AdzunaSource(new HttpClient(handler), new AdzunaOptions("id1", "key1"));
        var request = new SearchRequest(new[] { "C#" }, Array.Empty<WorkType>(), "Worldwide",
            new LookBack(3, LookBackUnit.Hours), null);
        await source.SearchAsync(request, CancellationToken.None);
        var url = Assert.Single(handler.Requests);
        Assert.DoesNotContain("where=", url);
        Assert.Contains("max_days_old=1", url);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter AdzunaSourceTests`
Expected: build FAIL, `AdzunaSource` not found.

- [ ] **Step 3: Implement**

`src/JobScreener.Core/Sources/AdzunaSource.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed record AdzunaOptions(string AppId, string AppKey, string Country = "gb");

public sealed class AdzunaSource(HttpClient http, AdzunaOptions options) : IJobSource
{
    public string Name => "Adzuna";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var days = Math.Max(1, (int)Math.Ceiling(request.LookBack.ToTimeSpan().TotalDays));
        var worldwide = request.Location.Trim().Length == 0 ||
                        request.Location.Trim().Equals("worldwide", StringComparison.OrdinalIgnoreCase);
        var byUrl = new Dictionary<string, JobPosting>();
        foreach (var title in request.Titles.DefaultIfEmpty(""))
        {
            var url = $"https://api.adzuna.com/v1/api/jobs/{options.Country}/search/1" +
                      $"?app_id={Uri.EscapeDataString(options.AppId)}&app_key={Uri.EscapeDataString(options.AppKey)}" +
                      $"&results_per_page=50&max_days_old={days}&what={Uri.EscapeDataString(title)}" +
                      (worldwide ? "" : $"&where={Uri.EscapeDataString(request.Location)}");
            using var response = await http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429) throw new SourceBlockedException("Adzuna rate limit reached.");
            response.EnsureSuccessStatusCode();
            foreach (var job in Parse(await response.Content.ReadAsStringAsync(ct)))
                byUrl[job.Url] = job;
        }
        return byUrl.Values.ToList();
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<JobPosting>();
        var list = new List<JobPosting>();
        foreach (var j in results.EnumerateArray())
        {
            var title = JsonRead.Str(j, "title");
            var description = JsonRead.Str(j, "description");
            var company = j.TryGetProperty("company", out var c) ? JsonRead.Str(c, "display_name") : "";
            var location = j.TryGetProperty("location", out var l) ? JsonRead.Str(l, "display_name") : "";
            var posted = DateTimeOffset.TryParse(JsonRead.Str(j, "created"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var p) ? p : DateTimeOffset.MinValue;
            var remote = (title + " " + description).Contains("remote", StringComparison.OrdinalIgnoreCase);
            list.Add(new JobPosting("Adzuna", title, company, location, remote ? WorkType.Remote : WorkType.Unknown,
                posted, null, JsonRead.Str(j, "redirect_url"), description));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter AdzunaSourceTests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): Adzuna source

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: LinkedIn public-page plug-in (best effort, off by default)

**Files:**
- Create: `src/JobScreener.Core/Sources/LinkedInPublicSource.cs`
- Modify: `src/JobScreener.Core/JobScreener.Core.csproj` (HtmlAgilityPack)
- Test: `tests/JobScreener.Tests/LinkedInPublicSourceTests.cs`

**Interfaces:**
- Consumes: `IJobSource`, `SourceBlockedException`, `TimeProvider`.
- Produces: `LinkedInOptions(TimeSpan RequestDelay, int MaxDetailLookups = 25)`; `LinkedInPublicSource(HttpClient http, LinkedInOptions options, TimeProvider time)` with `Name == "LinkedIn (public pages)"`, `static ParseList(string html, DateTimeOffset notBefore) : IReadOnlyList<(string Id, JobPosting Job)>` and `static ParseApplicantCount(string html) : int?`.

Status (verified 2026-10-14): a single logged-out request to `jobs-guest/jobs/api/seeMoreJobPostings/search` from the developer machine returned HTTP 429 with an anti-bot challenge page. The markup below is from LinkedIn's public guest pages as documented by other projects and is **not verified live**. The source therefore must: treat any 429, any non-success status, or any response without job cards as "blocked" and raise `SourceBlockedException`; never retry; never try to solve or bypass a challenge. The README says this plug-in often does not work.

Guest URLs: list `https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search?keywords=&location=&f_TPR=r{seconds}&f_WT={1|2|3}&start=0` (`f_WT`: 1 on-site, 2 remote, 3 hybrid); detail `https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/{id}`.

- [ ] **Step 1: Add the package**

```bash
dotnet add src/JobScreener.Core package HtmlAgilityPack
```

- [ ] **Step 2: Write the failing tests**

`tests/JobScreener.Tests/LinkedInPublicSourceTests.cs`:

```csharp
using System.Net;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class LinkedInPublicSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private const string ListHtml = """
    <ul><li><div class="base-card" data-entity-urn="urn:li:jobPosting:123">
      <a class="base-card__full-link" href="https://www.linkedin.com/jobs/view/123?trk=x"></a>
      <h3 class="base-search-card__title"> .NET Developer </h3>
      <h4 class="base-search-card__subtitle"><a>Acme</a></h4>
      <span class="job-search-card__location">Remote</span>
      <time class="job-search-card__listdate" datetime="2026-10-14"></time>
    </div></li></ul>
    """;

    private static SearchRequest Request(int? max = null) =>
        new(new[] { ".NET Developer" }, new[] { WorkType.Remote }, "Worldwide", new LookBack(1, LookBackUnit.Hours), max);

    private static LinkedInPublicSource Source(FakeHandler handler) =>
        new(new HttpClient(handler), new LinkedInOptions(TimeSpan.Zero), new FixedTime(Now));

    [Fact]
    public void ParseList_maps_cards()
    {
        var item = Assert.Single(LinkedInPublicSource.ParseList(ListHtml, Now.AddHours(-1)));
        Assert.Equal("123", item.Id);
        Assert.Equal(".NET Developer", item.Job.Title);
        Assert.Equal("Acme", item.Job.Company);
        Assert.Equal("Remote", item.Job.Location);
        Assert.Equal("https://www.linkedin.com/jobs/view/123", item.Job.Url);
    }

    [Fact]
    public void ParseList_uses_the_window_start_when_the_page_only_has_a_date()
    {
        // LinkedIn already applied the time window server-side (f_TPR), so the posting is inside it.
        var item = Assert.Single(LinkedInPublicSource.ParseList(ListHtml, Now.AddHours(-1)));
        Assert.True(item.Job.PostedAt >= Now.AddHours(-1));
        Assert.True(item.Job.PostedAt <= Now);
    }

    [Theory]
    [InlineData("<figcaption class=\"num-applicants__caption\">42 applicants</figcaption>", 42)]
    [InlineData("<figcaption class=\"num-applicants__caption\">Over 200 applicants</figcaption>", 201)]
    [InlineData("<figcaption class=\"num-applicants__caption\">Be among the first 25 applicants</figcaption>", 25)]
    [InlineData("<div>nothing here</div>", null)]
    public void ParseApplicantCount_reads_the_caption(string html, int? expected)
    {
        Assert.Equal(expected, LinkedInPublicSource.ParseApplicantCount(html));
    }

    [Fact]
    public async Task Search_requests_the_window_in_seconds_and_remote_filter()
    {
        var handler = new FakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsoluteUri.Contains("jobPosting/")
                ? "<figcaption class=\"num-applicants__caption\">7 applicants</figcaption>" : ListHtml)
        });
        var jobs = await Source(handler).SearchAsync(Request(max: 50), CancellationToken.None);
        Assert.Equal(7, Assert.Single(jobs).ApplicantCount);
        Assert.Contains("f_TPR=r3600", handler.Requests[0]);
        Assert.Contains("f_WT=2", handler.Requests[0]);
    }

    [Fact]
    public async Task Http_429_raises_SourceBlockedException_without_retrying()
    {
        var handler = FakeHandler.Status((HttpStatusCode)429);
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(handler).SearchAsync(Request(), CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_page_with_no_job_cards_is_treated_as_blocked()
    {
        var handler = FakeHandler.Json("<html><script>challenge</script></html>");
        await Assert.ThrowsAsync<SourceBlockedException>(() => Source(handler).SearchAsync(Request(), CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter LinkedInPublicSourceTests`
Expected: build FAIL, `LinkedInPublicSource` not found.

- [ ] **Step 4: Implement**

`src/JobScreener.Core/Sources/LinkedInPublicSource.cs`:

```csharp
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed record LinkedInOptions(TimeSpan RequestDelay, int MaxDetailLookups = 25);

/// <summary>
/// Optional, best-effort reader of LinkedIn's public (logged-out) job pages. Disabled by default.
/// Any 429, error status or page without job cards is reported as blocked and never retried.
/// </summary>
public sealed class LinkedInPublicSource(HttpClient http, LinkedInOptions options, TimeProvider time) : IJobSource
{
    private static readonly Regex Number = new(@"(\d[\d,]*)", RegexOptions.Compiled);

    public string Name => "LinkedIn (public pages)";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var windowStart = request.LookBack.CutoffFrom(now);
        var seconds = (long)request.LookBack.ToTimeSpan().TotalSeconds;
        var byId = new Dictionary<string, JobPosting>();

        foreach (var title in request.Titles.DefaultIfEmpty(""))
        {
            var url = "https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search" +
                      $"?keywords={Uri.EscapeDataString(title)}&location={Uri.EscapeDataString(request.Location)}" +
                      $"&f_TPR=r{seconds}{WorkTypeFilter(request)}&start=0";
            var html = await GetAsync(url, ct);
            var items = ParseList(html, windowStart);
            if (items.Count == 0) throw new SourceBlockedException("LinkedIn returned no job cards (blocked or no results).");
            foreach (var (id, job) in items) byId[id] = job;
            await Task.Delay(options.RequestDelay, ct);
        }

        if (request.MaxApplicants is not null)
        {
            var lookups = 0;
            foreach (var id in byId.Keys.ToList())
            {
                if (lookups++ >= options.MaxDetailLookups) break;
                var html = await GetAsync($"https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/{id}", ct);
                byId[id] = byId[id] with { ApplicantCount = ParseApplicantCount(html) };
                await Task.Delay(options.RequestDelay, ct);
            }
        }
        return byId.Values.ToList();
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Job-Screener/1.0; +https://github.com/siradakole/Job-Screener)");
        using var response = await http.SendAsync(message, ct);
        if ((int)response.StatusCode == 429) throw new SourceBlockedException("LinkedIn rate limit reached (HTTP 429).");
        if (!response.IsSuccessStatusCode) throw new SourceBlockedException($"LinkedIn returned HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string WorkTypeFilter(SearchRequest r)
    {
        var codes = r.WorkTypes.Select(t => t switch { WorkType.OnSite => "1", WorkType.Remote => "2", WorkType.Hybrid => "3", _ => "" })
            .Where(c => c.Length > 0).ToList();
        return codes.Count == 0 ? "" : "&f_WT=" + string.Join(",", codes);
    }

    public static IReadOnlyList<(string Id, JobPosting Job)> ParseList(string html, DateTimeOffset notBefore)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new List<(string, JobPosting)>();
        var cards = doc.DocumentNode.SelectNodes("//div[@data-entity-urn]");
        if (cards is null) return result;
        foreach (var card in cards)
        {
            var urn = card.GetAttributeValue("data-entity-urn", "");
            var id = urn[(urn.LastIndexOf(':') + 1)..];
            if (id.Length == 0) continue;
            var link = card.SelectSingleNode(".//a[contains(@class,'base-card__full-link')]")?.GetAttributeValue("href", "") ?? "";
            var qmark = link.IndexOf('?');
            if (qmark >= 0) link = link[..qmark];
            string Text(string cls) => HtmlEntity.DeEntitize(
                card.SelectSingleNode($".//*[contains(@class,'{cls}')]")?.InnerText ?? "").Trim();
            // The public page only has a date. LinkedIn filtered by time server-side, so place the posting inside the window.
            result.Add((id, new JobPosting("LinkedIn (public pages)", Text("base-search-card__title"),
                Text("base-search-card__subtitle"), Text("job-search-card__location"), WorkType.Unknown,
                notBefore.AddMinutes(1), null, link, "")));
        }
        return result;
    }

    public static int? ParseApplicantCount(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var caption = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'num-applicants__caption')]")?.InnerText;
        if (caption is null) return null;
        var m = Number.Match(caption);
        if (!m.Success) return null;
        var n = int.Parse(m.Groups[1].Value.Replace(",", ""));
        return caption.Contains("Over", StringComparison.OrdinalIgnoreCase) ? n + 1 : n;
    }
}
```

Note: `ParseList` sets `WorkType.Unknown` and the request's `f_WT` filter already restricted type, so `FilterEngine` keeps Unknown postings.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter LinkedInPublicSourceTests`
Expected: all pass. The `PostedAt` test accepts `notBefore + 1 minute`, which is `<= Now`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): optional LinkedIn public-page source (off by default)

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 9: SearchService

**Files:**
- Create: `src/JobScreener.Core/Search/SearchResult.cs`, `src/JobScreener.Core/Search/SearchService.cs`
- Test: `tests/JobScreener.Tests/SearchServiceTests.cs`

**Interfaces:**
- Consumes: `IJobSource`, `FilterEngine.Apply`, `MatchScorer.Score`, `TimeProvider`, `SourceBlockedException`, test `FakeSource`, `FixedTime`.
- Produces:
  - `record ScoredJob(JobPosting Job, int Match)`
  - `record SourceWarning(string Source, string Message)`
  - `record SearchResult(IReadOnlyList<ScoredJob> Jobs, IReadOnlyList<SourceWarning> Warnings, IReadOnlyDictionary<string,int> RawCountBySource)`
  - `SearchService(IEnumerable<IJobSource> sources, TimeProvider time)` with `Task<SearchResult> RunAsync(SearchRequest request, IReadOnlyCollection<string> resumeSkills, CancellationToken ct, TimeSpan? perSourceTimeout = null)` and `IReadOnlyList<string> SourceNames`.

Behaviour: run selected sources in parallel (all registered when `request.Sources` is null or empty), per-source timeout default 30 s, a failing or blocked or timed-out source becomes a warning and the others still return. Merge, de-duplicate by `Url`, filter, score, sort by match descending then `PostedAt` descending.

- [ ] **Step 1: Write the failing tests**

`tests/JobScreener.Tests/SearchServiceTests.cs`:

```csharp
using JobScreener.Core.Models;
using JobScreener.Core.Search;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public class SearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static JobPosting Job(string url, string title = ".NET Developer", string description = "C# Azure", int minutesAgo = 5) =>
        new("S", title, "Acme", "Worldwide", WorkType.Remote, Now.AddMinutes(-minutesAgo), null, url, description);

    private static SearchRequest Request(IReadOnlyList<string>? sources = null) =>
        new(new[] { ".NET Developer" }, new[] { WorkType.Remote }, "Worldwide", new LookBack(1, LookBackUnit.Hours), null, true, sources);

    private static SearchService Service(params IJobSource[] sources) => new(sources, new FixedTime(Now));

    [Fact]
    public async Task Merges_filters_deduplicates_and_sorts_by_match()
    {
        var a = FakeSource.Returning("A", Job("https://x/1", description: "Python"), Job("https://x/2", description: "C# Azure"));
        var b = FakeSource.Returning("B", Job("https://x/2", description: "C# Azure"), Job("https://x/3", minutesAgo: 500));
        var result = await Service(a, b).RunAsync(Request(), new[] { "C#", "Azure" }, CancellationToken.None);

        Assert.Equal(new[] { "https://x/2", "https://x/1" }, result.Jobs.Select(j => j.Job.Url));
        Assert.Equal(100, result.Jobs[0].Match);
        Assert.Equal(2, result.RawCountBySource["A"]);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task A_failing_source_becomes_a_warning_and_others_still_return()
    {
        var good = FakeSource.Returning("Good", Job("https://x/1"));
        var bad = new FakeSource("Bad", (_, _) => throw new HttpRequestException("boom"));
        var blocked = new FakeSource("Blocked", (_, _) => throw new SourceBlockedException("HTTP 429"));
        var result = await Service(good, bad, blocked).RunAsync(Request(), Array.Empty<string>(), CancellationToken.None);

        Assert.Single(result.Jobs);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Source == "Bad" && w.Message.Contains("boom"));
        Assert.Contains(result.Warnings, w => w.Source == "Blocked" && w.Message.Contains("429"));
    }

    [Fact]
    public async Task A_slow_source_times_out_with_a_warning()
    {
        var slow = new FakeSource("Slow", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Array.Empty<JobPosting>(); });
        var fast = FakeSource.Returning("Fast", Job("https://x/1"));
        var result = await Service(slow, fast).RunAsync(Request(), Array.Empty<string>(), CancellationToken.None,
            perSourceTimeout: TimeSpan.FromMilliseconds(100));

        Assert.Single(result.Jobs);
        Assert.Contains(result.Warnings, w => w.Source == "Slow" && w.Message.Contains("timed out"));
    }

    [Fact]
    public async Task Only_selected_sources_run()
    {
        var a = FakeSource.Returning("A", Job("https://x/1"));
        var b = FakeSource.Returning("B", Job("https://x/2"));
        var result = await Service(a, b).RunAsync(Request(new[] { "B" }), Array.Empty<string>(), CancellationToken.None);
        Assert.Equal("https://x/2", Assert.Single(result.Jobs).Job.Url);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeSource("Slow", async (_, ct) => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return Array.Empty<JobPosting>(); });
        var task = Service(slow).RunAsync(Request(), Array.Empty<string>(), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter SearchServiceTests`
Expected: build FAIL, `SearchService` not found.

- [ ] **Step 3: Implement**

`src/JobScreener.Core/Search/SearchResult.cs`:

```csharp
using JobScreener.Core.Models;

namespace JobScreener.Core.Search;

public sealed record ScoredJob(JobPosting Job, int Match);
public sealed record SourceWarning(string Source, string Message);
public sealed record SearchResult(
    IReadOnlyList<ScoredJob> Jobs,
    IReadOnlyList<SourceWarning> Warnings,
    IReadOnlyDictionary<string, int> RawCountBySource);
```

`src/JobScreener.Core/Search/SearchService.cs`:

```csharp
using JobScreener.Core.Filtering;
using JobScreener.Core.Matching;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Core.Search;

public sealed class SearchService(IEnumerable<IJobSource> sources, TimeProvider time)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<IJobSource> _sources = sources.ToList();

    public IReadOnlyList<string> SourceNames => _sources.Select(s => s.Name).ToList();

    public async Task<SearchResult> RunAsync(SearchRequest request, IReadOnlyCollection<string> resumeSkills,
        CancellationToken ct, TimeSpan? perSourceTimeout = null)
    {
        var timeout = perSourceTimeout ?? DefaultTimeout;
        var selected = _sources
            .Where(s => request.Sources is null || request.Sources.Count == 0 ||
                        request.Sources.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var outcomes = await Task.WhenAll(selected.Select(s => RunOneAsync(s, request, timeout, ct)));

        var warnings = outcomes.Where(o => o.Warning is not null).Select(o => new SourceWarning(o.Name, o.Warning!)).ToList();
        var raw = outcomes.ToDictionary(o => o.Name, o => o.Jobs.Count);
        var merged = outcomes.SelectMany(o => o.Jobs).GroupBy(j => j.Url).Select(g => g.First());

        var now = time.GetUtcNow();
        var scored = FilterEngine.Apply(merged, request, now)
            .Select(j => new ScoredJob(j, MatchScorer.Score(resumeSkills, request.Titles, j)))
            .OrderByDescending(s => s.Match).ThenByDescending(s => s.Job.PostedAt)
            .ToList();
        return new SearchResult(scored, warnings, raw);
    }

    private static async Task<(string Name, IReadOnlyList<JobPosting> Jobs, string? Warning)> RunOneAsync(
        IJobSource source, SearchRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return (source.Name, await source.SearchAsync(request, cts.Token), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (source.Name, Array.Empty<JobPosting>(), $"{source.Name} timed out after {timeout.TotalSeconds:0.#}s.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (source.Name, Array.Empty<JobPosting>(), $"{source.Name} failed: {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test --filter SearchServiceTests`
Expected: all pass. The `FakeSource` constructor lambda in the "failing source" test throws synchronously inside `SearchAsync`; `RunOneAsync` still catches it because the call is inside the `try`.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): SearchService orchestrating sources, filters and scoring

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Persistence, handle normalizer and CSV export

**Files:**
- Create: `src/JobScreener.Core/Data/HandleNormalizer.cs`, `Applicant.cs`, `SearchRun.cs`, `AppDbContext.cs`, `ApplicantStore.cs`; `src/JobScreener.Core/Export/CsvExport.cs`
- Modify: `src/JobScreener.Core/JobScreener.Core.csproj`
- Test: `tests/JobScreener.Tests/DataTests.cs`

**Interfaces:**
- Consumes: `SearchRequest`, `SourceWarning`, `ScoredJob`.
- Produces:
  - `HandleNormalizer.Normalize(string input) : string` (lowercase handle; accepts `siradakole`, `@siradakole`, `in/siradakole`, `https://www.linkedin.com/in/siradakole/?x=1`; throws `ArgumentException` for empty or invalid).
  - `Applicant { int Id; string Handle; string ResumeText; string SkillsCsv; DateTime CreatedUtc }`, `SearchRun { int Id; int ApplicantId; string FiltersJson; string WarningsJson; int ResultCount; DateTime StartedUtc }`.
  - `AppDbContext(DbContextOptions<AppDbContext>)` with `DbSet<Applicant> Applicants`, `DbSet<SearchRun> SearchRuns`.
  - `ApplicantStore(IDbContextFactory<AppDbContext> factory)`: `Task<Applicant> UpsertAsync(string handle, string resumeText, IReadOnlyList<string> skills)`, `Task<Applicant?> GetAsync(string handle)`, `Task RecordRunAsync(string handle, SearchRequest request, SearchResult result)`, `Task<bool> DeleteAsync(string handle)`.
  - `CsvExport.ToCsv(IEnumerable<ScoredJob> jobs) : string`.

- [ ] **Step 1: Add packages**

```bash
dotnet add src/JobScreener.Core package Microsoft.EntityFrameworkCore.Sqlite --version "8.0.*"
```

- [ ] **Step 2: Write the failing tests**

`tests/JobScreener.Tests/DataTests.cs`:

```csharp
using JobScreener.Core.Data;
using JobScreener.Core.Export;
using JobScreener.Core.Models;
using JobScreener.Core.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobScreener.Tests;

public class DataTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ApplicantStore _store;

    public DataTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using (var db = new AppDbContext(options)) db.Database.EnsureCreated();
        _store = new ApplicantStore(new TestFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TestFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Theory]
    [InlineData("siradakole", "siradakole")]
    [InlineData("@SiradaKole", "siradakole")]
    [InlineData("in/siradakole", "siradakole")]
    [InlineData("https://www.linkedin.com/in/siradakole/?isSelfProfile=true", "siradakole")]
    [InlineData("linkedin.com/in/samuel-adakole-8b8679168", "samuel-adakole-8b8679168")]
    public void Normalize_extracts_the_handle(string input, string expected)
    {
        Assert.Equal(expected, HandleNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a b")]
    [InlineData("https://example.com/")]
    public void Normalize_rejects_invalid_input(string input)
    {
        Assert.Throws<ArgumentException>(() => HandleNormalizer.Normalize(input));
    }

    [Fact]
    public async Task Upsert_creates_then_updates_the_same_applicant()
    {
        var first = await _store.UpsertAsync("Sam", "resume one", new[] { "C#" });
        var second = await _store.UpsertAsync("sam", "resume two", new[] { "C#", "Azure" });
        Assert.Equal(first.Id, second.Id);
        var loaded = await _store.GetAsync("SAM");
        Assert.Equal("resume two", loaded!.ResumeText);
        Assert.Equal("C#,Azure", loaded.SkillsCsv);
    }

    [Fact]
    public async Task Delete_removes_the_applicant_resume_and_runs()
    {
        await _store.UpsertAsync("sam", "resume", new[] { "C#" });
        var request = new SearchRequest(new[] { "x" }, Array.Empty<WorkType>(), "Worldwide", new LookBack(1, LookBackUnit.Hours), null);
        await _store.RecordRunAsync("sam", request, new SearchResult(Array.Empty<ScoredJob>(), Array.Empty<SourceWarning>(), new Dictionary<string, int>()));

        Assert.True(await _store.DeleteAsync("sam"));
        Assert.Null(await _store.GetAsync("sam"));
        Assert.False(await _store.DeleteAsync("sam"));
    }

    [Fact]
    public void Csv_quotes_commas_quotes_and_newlines()
    {
        var job = new JobPosting("Src", "Dev, Senior", "Acme \"Co\"", "Remote", WorkType.Remote,
            new DateTimeOffset(2026, 10, 14, 11, 0, 0, TimeSpan.Zero), null, "https://x/1", "d");
        var csv = CsvExport.ToCsv(new[] { new ScoredJob(job, 87) });
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Match,Title,Company,Location,Type,Posted (UTC),Applicants,Source,Url", lines[0]);
        Assert.Equal("87,\"Dev, Senior\",\"Acme \"\"Co\"\"\",Remote,Remote,2026-10-14 11:00,,Src,https://x/1", lines[1]);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter DataTests`
Expected: build FAIL, `HandleNormalizer` not found.

- [ ] **Step 4: Implement**

`src/JobScreener.Core/Data/HandleNormalizer.cs`:

```csharp
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
```

`src/JobScreener.Core/Data/Applicant.cs`:

```csharp
namespace JobScreener.Core.Data;

public sealed class Applicant
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string ResumeText { get; set; } = "";
    public string SkillsCsv { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
```

`src/JobScreener.Core/Data/SearchRun.cs`:

```csharp
namespace JobScreener.Core.Data;

public sealed class SearchRun
{
    public int Id { get; set; }
    public int ApplicantId { get; set; }
    public string FiltersJson { get; set; } = "";
    public string WarningsJson { get; set; } = "";
    public int ResultCount { get; set; }
    public DateTime StartedUtc { get; set; }
}
```

`src/JobScreener.Core/Data/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace JobScreener.Core.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<SearchRun> SearchRuns => Set<SearchRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Applicant>().HasIndex(a => a.Handle).IsUnique();
        b.Entity<SearchRun>().HasIndex(r => r.ApplicantId);
    }
}
```

`src/JobScreener.Core/Data/ApplicantStore.cs`:

```csharp
using System.Text.Json;
using JobScreener.Core.Models;
using JobScreener.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace JobScreener.Core.Data;

public sealed class ApplicantStore(IDbContextFactory<AppDbContext> factory)
{
    public async Task<Applicant> UpsertAsync(string handle, string resumeText, IReadOnlyList<string> skills)
    {
        var key = HandleNormalizer.Normalize(handle);
        await using var db = await factory.CreateDbContextAsync();
        var applicant = await db.Applicants.SingleOrDefaultAsync(a => a.Handle == key);
        if (applicant is null)
        {
            applicant = new Applicant { Handle = key, CreatedUtc = DateTime.UtcNow };
            db.Applicants.Add(applicant);
        }
        applicant.ResumeText = resumeText;
        applicant.SkillsCsv = string.Join(",", skills);
        await db.SaveChangesAsync();
        return applicant;
    }

    public async Task<Applicant?> GetAsync(string handle)
    {
        var key = HandleNormalizer.Normalize(handle);
        await using var db = await factory.CreateDbContextAsync();
        return await db.Applicants.AsNoTracking().SingleOrDefaultAsync(a => a.Handle == key);
    }

    public async Task RecordRunAsync(string handle, SearchRequest request, SearchResult result)
    {
        var key = HandleNormalizer.Normalize(handle);
        await using var db = await factory.CreateDbContextAsync();
        var applicant = await db.Applicants.SingleOrDefaultAsync(a => a.Handle == key);
        if (applicant is null) return;
        db.SearchRuns.Add(new SearchRun
        {
            ApplicantId = applicant.Id,
            FiltersJson = JsonSerializer.Serialize(new
            {
                request.Titles, request.WorkTypes, request.Location,
                LookBack = request.LookBack.ToString(), request.MaxApplicants
            }),
            WarningsJson = JsonSerializer.Serialize(result.Warnings),
            ResultCount = result.Jobs.Count,
            StartedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string handle)
    {
        var key = HandleNormalizer.Normalize(handle);
        await using var db = await factory.CreateDbContextAsync();
        var applicant = await db.Applicants.SingleOrDefaultAsync(a => a.Handle == key);
        if (applicant is null) return false;
        db.SearchRuns.RemoveRange(db.SearchRuns.Where(r => r.ApplicantId == applicant.Id));
        db.Applicants.Remove(applicant);
        await db.SaveChangesAsync();
        return true;
    }
}
```

`src/JobScreener.Core/Export/CsvExport.cs`:

```csharp
using System.Text;
using JobScreener.Core.Search;

namespace JobScreener.Core.Export;

public static class CsvExport
{
    public static string ToCsv(IEnumerable<ScoredJob> jobs)
    {
        var sb = new StringBuilder("Match,Title,Company,Location,Type,Posted (UTC),Applicants,Source,Url\r\n");
        foreach (var s in jobs)
        {
            var j = s.Job;
            sb.Append(string.Join(",", new[]
            {
                s.Match.ToString(), Q(j.Title), Q(j.Company), Q(j.Location), j.WorkType.ToString(),
                j.PostedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm"), j.ApplicantCount?.ToString() ?? "",
                Q(j.Source), Q(j.Url)
            })).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Q(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test --filter DataTests`
Expected: all pass. The test project needs `Microsoft.Data.Sqlite`, which arrives transitively through `Microsoft.EntityFrameworkCore.Sqlite` via the project reference; if the `using` fails, run `dotnet add tests/JobScreener.Tests package Microsoft.Data.Sqlite --version "8.0.*"`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(core): SQLite persistence, handle normalizer and CSV export

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Blazor Server web app

**Files:**
- Create: `src/JobScreener.Web/JobScreener.Web.csproj` (via `dotnet new web`), `Program.cs`, `appsettings.json`, `State/AppState.cs`, `Components/App.razor`, `Components/Routes.razor`, `Components/_Imports.razor`, `Components/Layout/MainLayout.razor`, `Components/Pages/Home.razor`, `Components/Pages/Search.razor`, `wwwroot/css/app.css`, `wwwroot/js/app.js`
- Test: `tests/JobScreener.Tests/WebSmokeTests.cs`

**Interfaces:**
- Consumes everything in Core. Produces the running app at `/` (Home) and `/search`, health check at `/healthz`.

`appsettings.json` keys: `ConnectionStrings:Default`, `Sources:Remotive`, `Sources:RemoteOk`, `Sources:Arbeitnow` (default true), `Sources:Adzuna:Enabled/AppId/AppKey/Country`, `Sources:LinkedIn:Enabled` (default false), `Sources:LinkedIn:RequestDelaySeconds` (default 2).

- [ ] **Step 1: Scaffold the web project**

```bash
dotnet new web -n JobScreener.Web -o src/JobScreener.Web -f net8.0
dotnet sln add src/JobScreener.Web
dotnet add src/JobScreener.Web reference src/JobScreener.Core
dotnet add tests/JobScreener.Tests reference src/JobScreener.Web
dotnet add tests/JobScreener.Tests package Microsoft.AspNetCore.Mvc.Testing --version "8.0.*"
```

Edit `tests/JobScreener.Tests/JobScreener.Tests.csproj` so the first line is `<Project Sdk="Microsoft.NET.Sdk">` (unchanged) and no extra SDK is needed; `Microsoft.AspNetCore.Mvc.Testing` brings the web test host.

- [ ] **Step 2: Write the failing smoke test**

`tests/JobScreener.Tests/WebSmokeTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobScreener.Tests;

public class WebSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Home_page_renders_the_handle_form()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("LinkedIn handle", html);
        Assert.Contains("Upload", html);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --filter WebSmokeTests`
Expected: FAIL (no `Program` accessible, no routes).

- [ ] **Step 4: Write the app shell**

`src/JobScreener.Web/appsettings.json`:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "ConnectionStrings": { "Default": "Data Source=jobscreener.db" },
  "Sources": {
    "Remotive": true,
    "RemoteOk": true,
    "Arbeitnow": true,
    "Adzuna": { "Enabled": false, "AppId": "", "AppKey": "", "Country": "gb" },
    "LinkedIn": { "Enabled": false, "RequestDelaySeconds": 2 }
  }
}
```

`src/JobScreener.Web/Program.cs`:

```csharp
using JobScreener.Core.Data;
using JobScreener.Core.Search;
using JobScreener.Core.Sources;
using JobScreener.Web.Components;
using JobScreener.Web.State;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=jobscreener.db"));
builder.Services.AddSingleton<ApplicantStore>();
builder.Services.AddScoped<AppState>();

// Sources: open APIs on by default; Adzuna needs keys; LinkedIn is opt-in only.
if (config.GetValue("Sources:Remotive", true))
    builder.Services.AddHttpClient<IJobSource, RemotiveSource>();
if (config.GetValue("Sources:RemoteOk", true))
    builder.Services.AddHttpClient<IJobSource, RemoteOkSource>();
if (config.GetValue("Sources:Arbeitnow", true))
    builder.Services.AddHttpClient<IJobSource, ArbeitnowSource>();
if (config.GetValue("Sources:Adzuna:Enabled", false))
{
    var adzuna = new AdzunaOptions(config["Sources:Adzuna:AppId"] ?? "", config["Sources:Adzuna:AppKey"] ?? "",
        config["Sources:Adzuna:Country"] ?? "gb");
    builder.Services.AddSingleton(adzuna);
    builder.Services.AddHttpClient<IJobSource, AdzunaSource>();
}
if (config.GetValue("Sources:LinkedIn:Enabled", false))
{
    builder.Services.AddSingleton(new LinkedInOptions(
        TimeSpan.FromSeconds(config.GetValue("Sources:LinkedIn:RequestDelaySeconds", 2))));
    builder.Services.AddHttpClient<IJobSource, LinkedInPublicSource>();
}
builder.Services.AddScoped<SearchService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
```

`src/JobScreener.Web/State/AppState.cs`:

```csharp
namespace JobScreener.Web.State;

/// <summary>Per-browser-circuit state shared between the Home and Search pages.</summary>
public sealed class AppState
{
    public string Handle { get; set; } = "";
    public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SuggestedTitles { get; set; } = Array.Empty<string>();
    public int? YearsExperience { get; set; }
    public bool HasProfile => Handle.Length > 0;
}
```

`src/JobScreener.Web/Components/_Imports.razor`:

```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.JSInterop
@using JobScreener.Web
@using JobScreener.Web.Components
@using JobScreener.Web.State
@using JobScreener.Core.Models
```

`src/JobScreener.Web/Components/App.razor`:

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Job-Screener</title>
    <base href="/" />
    <link rel="stylesheet" href="css/app.css" />
    <HeadOutlet @rendermode="InteractiveServer" />
</head>
<body>
    <Routes @rendermode="InteractiveServer" />
    <script src="js/app.js"></script>
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

`src/JobScreener.Web/Components/Routes.razor`:

```razor
<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
    </Found>
    <NotFound>
        <Layout.MainLayout><p>Page not found.</p></Layout.MainLayout>
    </NotFound>
</Router>
```

`src/JobScreener.Web/Components/Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase

<header class="top">
    <a class="brand" href="/">Job-Screener</a>
    <nav><a href="/">Profile</a><a href="/search">Search</a></nav>
</header>
<main>@Body</main>
<footer>
    Jobs come from
    <a href="https://remotive.com" target="_blank" rel="noopener">Remotive</a>,
    <a href="https://remoteok.com" target="_blank" rel="noopener">Remote OK</a>,
    <a href="https://www.arbeitnow.com" target="_blank" rel="noopener">Arbeitnow</a>
    and optionally Adzuna. Every result links back to its original posting.
    Match percent is a skills overlap with your resume, not a prediction.
</footer>
```

`src/JobScreener.Web/wwwroot/js/app.js`:

```javascript
window.downloadText = (filename, text) => {
    const blob = new Blob([text], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = filename; a.click();
    URL.revokeObjectURL(url);
};
```

`src/JobScreener.Web/wwwroot/css/app.css`:

```css
:root { --ink:#16231d; --muted:#51615a; --line:#cbd6cf; --paper:#f2f5f2; --green:#0a7b4f; --warn:#8a5a00; }
* { box-sizing: border-box; }
body { margin:0; font-family: system-ui, -apple-system, "Segoe UI", sans-serif; color:var(--ink); background:var(--paper); line-height:1.5; }
.top { display:flex; justify-content:space-between; align-items:center; padding:.8rem 1.2rem; border-bottom:1px solid var(--line); background:#fff; }
.top nav a { margin-left:1rem; color:var(--green); text-decoration:none; }
.brand { font-weight:700; color:var(--ink); text-decoration:none; }
main { max-width: 1100px; margin: 0 auto; padding: 1.2rem; }
footer { max-width:1100px; margin:2rem auto; padding:0 1.2rem; color:var(--muted); font-size:.85rem; }
h1 { margin:.2rem 0 1rem; } label { display:block; font-weight:600; margin:.6rem 0 .2rem; }
input[type=text], input[type=number], select { padding:.45rem .6rem; border:1px solid var(--line); border-radius:6px; font:inherit; }
button { padding:.5rem .9rem; border:0; border-radius:6px; background:var(--green); color:#fff; font:inherit; cursor:pointer; }
button.secondary { background:#fff; color:var(--green); border:1px solid var(--green); }
button.danger { background:#fff; color:#a12; border:1px solid #a12; }
button:disabled { opacity:.5; cursor:default; }
.card { background:#fff; border:1px solid var(--line); border-radius:8px; padding:1rem; margin-bottom:1rem; }
.chips { display:flex; flex-wrap:wrap; gap:.4rem; margin:.4rem 0; }
.chip { background:#e4efe9; border-radius:999px; padding:.15rem .65rem; display:inline-flex; gap:.4rem; align-items:center; }
.chip button { background:none; color:var(--ink); padding:0 .2rem; }
.row { display:flex; flex-wrap:wrap; gap:1rem; } .row > div { min-width:200px; }
.warn { color:var(--warn); } .muted { color:var(--muted); }
table { border-collapse:collapse; width:100%; background:#fff; }
th, td { text-align:left; padding:.45rem .6rem; border-bottom:1px solid var(--line); vertical-align:top; }
.match { font-weight:700; }
.badge { font-size:.75rem; background:#eee; border-radius:4px; padding:0 .35rem; color:var(--muted); }
```

- [ ] **Step 5: Write the Home page**

`src/JobScreener.Web/Components/Pages/Home.razor`:

```razor
@page "/"
@using JobScreener.Core.Data
@using JobScreener.Core.Resume
@inject AppState State
@inject ApplicantStore Store
@inject NavigationManager Nav

<h1>Find jobs that fit your resume</h1>

<div class="card">
    <label for="handle">LinkedIn handle</label>
    <input id="handle" type="text" @bind="handle" placeholder="siradakole or linkedin.com/in/siradakole" size="40" />
    <p class="muted">Used as your identity in this app. Job-Screener does not read your LinkedIn profile.</p>

    <label for="resume">Resume (PDF or DOCX, up to 5 MB)</label>
    <InputFile id="resume" OnChange="OnFileChosen" accept=".pdf,.docx" />
    <p>@status</p>
    @if (error is not null) { <p class="warn">@error</p> }
</div>

@if (parsed is not null)
{
    <div class="card">
        <h2>What we found</h2>
        <p><strong>Skills:</strong> @(parsed.Skills.Count == 0 ? "none recognised" : string.Join(", ", parsed.Skills))</p>
        <p><strong>Suggested job titles:</strong> @(parsed.SuggestedTitles.Count == 0 ? "none" : string.Join(", ", parsed.SuggestedTitles))</p>
        @if (parsed.YearsExperience is not null) { <p><strong>Experience mentioned:</strong> @parsed.YearsExperience years</p> }
        <label for="extra">Add skills the resume missed (comma separated)</label>
        <input id="extra" type="text" @bind="extraSkills" size="50" />
        <p><button @onclick="SaveAndContinue" disabled="@(handle.Length == 0)">Continue to search</button></p>
    </div>
}

@if (State.HasProfile)
{
    <div class="card">
        <p>Saved profile: <strong>@State.Handle</strong></p>
        <button class="danger" @onclick="DeleteData">Delete my data</button>
        @if (deleted) { <span class="muted"> Your data was deleted.</span> }
    </div>
}

@code {
    private string handle = "";
    private string extraSkills = "";
    private string status = "";
    private string? error;
    private bool deleted;
    private ParsedResume? parsed;

    protected override void OnInitialized() => handle = State.Handle;

    private async Task OnFileChosen(InputFileChangeEventArgs e)
    {
        error = null; deleted = false;
        try
        {
            await using var stream = e.File.OpenReadStream(5 * 1024 * 1024);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            var text = ResumeParser.ExtractText(buffer, e.File.Name);
            if (string.IsNullOrWhiteSpace(text)) { error = "No text found in that file. Is it a scanned image?"; return; }
            parsed = ResumeParser.Parse(text);
            status = $"Read {e.File.Name}.";
        }
        catch (NotSupportedException ex) { error = ex.Message; }
        catch (IOException) { error = "The file is too large (limit 5 MB)."; }
        catch (Exception) { error = "Could not read that file. Try another PDF or DOCX."; }
    }

    private async Task SaveAndContinue()
    {
        error = null;
        try
        {
            var key = HandleNormalizer.Normalize(handle);
            var skills = parsed!.Skills
                .Concat(extraSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            await Store.UpsertAsync(key, parsed.Text, skills);
            State.Handle = key;
            State.Skills = skills;
            State.SuggestedTitles = parsed.SuggestedTitles;
            State.YearsExperience = parsed.YearsExperience;
            Nav.NavigateTo("/search");
        }
        catch (ArgumentException ex) { error = ex.Message; }
    }

    private async Task DeleteData()
    {
        await Store.DeleteAsync(State.Handle);
        State.Handle = ""; State.Skills = Array.Empty<string>(); State.SuggestedTitles = Array.Empty<string>();
        parsed = null; deleted = true;
    }
}
```

- [ ] **Step 6: Write the Search page**

`src/JobScreener.Web/Components/Pages/Search.razor`:

```razor
@page "/search"
@using JobScreener.Core.Data
@using JobScreener.Core.Export
@using JobScreener.Core.Search
@inject AppState State
@inject SearchService Searcher
@inject ApplicantStore Store
@inject NavigationManager Nav
@inject IJSRuntime Js

@if (!State.HasProfile)
{
    <h1>Search</h1>
    <p>Start on the <a href="/">profile page</a> to enter your handle and upload a resume.</p>
    return;
}

<h1>Search jobs</h1>
<p class="muted">Profile: <strong>@State.Handle</strong> · @State.Skills.Count skills</p>

<div class="card">
    <label>Job titles</label>
    <div class="chips">
        @foreach (var t in titles)
        {
            <span class="chip">@t <button title="Remove" @onclick="() => titles.Remove(t)">×</button></span>
        }
    </div>
    <input type="text" @bind="newTitle" @bind:event="oninput" @onkeydown="OnTitleKey" placeholder="Add a job title, press Enter" size="40" />
    <button class="secondary" @onclick="AddTitle">Add</button>
    @if (State.SuggestedTitles.Any(s => !titles.Contains(s)))
    {
        <p class="muted">Suggested:
            @foreach (var s in State.SuggestedTitles.Where(s => !titles.Contains(s)))
            { <button class="secondary" @onclick="() => titles.Add(s)">+ @s</button> }
        </p>
    }

    <div class="row">
        <div>
            <label>Look back</label>
            <input type="number" min="1" @bind="amount" style="width:80px" />
            <select @bind="unit">
                @foreach (var u in Enum.GetValues<LookBackUnit>()) { <option value="@u">@u</option> }
            </select>
        </div>
        <div>
            <label>Job type</label>
            @foreach (var t in new[] { WorkType.Remote, WorkType.OnSite, WorkType.Hybrid })
            {
                <label style="display:inline;font-weight:400"><input type="checkbox" checked="@types.Contains(t)" @onchange="e => ToggleType(t, (bool)e.Value!)" /> @t &nbsp;</label>
            }
        </div>
        <div>
            <label>Location</label>
            <input type="text" @bind="location" placeholder="Worldwide, Nigeria, Berlin..." />
        </div>
        <div>
            <label>Max applicants</label>
            <input type="number" min="1" @bind="maxApplicants" style="width:100px" />
            <label style="font-weight:400"><input type="checkbox" @bind="includeUnknown" /> include jobs with unknown applicant count</label>
        </div>
    </div>

    <label>Sources</label>
    @foreach (var s in Searcher.SourceNames)
    {
        <label style="display:inline;font-weight:400"><input type="checkbox" checked="@sources.Contains(s)" @onchange="e => ToggleSource(s, (bool)e.Value!)" /> @s &nbsp;</label>
    }
    @if (Searcher.SourceNames.Any(n => n.StartsWith("LinkedIn")))
    {
        <p class="warn">LinkedIn public pages are enabled on this server. Scraping LinkedIn is against its terms of service and may be blocked at any time. Use at your own risk.</p>
    }

    <p><button @onclick="Run" disabled="@running">@(running ? "Searching..." : "Search")</button></p>
    @if (error is not null) { <p class="warn">@error</p> }
</div>

@if (result is not null)
{
    @foreach (var w in result.Warnings) { <p class="warn">⚠ @w.Source: @w.Message</p> }
    <p class="muted">Fetched @string.Join(", ", result.RawCountBySource.Select(kv => $"{kv.Key} {kv.Value}")) postings. @shown.Count of @result.Jobs.Count shown.</p>

    <label>Minimum skills overlap: @minMatch%</label>
    <input type="range" min="0" max="100" step="5" @bind="minMatch" @bind:event="oninput" />
    <button class="secondary" @onclick="ExportCsv" disabled="@(shown.Count == 0)">Export CSV</button>

    @if (shown.Count == 0)
    {
        <p>No jobs matched. @NoResultsHint()</p>
    }
    else
    {
        <table>
            <thead><tr><th>Skills overlap</th><th>Title</th><th>Company</th><th>Location</th><th>Posted</th><th>Applicants</th><th>Source</th></tr></thead>
            <tbody>
            @foreach (var s in shown)
            {
                <tr>
                    <td class="match">@s.Match%</td>
                    <td><a href="@s.Job.Url" target="_blank" rel="noopener noreferrer">@s.Job.Title</a>
                        @if (s.Job.WorkType == WorkType.Unknown) { <span class="badge">type unknown</span> }</td>
                    <td>@s.Job.Company</td>
                    <td>@s.Job.Location</td>
                    <td>@Ago(s.Job.PostedAt)</td>
                    <td>@(s.Job.ApplicantCount?.ToString() ?? "not available")</td>
                    <td>@s.Job.Source</td>
                </tr>
            }
            </tbody>
        </table>
    }
}

@code {
    private readonly List<string> titles = new();
    private readonly List<WorkType> types = new() { WorkType.Remote };
    private readonly HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
    private string newTitle = "";
    private int amount = 24;
    private LookBackUnit unit = LookBackUnit.Hours;
    private string location = "Worldwide";
    private int? maxApplicants = 100;
    private bool includeUnknown = true;
    private bool running;
    private string? error;
    private SearchResult? result;
    private int minMatch;
    private SearchRequest? lastRequest;

    private IReadOnlyList<ScoredJob> shown => result is null
        ? Array.Empty<ScoredJob>() : result.Jobs.Where(j => j.Match >= minMatch).ToList();

    protected override void OnInitialized()
    {
        foreach (var s in Searcher.SourceNames) sources.Add(s);
        titles.AddRange(State.SuggestedTitles.Take(2));
    }

    private void AddTitle()
    {
        var t = newTitle.Trim();
        if (t.Length > 0 && !titles.Contains(t, StringComparer.OrdinalIgnoreCase)) titles.Add(t);
        newTitle = "";
    }

    private void OnTitleKey(KeyboardEventArgs e) { if (e.Key == "Enter") AddTitle(); }
    private void ToggleType(WorkType t, bool on) { types.Remove(t); if (on) types.Add(t); }
    private void ToggleSource(string s, bool on) { if (on) sources.Add(s); else sources.Remove(s); }

    private async Task Run()
    {
        error = null;
        AddTitle();
        if (amount < 1) { error = "Look back must be at least 1."; return; }
        if (sources.Count == 0) { error = "Pick at least one source."; return; }
        running = true; result = null;
        try
        {
            lastRequest = new SearchRequest(titles.ToList(), types.ToList(), location, new LookBack(amount, unit),
                maxApplicants, includeUnknown, sources.ToList());
            result = await Searcher.RunAsync(lastRequest, State.Skills, CancellationToken.None);
            await Store.RecordRunAsync(State.Handle, lastRequest, result);
        }
        catch (Exception ex) { error = "Search failed: " + ex.Message; }
        finally { running = false; }
    }

    private async Task ExportCsv() =>
        await Js.InvokeVoidAsync("downloadText", $"jobs-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv", CsvExport.ToCsv(shown));

    private string NoResultsHint() =>
        result!.Jobs.Count > 0 ? "Lower the minimum skills overlap."
        : result.RawCountBySource.Values.Sum() == 0 ? "No source returned postings. Check the warnings above."
        : "Try a longer look-back, more job titles, or a higher max applicants.";

    private static string Ago(DateTimeOffset when)
    {
        var span = DateTimeOffset.UtcNow - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 48) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }
}
```

- [ ] **Step 7: Run the smoke tests and build**

Run: `dotnet build && dotnet test`
Expected: build succeeds, all tests pass including `WebSmokeTests`. If the Home test fails on missing text, check that `Home.razor` is the page at `/` and that the layout renders; the antiforgery middleware must be present (`app.UseAntiforgery()`).

- [ ] **Step 8: Run the app and check it by hand**

```bash
dotnet run --project src/JobScreener.Web --urls http://localhost:5080
```

Open `http://localhost:5080`. Check: the home page shows the handle field and upload; uploading `Samuel_Adakole_Resume.pdf` from `C:\Users\DELL\Desktop\Mis_Information\resume` lists C#, .NET, Azure, Hangfire and RabbitMQ; "Continue to search" opens `/search`; a search with titles `.NET Developer`, 7 days, Remote, Worldwide returns results with a skills-overlap percentage and links; Export CSV downloads a file; "Delete my data" clears the profile. Stop the app with Ctrl+C.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(web): Blazor Server UI with handle, resume upload, filters, results and CSV

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Docker, CI, README and publish

**Files:**
- Create: `Dockerfile`, `.dockerignore`, `.github/workflows/ci.yml`, `README.md`

- [ ] **Step 1: Write the Dockerfile**

`Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/JobScreener.Web -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 ConnectionStrings__Default="Data Source=/data/jobscreener.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobScreener.Web.dll"]
```

`.dockerignore`:

```
**/bin
**/obj
.git
*.db
```

- [ ] **Step 2: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - run: dotnet build --configuration Release
      - run: dotnet test --configuration Release --no-build
```

- [ ] **Step 3: Write the README**

`README.md` (replace the placeholders with real values only where shown):

````markdown
# Job-Screener

Search jobs across several sources, filtered by how far back to look, job titles, job type, location and maximum applicants, then ranked by how many of your resume's skills each posting mentions.

Built with ASP.NET Core 8, Blazor Server, EF Core (SQLite) and xUnit.

## How it works

1. Enter your LinkedIn handle (used only as your identity in the app) and upload a resume (PDF or DOCX).
2. The app reads your skills and suggests job titles.
3. Set the filters and search. Results show a **skills overlap** percentage, a link to the original posting, and a CSV export.

## Filters

| Filter | Notes |
|---|---|
| Look back | Hours, days or weeks |
| Job titles | Any number |
| Job type | Remote, On-site, Hybrid |
| Location | Free text. `Worldwide` means no restriction |
| Max applicants | Only some sources report applicant counts. Postings with an unknown count can be included or excluded |

## Job sources

| Source | Needs | Notes |
|---|---|---|
| Remotive | nothing | Remote jobs. Please follow Remotive's API terms |
| Remote OK | nothing | Remote jobs. Requires linking back and crediting Remote OK, which the app does |
| Arbeitnow | nothing | Mostly European jobs, some remote |
| Adzuna | free API key | Set `Sources__Adzuna__Enabled=true`, `Sources__Adzuna__AppId`, `Sources__Adzuna__AppKey` |
| LinkedIn public pages | opt-in | **Off by default. Often blocked.** See below |

Check each provider's current terms before running a public instance.

## LinkedIn plug-in (read this first)

LinkedIn has no public jobs API. The optional plug-in reads only LinkedIn's public, logged-out job pages, rate limits itself and stops at the first block. This is against LinkedIn's terms of service, can get your server's IP blocked and breaks when LinkedIn changes its pages. In testing, LinkedIn answered with HTTP 429 and an anti-bot challenge, so expect it not to work. It is intended for personal use only. Enable with `Sources__LinkedIn__Enabled=true`.

## Limits

- Only LinkedIn reports applicant counts, so the max-applicants filter mostly affects LinkedIn results.
- API feeds update slowly, so very short look-back windows return fewer jobs.
- The match percentage is keyword overlap between resume skills and posting text, not an AI judgement.
- Resumes are personal data. They are stored in the local SQLite database, never logged, and removed by "Delete my data".

## Run it

```bash
dotnet run --project src/JobScreener.Web --urls http://localhost:5080
```

Tests: `dotnet test`

Docker:

```bash
docker build -t job-screener .
docker run -p 8080:8080 -v jobscreener-data:/data job-screener
```

## Hosting

This is a server app, so it cannot run on GitHub Pages. Use any host that runs a .NET or Docker container (Azure App Service, Fly.io, Render, a VPS).

## Spec and plan

See `docs/superpowers/specs` and `docs/superpowers/plans`.
````

- [ ] **Step 4: Verify the build, tests and Docker file syntax**

Run: `dotnet build -c Release && dotnet test -c Release --no-build`
Expected: build succeeds, all tests pass.

If Docker is installed: `docker build -t job-screener .`. If it is not installed, skip this and rely on the CI build.

- [ ] **Step 5: Commit and push**

```bash
git add -A
git commit -m "chore: Dockerfile, CI workflow and README

Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
git push origin main
```

Expected: push succeeds. Confirm the GitHub Actions run on `https://github.com/siradakole/Job-Screener/actions` passes.

- [ ] **Step 6: Final checks**

- Open `https://github.com/siradakole/Job-Screener` and confirm the README renders and the repository is public.
- Confirm no secrets or `*.db` files are committed: `git ls-files | grep -E "\.db$|appsettings.*Development"` prints nothing.

---

## Self-Review

**Spec coverage**
- Five filters: look-back with unit (Task 1, 3, 11), titles chips (Task 11), job type (Task 3, 11), location (Task 3, 11), max applicants plus unknown toggle (Task 3, 11).
- Sources: Remotive, Remote OK, Arbeitnow (Task 6), Adzuna (Task 7), LinkedIn opt-in (Task 8), registration and config (Task 11).
- Resume upload, skills and suggested titles (Task 4, 11), match score labelled as skills overlap (Task 5, 11).
- Handle as identity only, no accounts, delete my data (Task 10, 11).
- CSV export (Task 10, 11), min-match slider and warnings panel (Task 11), no-results hint (Task 11).
- Error handling: bad resume, source timeout/failure/block, LinkedIn 429 (Tasks 4, 8, 9, 11).
- Tests per unit, integration test with a failing source, no live network (Tasks 1 to 10).
- Out of scope respected: no accounts, schedules, alerts, auto-apply, AI scoring.

**Spec deviation to note:** the spec said work-type unknown would be treated implicitly; the plan makes it explicit: `Unknown` postings are kept and badged "type unknown". The spec's LinkedIn rules are tightened with the verified 429 finding.

**Type consistency:** `SearchRequest` (Task 1) is used with the same positional parameters in Tasks 3, 6, 7, 8, 9, 10 and 11. `SearchResult`/`ScoredJob`/`SourceWarning` (Task 9) are used unchanged in Tasks 10 and 11. `IJobSource.Name` strings: `"Remotive"`, `"Remote OK"`, `"Arbeitnow"`, `"Adzuna"`, `"LinkedIn (public pages)"`; the UI selects by these names.

**Known risks for the implementer:** PdfPig builder API names can differ slightly between versions (adjust only the test helper); the LinkedIn markup is unverified live; Adzuna and Remotive terms need a manual read before any public hosting.
