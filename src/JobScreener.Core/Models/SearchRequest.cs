namespace JobScreener.Core.Models;

public sealed record SearchRequest(
    IReadOnlyList<string> Titles,
    IReadOnlyList<WorkType> WorkTypes,
    string Location,
    LookBack LookBack,
    int? MaxApplicants,
    bool IncludeUnknownApplicants = true,
    IReadOnlyList<string>? Sources = null);
