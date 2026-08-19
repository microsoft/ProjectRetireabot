namespace Microsoft.RetireaBot.Contracts
{
    public interface IResponseProjector
    {
        ApiVersion Version { get; }
        Task<ProjectedResponse> Project(Domain.RetirementReport report, string mediaType);
    }

    public readonly record struct ProjectedResponse(string ContentType, BinaryData Body);
}