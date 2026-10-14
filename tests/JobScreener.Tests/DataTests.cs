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
