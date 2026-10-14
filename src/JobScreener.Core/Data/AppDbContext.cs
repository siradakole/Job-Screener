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
