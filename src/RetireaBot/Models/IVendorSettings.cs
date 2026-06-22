using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Models
{
    public interface IVendorSettings
    {
        bool AssignCopilot { get; }
        string AdvisoryLabel { get; }
        string AdvisoryParentLabel { get; }
        string AdvisoryLabelPrefix { get; }
        string AdvisoryParentLabelPrefix { get; }
        WorkItemBackend Backend { get; }
        string TargetRepository { get; }
        List<AzureRepositoryMap> TargetResourceGroupMapping { get; }
        string? TargetResourceGroup { get; }
        string? UnmappedRepository { get; }
    }

    public interface IDataSinkSettings
    {
        DataSinkBackend Backend { get; }
    }

    public interface IVendorSettingsProvider
    {
        IVendorSettings For(WorkItemBackend backend);
        IDataSinkSettings For(DataSinkBackend backend);
    }
}