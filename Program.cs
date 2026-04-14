using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Retirebot.Helpers;
using Retirebot.Models;

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

DefaultAzureCredential credentials = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = builder.Configuration.GetSection(ConfigKeys.AzureClientId).Get<string>()
});
builder.Services.AddSingleton(credentials);

string? keyvaultUri = builder.Configuration.GetSection(ConfigKeys.KeyVault.Uri).Get<string>();
if (keyvaultUri != null)
{
    Uri KeyVaultUri = new Uri(keyvaultUri);

    builder.Services.AddSingleton(sp => new KeyClient(KeyVaultUri, credentials));
    builder.Configuration.AddAzureKeyVault(KeyVaultUri, credentials);
}

builder.Services.AddTransient(sp =>
    new Retirebot.Helpers.Azure.CredentialTokenHandler(
        sp.GetRequiredService<DefaultAzureCredential>(),
        new[] { "https://management.azure.com/.default" }));

builder.Services.AddHttpClient<Retirebot.Helpers.Azure.ManagementClient>(c =>
{
    c.BaseAddress = new Uri("https://management.azure.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
})
    .AddHttpMessageHandler<Retirebot.Helpers.Azure.CredentialTokenHandler>()
        .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

WorkItemBackend backend = Enum.Parse<WorkItemBackend>(builder.Configuration.GetSection(ConfigKeys.App.WorkItemBackend).Get<string>() ?? "GitHub", true);

switch (backend)
{
    case WorkItemBackend.GitHub:
        builder.Services.AddSingleton<Retirebot.Helpers.GitHub.AuthModeService>();
        builder.Services.AddSingleton<Retirebot.Helpers.GitHub.CredentialProvider>();
        builder.Services.AddSingleton<IWorkItemClient, Retirebot.Helpers.GitHub.WorkItemClient>();
        break;
    case WorkItemBackend.AzureDevOps:
        throw new NotImplementedException("Azure DevOps Tracking backend has not yet been implemented.");
    default:
        throw new InvalidOperationException($"Unsupported work item backend: {backend}");

}

var app = builder.Build();

PreflightChecks.StartPreflightChecks(builder, app);

app.Run();