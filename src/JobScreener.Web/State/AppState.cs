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
