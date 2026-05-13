namespace Microsoft.RetireaBot.Models.AzureDevOps
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
