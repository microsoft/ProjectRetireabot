using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Helpers.Orchestration
{
    public interface IDataSinkOrchestrator
    {
        Task<IReadOnlyList<DataSinkOutputResult>> RunAsync(
            List<Advisory> advisories,
            bool whatIf,
            CancellationToken cancellationToken = default);
    }
}