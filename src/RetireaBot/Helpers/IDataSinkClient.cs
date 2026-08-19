using Microsoft.RetireaBot.Domain;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.Azure;


namespace Microsoft.RetireaBot.Helpers
{
    public interface IDataSinkClient
    {
        DataSinkBackend Backend { get; }
        Task<DataSinkOutputResult> PushAsync(
            IReadOnlyList<Advisory> advisories,
            bool whatIf,
            CancellationToken cancellationToken = default
        );
    }
}