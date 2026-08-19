using System.Runtime.CompilerServices;

namespace Microsoft.RetireaBot.Models
{
    public enum WorkItemScope
    {
        Monolithic,
        PerContainer
    }

    public enum WorkItemBackend
    {
        Unknown,
        AzureDevOps,
        GitHub
    }

    public enum DataSinkBackend
    {
        NoOp,
        PowerBI
    }

    public abstract class VendorKeys
    {
        protected string Section { get; }
        protected VendorKeys(string section) => Section = section;

        protected string K([CallerMemberName] string name = "") => $"{Section}:{name}";

        public string AdvisoryLabel => K();
        public string AdvisoryParentLabel => K();
        public string AdvisoryLabelPrefix => K();
        public string AdvisoryParentLabelPrefix => K();
        public string PAT => K();
        public string TargetRepository => K();
        public string UnmappedRepository => K();
        public string TargetResourceGroup => K();
        public string TargetContainerMapping => K();
    }

    public sealed class GitHubKeys : VendorKeys
    {
        public GitHubKeys() : base("GitHub") { }

        public string AppId => K();
        public string AppPrivateKeyId => K();
        public string AppInstallId => K();
    }

    public sealed class AzureDevOpsKeys : VendorKeys
    {
        public AzureDevOpsKeys() : base("AzureDevOps") { }

        public string ClientId => K();
        public string TenantId => K();
        public string ClientSecret => K();
        public string CertificateId => K();
        public string OrganisationUrl => K();
        public string WorkItemDefaultAssignee => K();
        public string WorkItemClosedState => K();
        public string WorkItemOpenState => K();
        public string WorkItemType => K();
    }

    public sealed class PowerBIKeys : VendorKeys
    {
        public PowerBIKeys() : base("PowerBI") { }

        public string ClientId => K();
        public string TenantId => K();
        public string ClientSecret => K();
        public string CertificateId => K();
        public string WorkspaceId => K();
        public string DatasetId => K();
        public string TableName => K();
        public string WriteMode => K();
    }

    public static class ConfigKeys
    {
        public const string AzureClientId = "AZURE_CLIENT_ID";

        public static readonly AzureDevOpsKeys AzureDevOps = new();
        public static readonly GitHubKeys GitHub = new();
        public static readonly PowerBIKeys PowerBI = new();

        public static class App
        {
            public const string AssignGitHubCopilot = "App:AssignGitHubCopilot";
            public const string CreateParentWorkItems = "App:CreateParentWorkItems";
            public const string CreateChildWorkItems = "App:CreateChildWorkItems";
            public const string DataSinkBackend = "App:DataSinkBackend";
            public const string HTTPEndpointEnable = "App:HTTPEndpointEnable";
            public const string HTTPEndpointOutput = "App:HTTPEndpointOutput";
            public const string HTTPEndpointWhatIf = "App:HTTPEndpointWhatIf";
            public const string LifecycleSignalsEnable = "App:LifecycleSignalsEnable";
            public const string LifecycleWarningWindowDays = "App:LifecycleWarningWindowDays";
            public const string UseTriageRepoForUnmapped = "App:UseTriageRepoForUnmapped";
            public const string WorkItemBackend = "App:WorkItemBackend";
            public const string IncludeResolvedAdvisories = "App:IncludeResolvedAdvisories";
            public const string WorkItemScope = "App:WorkItemScope";
        }

        public static class KeyVault
        {
            public const string Uri = "KeyVault:Uri";
        }
    }
}