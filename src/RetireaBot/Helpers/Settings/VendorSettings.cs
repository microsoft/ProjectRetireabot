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
            CreateParentWorkItems = config.GetSection($"{section}:CreateParentWorkItems").Get<bool?>() ?? true;
            CreateChildWorkItems = config.GetSection($"{section}:CreateChildWorkItems").Get<bool?>() ?? true;
            TargetRepository = config.GetSection($"{section}:TargetRepository").Get<string?>() ?? throw new InvalidOperationException($"{section}:TargetRepository is not configured.");

            string? mappingJson = config.GetSection($"{section}:TargetContainerMapping").Get<string>();
            TargetContainerMapping = !string.IsNullOrEmpty(mappingJson)
                ? (JsonSerializer.Deserialize<List<AzureRepositoryMap>>(mappingJson) ?? [])
                    .Select(ScanScopeResolver.NormalizeMapping).ToList()
                : [];

            IncludeResourceId = config.GetSection($"{section}:IncludeResourceId").Get<bool?>() ?? false;
            TargetResourceGroup = config.GetSection($"{section}:TargetResourceGroup").Get<string>();
            UnmappedRepository = config.GetSection($"{section}:UnmappedRepository").Get<string>();
            UseTriageRepoForUnmapped = config.GetSection($"{section}:UseTriageRepoForUnmapped").Get<bool?>() ?? false;
            WorkItemScope = Enum.Parse<WorkItemScope>(config.GetSection($"{section}:WorkItemScope").Get<string?>() ?? nameof(WorkItemScope.Monolithic), ignoreCase: true);
        }

        public WorkItemBackend Backend { get; }
        public string AdvisoryLabel { get; }
        public string AdvisoryParentLabel { get; }
        public string AdvisoryLabelPrefix { get; }
        public string AdvisoryParentLabelPrefix { get; }
        public bool AssignCopilot { get; }
        public bool CreateParentWorkItems { get; }
        public bool CreateChildWorkItems { get; }
        public string TargetRepository { get; }
        public bool IncludeResourceId { get; }
        public List<AzureRepositoryMap> TargetContainerMapping { get; }
        public string? TargetResourceGroup { get; }
        public string? UnmappedRepository { get; }
        public bool UseTriageRepoForUnmapped { get; }
        public WorkItemScope WorkItemScope { get; }
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