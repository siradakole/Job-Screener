namespace JobScreener.Core.Data;

public sealed class Applicant
{
    public int Id { get; set; }
    public string Handle { get; set; } = "";
    public string ResumeText { get; set; } = "";
    public string SkillsCsv { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
