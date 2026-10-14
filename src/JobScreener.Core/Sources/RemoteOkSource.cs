using System.Text.Json;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed class RemoteOkSource(HttpClient http) : IJobSource
{
    public string Name => "Remote OK";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://remoteok.com/api");
        message.Headers.UserAgent.ParseAdd("Job-Screener/1.0 (+https://github.com/siradakole/Job-Screener)");
        using var response = await http.SendAsync(message, ct);
        if ((int)response.StatusCode == 429) throw new SourceBlockedException("Remote OK rate limit reached.");
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct));
    }

    public static IReadOnlyList<JobPosting> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<JobPosting>();
        foreach (var j in doc.RootElement.EnumerateArray())
        {
            if (!j.TryGetProperty("position", out _)) continue;   // the first element is a legal notice
            var tags = string.Join(", ", JsonRead.Strings(j, "tags"));
            var location = JsonRead.Str(j, "location");
            var description = JsonRead.Str(j, "description") + (tags.Length > 0 ? " Tags: " + tags : "");
            list.Add(new JobPosting("Remote OK", JsonRead.Str(j, "position"), JsonRead.Str(j, "company"),
                location.Length == 0 ? "Worldwide" : location, WorkType.Remote,
                DateTimeOffset.FromUnixTimeSeconds(JsonRead.Long(j, "epoch")), null,
                JsonRead.Str(j, "url"), description));
        }
        return list;
    }
}
