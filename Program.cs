using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

builder.Build().Run();