namespace Retirebot.Models.AzureDevOps
{
    public enum AuthMode
    {
        None,
        BuiltIn,
        ClientSecret,
        Certificate,
        ManagedIdentity,
        PAT
    }
}
