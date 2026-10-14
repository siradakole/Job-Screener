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
