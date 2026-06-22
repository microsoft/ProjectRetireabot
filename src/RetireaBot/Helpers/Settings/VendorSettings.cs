using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Helpers.Settings
{
    internal sealed class VendorSettings : IVendorSettings
    {
        public VendorSettings(
            WorkItemBackend backend,
            string section,
            IConfiguration config,
            bool supportsCopilot)
        {
            Backend = backend;
            AdvisoryLabel = config[$"{section}:AdvisoryLabel"] ?? "azure-advisor";
            AdvisoryParentLabel = config[$"{section}:AdvisoryParentLabel"] ?? "tracking";
            AdvisoryLabelPrefix = config[$"{section}:AdvisoryLabelPrefix"] ?? "advisor-";
            AdvisoryParentLabelPrefix = config[$"{section}:AdvisoryParentLabelPrefix"] ?? "advisor-type-";
            AssignCopilot = supportsCopilot
                                  && (config.GetSection($"{section}:AssignCopilot").Get<bool?>() ?? false);
            TargetRepository = config.GetSection($"{section}:TargetRepository").Get<string?>() ?? throw new InvalidOperationException($"{section}:TargetRepository is not configured.");

            string? mappingJson = config.GetSection($"{section}:TargetResourceGroupMapping").Get<string>();
            TargetResourceGroupMapping = !string.IsNullOrEmpty(mappingJson)
                ? JsonSerializer.Deserialize<List<AzureRepositoryMap>>(mappingJson) ?? []
                : [];

            TargetResourceGroup = config.GetSection($"{section}:TargetResourceGroup").Get<string>();
            UnmappedRepository = config.GetSection($"{section}:UnmappedRepository").Get<string>();
        }

        public WorkItemBackend Backend { get; }
        public string AdvisoryLabel { get; }
        public string AdvisoryParentLabel { get; }
        public string AdvisoryLabelPrefix { get; }
        public string AdvisoryParentLabelPrefix { get; }
        public bool AssignCopilot { get; }
        public string TargetRepository { get; }
        public List<AzureRepositoryMap> TargetResourceGroupMapping { get; }
        public string? TargetResourceGroup { get; }
        public string? UnmappedRepository { get; }
    }


    internal sealed class DataSinkSettings : IDataSinkSettings
    {
        public DataSinkSettings(
            DataSinkBackend backend
        )
        {
            Backend = backend;
        }

        public DataSinkBackend Backend { get; }
    }

    public sealed class VendorSettingsProvider : IVendorSettingsProvider
    {
        private readonly IReadOnlyDictionary<WorkItemBackend, IVendorSettings> _byBackend;
        private readonly IReadOnlyDictionary<DataSinkBackend, IDataSinkSettings> _bySink;

        public VendorSettingsProvider(IConfiguration config, IEnumerable<WorkItemBackend> activeBackends, IEnumerable<DataSinkBackend> activeSinks)
        {
            var backendDict = new Dictionary<WorkItemBackend, IVendorSettings>();
            foreach (var backend in activeBackends)
            {
                backendDict[backend] = backend switch
                {
                    WorkItemBackend.GitHub => new VendorSettings(WorkItemBackend.GitHub, "GitHub", config, supportsCopilot: true),
                    WorkItemBackend.AzureDevOps => new VendorSettings(WorkItemBackend.AzureDevOps, "AzureDevOps", config, supportsCopilot: false),
                    _ => throw new InvalidOperationException($"No vendor settings mapping for backend {backend}")
                };
            }
            _byBackend = backendDict;

            var sinkDict = new Dictionary<DataSinkBackend, IDataSinkSettings>();
            foreach (var sink in activeSinks)
            {
                sinkDict[sink] = new DataSinkSettings(sink);
            }
            _bySink = sinkDict;
        }

        public IVendorSettings For(WorkItemBackend backend)
            => _byBackend.TryGetValue(backend, out var s)
                ? s
                : throw new InvalidOperationException($"No vendor settings registered for backend {backend}");

        public IDataSinkSettings For(DataSinkBackend sink)
            => _bySink.TryGetValue(sink, out var s)
                ? s
                : throw new InvalidOperationException($"No vendor settings registered for data sink {sink}");
    }
}