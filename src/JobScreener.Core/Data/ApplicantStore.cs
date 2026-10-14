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
