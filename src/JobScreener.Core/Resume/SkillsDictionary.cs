using System.Text.RegularExpressions;

namespace JobScreener.Core.Resume;

public static class SkillsDictionary
{
    private static readonly (string Canonical, string[] Aliases)[] Entries =
    {
        ("C#", new[] { "c#", "csharp" }),
        (".NET", new[] { ".net", "dotnet", "asp.net" }),
        ("ASP.NET Core", new[] { "asp.net core", "aspnet core" }),
        ("Blazor", new[] { "blazor" }),
        ("Entity Framework", new[] { "entity framework", "ef core" }),
        ("SQL Server", new[] { "sql server", "mssql", "t-sql" }),
        ("MySQL", new[] { "mysql" }),
        ("PostgreSQL", new[] { "postgresql", "postgres" }),
        ("SQLite", new[] { "sqlite" }),
        ("SQL", new[] { "sql" }),
        ("Hangfire", new[] { "hangfire" }),
        ("RabbitMQ", new[] { "rabbitmq" }),
        ("Azure", new[] { "azure" }),
        ("AWS", new[] { "aws", "amazon web services" }),
        ("Docker", new[] { "docker" }),
        ("Kubernetes", new[] { "kubernetes", "k8s" }),
        ("Git", new[] { "git", "github" }),
        ("REST API", new[] { "rest api", "rest apis", "restful" }),
        ("Webhooks", new[] { "webhook", "webhooks" }),
        ("Microservices", new[] { "microservice", "microservices" }),
        ("JWT", new[] { "jwt" }),
        ("CI/CD", new[] { "ci/cd", "cicd" }),
        ("Automation", new[] { "automation", "automate", "automated" }),
        ("Android", new[] { "android" }),
        ("Java", new[] { "java" }),
        ("Kotlin", new[] { "kotlin" }),
        ("React", new[] { "react", "reactjs", "react.js" }),
        ("Node.js", new[] { "node.js", "nodejs", "node js" }),
        ("JavaScript", new[] { "javascript" }),
        ("TypeScript", new[] { "typescript" }),
        ("Python", new[] { "python" }),
        ("Redis", new[] { "redis" }),
        ("MongoDB", new[] { "mongodb" }),
        ("GraphQL", new[] { "graphql" }),
        ("Unity", new[] { "unity" }),
        ("Firebase", new[] { "firebase" }),
        ("OpenAI", new[] { "openai" }),
    };

    private static readonly (string Canonical, Regex[] Patterns)[] Compiled = Entries
        .Select(e => (e.Canonical, e.Aliases.Select(a =>
            new Regex($@"(?<![a-z0-9]){Regex.Escape(a)}(?![a-z0-9])", RegexOptions.Compiled)).ToArray()))
        .ToArray();

    public static IReadOnlyList<string> All { get; } = Entries.Select(e => e.Canonical).ToList();

    public static IReadOnlyList<string> Extract(string text)
    {
        var lower = (text ?? "").ToLowerInvariant();
        return Compiled.Where(e => e.Patterns.Any(p => p.IsMatch(lower))).Select(e => e.Canonical).ToList();
    }
}
