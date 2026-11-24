namespace Microsoft.RetireaBot.Models
{
    public enum ScanMode
    {
        Explicit,
        FromRouting,
        DeploymentSubscription
    }

    public sealed class ScanOptions
    {
        public required ScanMode Mode { get; init; }
        public string[] Scopes { get; init; } = [];
        public bool AssignReader { get; init; } = true;
    }

    public sealed record ScanScope(string ScopeId, string SubscriptionId, string? ResourceGroupName);

    public sealed record ResolvedScanOptions(ScanMode Mode, IReadOnlyList<ScanScope> Scopes, bool AssignReader);
}
