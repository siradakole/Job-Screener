using System.Text;
using JobScreener.Core.Search;

namespace JobScreener.Core.Export;

public static class CsvExport
{
    public static string ToCsv(IEnumerable<ScoredJob> jobs)
    {
        var sb = new StringBuilder("Match,Title,Company,Location,Type,Posted (UTC),Applicants,Source,Url\r\n");
        foreach (var s in jobs)
        {
            var j = s.Job;
            sb.Append(string.Join(",", new[]
            {
                s.Match.ToString(), Q(j.Title), Q(j.Company), Q(j.Location), j.WorkType.ToString(),
                j.PostedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm"), j.ApplicantCount?.ToString() ?? "",
                Q(j.Source), Q(j.Url)
            })).Append("\r\n");
        }
        return sb.ToString();
    }

    private static string Q(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
