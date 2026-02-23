using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octokit;
using Polly;
using Retirebot.Helpers;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
     .AddApplicationInsightsTelemetryWorkerService()
     .ConfigureFunctionsApplicationInsights();

builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    LoggerFilterRule? defaultRule = options.Rules.FirstOrDefault(rule => rule.ProviderName
        == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

builder.Services.AddSingleton(new DefaultAzureCredential());

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
    var username = Environment.GetEnvironmentVariable("GITHUB_USERNAME") ?? throw new InvalidOperationException("GITHUB_USERNAME not configured");

    var appClient = new GitHubClient(new ProductHeaderValue("Retirebot"))
    {
        Credentials = new Credentials(username, pat)
    };

    return appClient;
});
builder.Build().Run();