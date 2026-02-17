using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Octokit;
using Retirebot;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Application Insights isn't enabled by default. See https://aka.ms/AAt8mw4.
// builder.Services
//     .AddApplicationInsightsTelemetryWorkerService()
//     .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(new DefaultAzureCredential());

builder.Services.AddTransient(sp =>
    new AzureCredentialTokenHandler(
        sp.GetRequiredService<DefaultAzureCredential>(),
        ["https://management.azure.com/.default"]));

builder.Services.AddHttpClient<ManagementClient>(c => c.BaseAddress = new Uri("https://management.azure.com/"))
    .AddHttpMessageHandler<AzureCredentialTokenHandler>();

builder.Services.AddSingleton(sp =>
{
    var appId = long.Parse(Environment.GetEnvironmentVariable("GITHUB_APP_ID") ?? throw new InvalidOperationException("GITHUB_APP_ID not configured"));
    var privateKeyPath = Path.Combine(Directory.GetCurrentDirectory(), Environment.GetEnvironmentVariable("GITHUB_PRIVATE_KEY_PATH") ?? "private-key.pem");
    var privateKey = File.ReadAllText(privateKeyPath);

    var appClient = new GitHubClient(new ProductHeaderValue("Retirebot"))
    {
        Credentials = new Credentials(GitHubAuthenticationHandler.GetJWT(appId, privateKey), AuthenticationType.Bearer)
    };

    // If you need installation token instead of JWT, you'd do:
    // var installationId = long.Parse(Environment.GetEnvironmentVariable("GITHUB_INSTALLATION_ID"));
    // var response = appClient.GitHubApps.CreateInstallationToken(installationId).Result;
    // return new GitHubClient(new ProductHeaderValue("Retirebot"))
    // {
    //     Credentials = new Credentials(response.Token)
    // };

    return appClient;
});
builder.Build().Run();