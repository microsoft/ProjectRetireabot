using System.Globalization;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.RetireaBot.Contracts;
using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Middleware
{
    public sealed class ApiVersionMiddleware : IFunctionsWorkerMiddleware
    {
        private ResponseProjectorRegistry _registry;
        private readonly bool _httpEndpointEnable;
        private readonly ILogger<ApiVersionMiddleware> _logger;

        public ApiVersionMiddleware(ResponseProjectorRegistry projectorRegistry, IConfiguration config, ILogger<ApiVersionMiddleware> logger)
        {
            _registry = projectorRegistry;
            _logger = logger;
            _httpEndpointEnable = config.GetSection(ConfigKeys.App.HTTPEndpointEnable).Get<bool?>() ?? false;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            var req = await context.GetHttpRequestDataAsync();
            if (req == null) // non-http triggers (timers)
            {
                await next(context);
                return;
            }

            if (!_httpEndpointEnable)
            {
                _logger.LogDebug("Manual Endpoint hit when App:HTTPEndpointEnable is disabled");
                context.GetInvocationResult().Value = req.CreateResponse(HttpStatusCode.NotFound);
                return;
            }

            var raw = req.Query["api-version"];
            if (!ApiVersion.TryParse(raw, _registry.SupportedVersions, out var version))
            {
                var resp = req.CreateResponse(HttpStatusCode.BadRequest);
                string supportedVersions = string.Join(", ", _registry.SupportedVersions.Select(d => $"{d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(d.IsPreview ? "-preview" : "")}"));

                // Azure-style discoverability header
                resp.Headers.Add("api-supported-versions", supportedVersions);
                await resp.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = "InvalidApiVersion",
                        message = $"The 'api-version' query parameter is required and must be a supported date. Supported versions: {supportedVersions}."
                    }
                });

                context.GetInvocationResult().Value = resp;
                return;
            }

            context.Items["ApiVersion"] = version; // hand over parsed version info
            await next(context);
        }
    }


}