using JobScreener.Core.Data;
using JobScreener.Core.Search;
using JobScreener.Core.Sources;
using JobScreener.Web.Components;
using JobScreener.Web.State;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=jobscreener.db"));
builder.Services.AddSingleton<ApplicantStore>();
builder.Services.AddScoped<AppState>();

// Sources: open APIs on by default; Adzuna needs keys; LinkedIn is opt-in only.
if (config.GetValue("Sources:Remotive", true))
    builder.Services.AddHttpClient<IJobSource, RemotiveSource>();
if (config.GetValue("Sources:RemoteOk", true))
    builder.Services.AddHttpClient<IJobSource, RemoteOkSource>();
if (config.GetValue("Sources:Arbeitnow", true))
    builder.Services.AddHttpClient<IJobSource, ArbeitnowSource>();
if (config.GetValue("Sources:Adzuna:Enabled", false))
{
    var adzuna = new AdzunaOptions(config["Sources:Adzuna:AppId"] ?? "", config["Sources:Adzuna:AppKey"] ?? "",
        config["Sources:Adzuna:Country"] ?? "gb");
    builder.Services.AddSingleton(adzuna);
    builder.Services.AddHttpClient<IJobSource, AdzunaSource>();
}
if (config.GetValue("Sources:LinkedIn:Enabled", false))
{
    builder.Services.AddSingleton(new LinkedInOptions(
        TimeSpan.FromSeconds(config.GetValue("Sources:LinkedIn:RequestDelaySeconds", 2))));
    builder.Services.AddHttpClient<IJobSource, LinkedInPublicSource>();
}
builder.Services.AddScoped<SearchService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

public partial class Program;
