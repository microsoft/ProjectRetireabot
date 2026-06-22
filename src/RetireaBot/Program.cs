using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Microsoft.RetireaBot.Helpers;
using Microsoft.RetireaBot.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.ConfigureFunctionsWebApplication();
builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Logging.SetMinimumLevel(LogLevel.Information);

string? azureClientId = builder.Configuration.GetSection(ConfigKeys.AzureClientId).Get<string>();

// Use ManagedIdentityCredential explicitly in Azure-hosted environments and fall back to local developer credentials only in dev
TokenCredential credentials = builder.Environment.IsDevelopment()
    ? new ChainedTokenCredential(
        new AzureCliCredential(),
        new VisualStudioCredential(),
        new AzureDeveloperCliCredential())
    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(azureClientId!));
builder.Services.AddSingleton(credentials);

string? keyvaultUri = builder.Configuration.GetSection(ConfigKeys.KeyVault.Uri).Get<string>();
if (keyvaultUri != null)
{
    Uri KeyVaultUri = new Uri(keyvaultUri);

    builder.Services.AddSingleton(sp => new KeyClient(KeyVaultUri, credentials));
    builder.Services.AddSingleton(sp => new CertificateClient(KeyVaultUri, credentials));
    builder.Configuration.AddAzureKeyVault(KeyVaultUri, credentials);
}

builder.Services.AddTransient(sp =>
    new Microsoft.RetireaBot.Helpers.Azure.CredentialTokenHandler(
        sp.GetRequiredService<TokenCredential>(),
        new[] { "https://management.azure.com/.default" }));

builder.Services.AddHttpClient<Microsoft.RetireaBot.Helpers.Azure.ManagementClient>(c =>
{
    c.BaseAddress = new Uri("https://management.azure.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("RetireaBot/1.0 (+https://github.com/microsoft/ProjectRetireabot)");
})
    .AddHttpMessageHandler<Microsoft.RetireaBot.Helpers.Azure.CredentialTokenHandler>()
        .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

builder.Services.AddHttpClient<Microsoft.RetireaBot.Helpers.Lifecycle.LifecycleClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("RetireaBot/1.0 (+https://github.com/microsoft/ProjectRetireabot)");
})
    .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

builder.Services.AddTransient<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.PowerBIHttpMessageHandler>();
builder.Services.AddHttpClient<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.PowerBIDataSink>(c =>
{
    c.BaseAddress = new Uri("https://api.powerbi.com/");
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("RetireaBot/1.0 (+https://github.com/microsoft/ProjectRetireabot)");
})
    .AddHttpMessageHandler<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.PowerBIHttpMessageHandler>()
    .AddPolicyHandler(Policy<HttpResponseMessage>
        .Handle<HttpRequestException>()
        .OrResult(r => (int)r.StatusCode is 429 or >= 500)
        .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(Math.Pow(2, retry))));

string? backendString = builder.Configuration.GetSection(ConfigKeys.App.WorkItemBackend).Get<string>();

List<WorkItemBackend> parsedBackends = new();
if (!string.IsNullOrWhiteSpace(backendString))
{
    var rawBackends = backendString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var uniqueBackends = rawBackends.ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (uniqueBackends.Count == 0)
    {
        Console.WriteLine($"[Startup] WARNING: App:WorkItemBackend contained no valid entries. No WorkItem backends will be loaded in this session.");
    }

    if (uniqueBackends.Count < rawBackends.Length)
    {
        Console.WriteLine($"[Startup] App:WorkItemBackend contained {rawBackends.Length - uniqueBackends.Count} duplicate entry(ies). Using {uniqueBackends.Count} unique backend(s).");
    }


    foreach (string raw in uniqueBackends)
    {
        if (!Enum.TryParse<WorkItemBackend>(raw, ignoreCase: true, out var backend) || backend == WorkItemBackend.Unknown)
        {
            throw new InvalidOperationException($"Unsupported work item backend: \"{raw}\"");
        }
        switch (backend)
        {
            case WorkItemBackend.GitHub:
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.GitHub.AuthModeService>();
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.GitHub.CredentialProvider>();
                builder.Services.AddSingleton<IWorkItemClient, Microsoft.RetireaBot.Helpers.GitHub.WorkItemClient>();
                break;
            case WorkItemBackend.AzureDevOps:
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.AzureDevOps.AuthModeService>();
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.AzureDevOps.CredentialProvider>();
                builder.Services.AddSingleton<IWorkItemClient, Microsoft.RetireaBot.Helpers.AzureDevOps.WorkItemClient>();
                break;
        }
        parsedBackends.Add(backend);
    }
}
else
{
    Console.WriteLine($"[Startup] WARNING: App:WorkItemBackend is null or empty. No WorkItem backends will be loaded in this session.");
}

string? datasinkString = builder.Configuration.GetSection(ConfigKeys.App.DataSinkBackend).Get<string>();

List<DataSinkBackend> parsedSinks = new();
if (!string.IsNullOrWhiteSpace(datasinkString))
{
    var rawSinks = datasinkString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var uniqueSinks = rawSinks.ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (uniqueSinks.Count == 0)
    {
        Console.WriteLine($"[Startup] WARNING: App:DataSinkBackend contained no valid entries. No DataSink backends will be loaded in this session.");
    }

    if (uniqueSinks.Count < rawSinks.Length)
    {
        Console.WriteLine($"[Startup] App:DataSinkBackend contained {rawSinks.Length - uniqueSinks.Count} duplicate entry(ies). Using {uniqueSinks.Count} unique sink(s).");
    }


    foreach (string raw in uniqueSinks)
    {
        if (!Enum.TryParse<DataSinkBackend>(raw, ignoreCase: true, out var sink))
        {
            throw new InvalidOperationException($"Unsupported data sink: \"{raw}\"");
        }

        switch (sink)
        {
            case DataSinkBackend.NoOp:
                builder.Services.AddSingleton<IDataSinkClient, Microsoft.RetireaBot.Helpers.Sinks.NoOpDataSink>();
                break;
            case DataSinkBackend.PowerBI:
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.AuthModeService>();
                builder.Services.TryAddSingleton<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.CredentialProvider>();
                builder.Services.AddSingleton<IDataSinkClient>(sp =>
                    sp.GetRequiredService<Microsoft.RetireaBot.Helpers.Sinks.PowerBI.PowerBIDataSink>());
                break;
            default:
                throw new InvalidOperationException($"Unsupported data sink: \"{raw}\"");
        }

        parsedSinks.Add(sink);
    }
}
else
{
    Console.WriteLine($"[Startup] WARNING: App:DataSinkBackend is null or empty. No DataSink backends will be loaded in this session.");
}

builder.Services.AddSingleton<Microsoft.RetireaBot.Models.IVendorSettingsProvider>(sp =>
    new Microsoft.RetireaBot.Helpers.Settings.VendorSettingsProvider(
        sp.GetRequiredService<IConfiguration>(),
        parsedBackends,
        parsedSinks));

builder.Services.AddSingleton<Microsoft.RetireaBot.Helpers.Orchestration.IBackendOrchestrator,
                              Microsoft.RetireaBot.Helpers.Orchestration.BackendOrchestrator>();

builder.Services.AddSingleton<Microsoft.RetireaBot.Helpers.Orchestration.IDataSinkOrchestrator,
                              Microsoft.RetireaBot.Helpers.Orchestration.DataSinkOrchestrator>();

var app = builder.Build();

PreflightChecks.StartPreflightChecks(builder.Configuration, app, parsedBackends, parsedSinks);

app.Run();
