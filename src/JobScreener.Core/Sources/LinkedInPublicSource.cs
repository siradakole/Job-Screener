using System.Text.RegularExpressions;
using HtmlAgilityPack;
using JobScreener.Core.Models;

namespace JobScreener.Core.Sources;

public sealed record LinkedInOptions(TimeSpan RequestDelay, int MaxDetailLookups = 25, TimeSpan? DetailBudget = null)
{
    public TimeSpan EffectiveDetailBudget => DetailBudget ?? TimeSpan.FromSeconds(20);
}

/// <summary>
/// Optional, best-effort reader of LinkedIn's public (logged-out) job pages. Disabled by default.
/// Any 429, error status or page without job cards is reported as blocked and never retried.
/// </summary>
public sealed class LinkedInPublicSource(HttpClient http, LinkedInOptions options, TimeProvider time) : IJobSource
{
    private static readonly Regex Number = new(@"(\d[\d,]*)", RegexOptions.Compiled);

    public string Name => "LinkedIn (public pages)";

    public async Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var windowStart = request.LookBack.CutoffFrom(now);
        var seconds = (long)request.LookBack.ToTimeSpan().TotalSeconds;
        var byId = new Dictionary<string, JobPosting>();

        foreach (var title in request.Titles.DefaultIfEmpty(""))
        {
            var url = "https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search" +
                      $"?keywords={Uri.EscapeDataString(title)}&location={Uri.EscapeDataString(request.Location)}" +
                      $"&f_TPR=r{seconds}{WorkTypeFilter(request)}&start=0";
            var html = await GetAsync(url, ct);
            var items = ParseList(html, windowStart);
            if (items.Count == 0) throw new SourceBlockedException("LinkedIn returned no job cards (blocked or no results).");
            foreach (var (id, job) in items) byId[id] = job;
            await Task.Delay(options.RequestDelay, ct);
        }

        if (request.MaxApplicants is not null)
        {
            var lookups = 0;
            var started = time.GetTimestamp();
            foreach (var id in byId.Keys.ToList())
            {
                if (lookups++ >= options.MaxDetailLookups) break;
                // Time budget so the whole source stays inside the per-source timeout; the rest keep a null count.
                if (time.GetElapsedTime(started) >= options.EffectiveDetailBudget) break;
                var html = await GetAsync($"https://www.linkedin.com/jobs-guest/jobs/api/jobPosting/{id}", ct);
                byId[id] = byId[id] with { ApplicantCount = ParseApplicantCount(html) };
                await Task.Delay(options.RequestDelay, ct);
            }
        }
        return byId.Values.ToList();
    }

    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Job-Screener/1.0; +https://github.com/siradakole/Job-Screener)");
        using var response = await http.SendAsync(message, ct);
        if ((int)response.StatusCode == 429) throw new SourceBlockedException("LinkedIn rate limit reached (HTTP 429).");
        if (!response.IsSuccessStatusCode) throw new SourceBlockedException($"LinkedIn returned HTTP {(int)response.StatusCode}.");
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string WorkTypeFilter(SearchRequest r)
    {
        var codes = r.WorkTypes.Select(t => t switch { WorkType.OnSite => "1", WorkType.Remote => "2", WorkType.Hybrid => "3", _ => "" })
            .Where(c => c.Length > 0).ToList();
        return codes.Count == 0 ? "" : "&f_WT=" + string.Join(",", codes);
    }

    public static IReadOnlyList<(string Id, JobPosting Job)> ParseList(string html, DateTimeOffset notBefore)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new List<(string, JobPosting)>();
        var cards = doc.DocumentNode.SelectNodes("//div[@data-entity-urn]");
        if (cards is null) return result;
        foreach (var card in cards)
        {
            var urn = card.GetAttributeValue("data-entity-urn", "");
            var id = urn[(urn.LastIndexOf(':') + 1)..];
            if (id.Length == 0) continue;
            var link = card.SelectSingleNode(".//a[contains(@class,'base-card__full-link')]")?.GetAttributeValue("href", "") ?? "";
            var qmark = link.IndexOf('?');
            if (qmark >= 0) link = link[..qmark];
            string Text(string cls) => HtmlEntity.DeEntitize(
                card.SelectSingleNode($".//*[contains(@class,'{cls}')]")?.InnerText ?? "").Trim();
            // The public page only has a date. LinkedIn filtered by time server-side, so place the posting inside the window.
            result.Add((id, new JobPosting("LinkedIn (public pages)", Text("base-search-card__title"),
                Text("base-search-card__subtitle"), Text("job-search-card__location"), WorkType.Unknown,
                notBefore.AddMinutes(5), null, link, "")));
        }
        return result;
    }

    public static int? ParseApplicantCount(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var caption = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'num-applicants__caption')]")?.InnerText;
        if (caption is null) return null;
        var m = Number.Match(caption);
        if (!m.Success) return null;
        var n = int.Parse(m.Groups[1].Value.Replace(",", ""));
        return caption.Contains("Over", StringComparison.OrdinalIgnoreCase) ? n + 1 : n;
    }
}
