# Job-Screener

Search jobs across several sources, filtered by how far back to look, job titles, job type, location and maximum applicants, then ranked by how many of your resume's skills each posting mentions.

Built with ASP.NET Core 8, Blazor Server, EF Core (SQLite) and xUnit.

## How it works

1. Enter your LinkedIn handle (used only as your identity in the app) and upload a resume (PDF or DOCX).
2. The app reads your skills and suggests job titles.
3. Set the filters and search. Results show a **skills overlap** percentage, a link to the original posting, and a CSV export.

## Filters

| Filter | Notes |
|---|---|
| Look back | Hours, days or weeks |
| Job titles | Any number |
| Job type | Remote, On-site, Hybrid |
| Location | Free text. `Worldwide` means no restriction |
| Max applicants | Only some sources report applicant counts. Postings with an unknown count can be included or excluded |

## Job sources

| Source | Needs | Notes |
|---|---|---|
| Remotive | nothing | Remote jobs. Please follow Remotive's API terms |
| Remote OK | nothing | Remote jobs. Requires linking back and crediting Remote OK, which the app does |
| Arbeitnow | nothing | Mostly European jobs, some remote |
| Adzuna | free API key | Set `Sources__Adzuna__Enabled=true`, `Sources__Adzuna__AppId`, `Sources__Adzuna__AppKey` (and optionally `Sources__Adzuna__Country`, default `gb`) |
| LinkedIn public pages | opt-in | **Off by default. Often blocked.** See below |

Check each provider's current terms before running a public instance.

## LinkedIn plug-in (read this first)

LinkedIn has no public jobs API. The optional plug-in reads only LinkedIn's public, logged-out job pages, rate limits itself and stops at the first block. This is against LinkedIn's terms of service, can get your server's IP blocked and breaks when LinkedIn changes its pages. In testing, LinkedIn answered with HTTP 429 and an anti-bot challenge, so expect it not to work. It is intended for personal use only. Enable with `Sources__LinkedIn__Enabled=true`.

LinkedIn's public list only carries a date, not a time, so those rows show "within search window" instead of a posted time.

## Limits

- Arbeitnow does not distinguish hybrid from on-site, so its non-remote jobs are labelled On-site.
- Only LinkedIn reports applicant counts, so the max-applicants filter mostly affects LinkedIn results.
- API feeds update slowly, so very short look-back windows return fewer jobs.
- The match percentage is keyword overlap between resume skills and posting text, not an AI judgement.
- Resumes are personal data. They are stored in the local SQLite database, never logged, and removed by "Delete my data".

## Privacy and limits of v1

There are no accounts. Anyone who knows a LinkedIn handle can overwrite or delete the data stored under that handle. For that reason the app stores only a resume and skills, and offers "Delete my data". Do not deploy this publicly with sensitive data until accounts exist.

## Run it

```bash
dotnet run --project src/JobScreener.Web --urls http://localhost:5080
```

Tests: `dotnet test`

Docker:

```bash
docker build -t job-screener .
docker run -p 8080:8080 -v jobscreener-data:/data job-screener
```

Source toggles (all default to true except where noted): `Sources__Remotive`, `Sources__RemoteOk`, `Sources__Arbeitnow` (true/false) and `Sources__LinkedIn__RequestDelaySeconds` (seconds between LinkedIn requests, default 2).

The container reads its settings from environment variables, for example `ConnectionStrings__Default` (defaults to `Data Source=/data/jobscreener.db`). A health check is available at `/healthz`.

## Hosting

Put the app behind HTTPS (a reverse proxy or your host's TLS) because resumes are uploaded; the container serves plain HTTP on 8080 and has no rate limiting, so do not expose a public instance without one.

See [DEPLOY.md](DEPLOY.md) for a step-by-step guide to Azure App Service (free tier). This is a server app, so it cannot run on GitHub Pages. Use any host that runs a .NET or Docker container (Azure App Service, Fly.io, Render, a VPS).

## Spec and plan

See `docs/superpowers/specs` and `docs/superpowers/plans`.
