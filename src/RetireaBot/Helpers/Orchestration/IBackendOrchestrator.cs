using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Helpers.Orchestration
{
    public interface IBackendOrchestrator
    {
        Task<IReadOnlyList<BackendOutputResult>> RunAsync(
            List<Advisory> advisories,
            bool whatIf,
            CancellationToken cancellationToken = default);
    }
}
