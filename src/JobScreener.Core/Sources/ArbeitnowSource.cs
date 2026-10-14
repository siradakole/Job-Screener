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
