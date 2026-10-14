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
