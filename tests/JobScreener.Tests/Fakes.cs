using System.Net;
using JobScreener.Core.Models;
using JobScreener.Core.Sources;

namespace JobScreener.Tests;

public sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }

    public static FakeHandler Json(string body) => new(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    });

    public static FakeHandler Status(HttpStatusCode code) => new(_ => new HttpResponseMessage(code));
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class FakeSource(string name, Func<SearchRequest, CancellationToken, Task<IReadOnlyList<JobPosting>>> run) : IJobSource
{
    public string Name => name;
    public Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest request, CancellationToken ct) => run(request, ct);

    public static FakeSource Returning(string name, params JobPosting[] jobs) =>
        new(name, (_, _) => Task.FromResult<IReadOnlyList<JobPosting>>(jobs));
}
