using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octokit;
using Polly;
using Retirebot.Helpers;
using System.Text.RegularExpressions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
     .AddApplicationInsightsTelemetryWorkerService()
     .ConfigureFunctionsApplicationInsights();

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    // The isolated worker SDK registers a default Warning filter under this provider name.
    // Remove it so that host.json logLevel settings are respected.
    var rulesToRemove = options.Rules
        .Where(rule => rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider")
        .ToList();

    foreach (var rule in rulesToRemove)
    {
        options.Rules.Remove(rule);
    }
});

builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddSingleton(new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
}));

builder.Services.AddTransient(sp =>
    new AzureCredentialTokenHandler(
        sp.GetRequiredService<DefaultAzureCredential>(),
        new[] { "https://management.azure.com/.default" }));

builder.Services.AddHttpClient<ManagementClient>(c =>
{
    c.BaseAddress = new Uri("https://management.azure.com/");
    c.Timeout = TimeSpan.FromSeconds(30);
})
    .AddHttpMessageHandler<AzureCredentialTokenHandler>()
        .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

builder.Services.AddSingleton(sp =>
{
    var pat = Environment.GetEnvironmentVariable("GITHUB_PAT") ?? throw new InvalidOperationException("GITHUB_PAT not configured");

    var appClient = new GitHubClient(new ProductHeaderValue("Retirebot"))
    {
        Credentials = new Credentials(pat)
    };

    return appClient;
});

string? targetRepo = Environment.GetEnvironmentVariable("TARGET_REPOSITORY");
Regex RepoPattern = new Regex(@"^[a-zA-Z0-9\-]+/[a-zA-Z0-9._\-]+$");

if (targetRepo == null || !RepoPattern.IsMatch(targetRepo))
{
    throw new MissingFieldException("TARGET_REPOSITORY is empty or not in the expected 'owner/repo' format");
}

builder.Build().Run();