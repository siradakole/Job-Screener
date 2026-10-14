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
