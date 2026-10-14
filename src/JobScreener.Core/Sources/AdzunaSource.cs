using System.Globalization;
using System.Text.Json;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed record AdzunaOptions(string AppId, string AppKey, string Country = "gb");

public sealed class AdzunaSource(HttpClient http, AdzunaOptions options) : IJobSource
{
    public string Name => "Adzuna";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var days = Math.Max(1, (int)Math.Ceiling(request.LookBack.ToTimeSpan().TotalDays));
        var worldwide = request.Location.Trim().Length == 0 ||
                        request.Location.Trim().Equals("worldwide", StringComparison.OrdinalIgnoreCase);
        var byUrl = new Dictionary<string, JobPosting>();
        foreach (var title in request.Titles.DefaultIfEmpty(""))
        {
            var url = $"https://api.adzuna.com/v1/api/jobs/{options.Country}/search/1" +
                      $"?app_id={Uri.EscapeDataString(options.AppId)}&app_key={Uri.EscapeDataString(options.AppKey)}" +
                      $"&results_per_page=50&max_days_old={days}&what={Uri.EscapeDataString(title)}" +
                      (worldwide ? "" : $"&where={Uri.EscapeDataString(request.Location)}");
            using var response = await http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429) throw new SourceBlockedException("Adzuna rate limit reached.");
            response.EnsureSuccessStatusCode();
            foreach (var job in Parse(await response.Content.ReadAsStringAsync(ct)))
                byUrl[job.Url] = job;
        }
        return byUrl.Values.ToList();
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results)) return Array.Empty<JobPosting>();
        var list = new List<JobPosting>();
        foreach (var j in results.EnumerateArray())
        {
            var title = JsonRead.Str(j, "title");
            var description = JsonRead.Str(j, "description");
            var company = j.TryGetProperty("company", out var c) ? JsonRead.Str(c, "display_name") : "";
            var location = j.TryGetProperty("location", out var l) ? JsonRead.Str(l, "display_name") : "";
            var posted = DateTimeOffset.TryParse(JsonRead.Str(j, "created"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var p) ? p : DateTimeOffset.MinValue;
            var remote = (title + " " + description).Contains("remote", StringComparison.OrdinalIgnoreCase);
            list.Add(new JobPosting("Adzuna", title, company, location, remote ? WorkType.Remote : WorkType.Unknown,
                posted, null, JsonRead.Str(j, "redirect_url"), description));
        }
        return list;
    }
}
