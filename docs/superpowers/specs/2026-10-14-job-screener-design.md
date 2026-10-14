# Job-Screener: design

Date: 2026-10-14. Repo: `siradakole/Job-Screener` (to be created on GitHub after this spec is approved).

## Purpose

A web app where an applicant enters their LinkedIn handle, uploads a resume, sets search filters and gets a ranked list of matching jobs from several sources. Version 1 is an on-demand search with no accounts.

## Decisions

| Topic | Decision |
|---|---|
| Job sources | Open job APIs first. LinkedIn public-page scraping is an optional plug-in, disabled by default. |
| Profile input | LinkedIn handle (identity and display link only) plus a PDF or DOCX resume upload. No scraping of the applicant's LinkedIn profile. |
| Stack | ASP.NET Core 8, Blazor Server, EF Core with SQLite, xUnit tests, Docker. |
| Usage model | On-demand search. No passwords or accounts. Applicants are keyed by handle. |
| Structure | One web project plus a class library for the engine and sources, so a worker service can be split out later. |

## Filters (all required in the UI)

1. **Look back:** a number plus a unit (hours, days, weeks). Converted to a cutoff timestamp.
2. **Job titles:** any number of titles, entered as chips. Suggested from the resume.
3. **Job type:** Remote, On-site, Hybrid (multi-select).
4. **Location:** free text. "Worldwide" means no location restriction.
5. **Max applicants:** a number. Applies only to postings that report an applicant count.

An extra toggle, "include jobs with unknown applicant count" (default on), decides what happens to postings without a count.

## Architecture

```
Blazor UI -> SearchService -> [ResumeParser] [IJobSource x N] [FilterEngine] [MatchScorer] -> SQLite
```

- **`JobPosting`** (normalized record): Id, Source, Title, Company, Location, WorkType, PostedAtUtc, ApplicantCount (nullable), Url, Description.
- **`IJobSource`**: `Name`, `Enabled`, `Task<IReadOnlyList<JobPosting>> SearchAsync(SearchRequest, CancellationToken)`. A source that cannot supply a field leaves it null.
- **Sources (v1):** `RemotiveSource`, `RemoteOkSource`, `ArbeitnowSource`, `AdzunaSource` (needs a free API key in configuration), and `LinkedInPublicSource` (disabled by default).
- **`SearchService`:** runs enabled sources in parallel with a per-source timeout. A failing source returns a warning and the run continues.
- **`FilterEngine`:** applies the five filters to normalized postings.
- **`ResumeParser`:** extracts text from PDF (PdfPig) and DOCX (OpenXML), then pulls skills against a curated skills dictionary, plus likely job titles and years of experience.
- **`MatchScorer`:** score 0 to 100 = weighted overlap between resume skills and skills found in the posting, with a title-match bonus. Labelled "skills overlap" in the UI.
- **Persistence:** `Applicant` (Handle, ResumeText, Skills, CreatedUtc), `SearchRun` (filters JSON, started, finished, warnings), cached `JobPosting` rows per run.

## Screens

1. **Home:** handle field and resume upload. Shows detected skills and suggested titles for confirmation and editing.
2. **Search:** the five filters, source toggles, Run button.
3. **Results:** table sorted by match percent: title, company, location, posted time, applicants (or "not available"), source, match, link. Slider for minimum match, CSV export, warnings panel.
4. **Privacy:** a "Delete my data" button that removes the applicant, resume text and runs.

## LinkedIn plug-in rules

- Disabled by default. Enabling needs a configuration flag and shows a terms-of-service warning in the UI.
- Uses only public, logged-out job pages. No credentials, cookies or logged-in sessions.
- Rate limited (one request per 2 seconds, capped pages per run) and respects HTTP 429 by stopping.
- The README states that it is intended for personal use and may break or be blocked.

## Limits to state in the UI and README

- Only LinkedIn reports applicant counts, so the max-applicants filter mainly affects LinkedIn results.
- API feeds update slowly, so very short look-back windows return few jobs from them.
- The match score is a keyword overlap, not a model judgement.
- Resumes are personal data: stored locally, never logged, deletable on request.

## Error handling

- Invalid or empty resume: show a clear message, allow manual skill entry.
- Source timeout or HTTP error: warning for that source, results from the others still shown.
- LinkedIn 429 or block: stop that source and warn. Do not retry in a loop.
- No results: show which filter removed the most postings.

## Testing

- Unit tests: `FilterEngine` (look-back conversion, job type, location, applicant rules), `MatchScorer`, `ResumeParser` (sample PDF and DOCX), each source's response mapping using saved sample responses.
- Integration test: `SearchService` with fake sources, including one that fails.
- No live network calls in tests.

## Out of scope for v1

Accounts, saved searches, scheduled runs, email alerts, automatic applying, AI-based scoring.

## Open items for the implementation plan

- Final choice and licence check for each free API (terms, rate limits, attribution).
- Whether to publish the repository as public or private (decide when creating it).
