namespace Retirebot.Models
{
    public enum WorkItemScope
    {
        Monolithic,
        PerResourceGroup
    }

    public static class ConfigKeys
    {
        public const string AzureClientId = "AZURE_CLIENT_ID";

        public static class GitHub
        {
            public const string AppId = "GitHub:AppId";
            public const string AppPrivateKeyId = "GitHub:AppPrivateKeyId";
            public const string AppInstallId = "GitHub:AppInstallId";
            public const string PAT = "GitHub:PAT";
            public const string TargetRepository = "GitHub:TargetRepository";
        }

        public static class Azure
        {
            public const string WorkItemScope = "Azure:WorkItemScope";
            public const string CreateParentIssues = "Azure:CreateParentIssues";
            public const string TargetResourceGroupMapping = "Azure:TargetResourceGroupMapping";
            public const string TargetResourceGroup = "Azure:TargetResourceGroup";
        }

        public static class App
        {
            public const string AssignGitHubCopilot = "App:AssignGitHubCopilot";
            public const string EnableHTTPEndpoint = "App:EnableHTTPEndpoint";
        }

        public static class KeyVault
        {
            public const string Uri = "KeyVault:Uri";
        }
    }
}
