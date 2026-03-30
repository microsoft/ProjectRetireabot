using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octokit;
using Polly;
using Retirebot.Helpers;
using System.Text.RegularExpressions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

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

string? keyvaultUri = builder.Configuration.GetSection("KeyVault:Uri").Get<string>();
if (keyvaultUri != null)
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyvaultUri), new DefaultAzureCredential());
}

builder.Services.AddSingleton(new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration.GetSection("AZURE_CLIENT_ID").Get<string>()
}));

builder.Services.AddTransient(sp =>
    new AzureCredentialTokenHandler(
        sp.GetRequiredService<DefaultAzureCredential>(),
        new[] { "https://management.azure.com/.default" }));

builder.Services.AddHttpClient<ManagementClient>(c =>
{
    c.BaseAddress = new Uri("https://management.azure.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
})
    .AddHttpMessageHandler<AzureCredentialTokenHandler>()
        .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

builder.Services.AddSingleton(sp =>
{
    var pat = builder.Configuration.GetSection("GitHub:PAT").Get<string>() ?? throw new InvalidOperationException("GitHub:PAT not configured");

    var appClient = new GitHubClient(new ProductHeaderValue("Retirebot"))
    {
        Credentials = new Credentials(pat)
    };

    return appClient;
});

string? targetRepo = builder.Configuration.GetSection("GitHub:TargetRepository").Get<string>();
Regex RepoPattern = new Regex(@"^[a-zA-Z0-9\-]+/[a-zA-Z0-9._\-]+$");

if (targetRepo == null || !RepoPattern.IsMatch(targetRepo))
{
    throw new MissingFieldException("GitHub:TargetRepository is empty or not in the expected 'owner/repo' format");
}

builder.Build().Run();