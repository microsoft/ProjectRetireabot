using System.Net.Http.Headers;
using Azure.Core;

namespace Microsoft.RetireaBot.Helpers.Sinks.PowerBI
{
    internal sealed class PowerBIHttpMessageHandler : DelegatingHandler
    {
        private readonly CredentialProvider _credentialProvider;

        public PowerBIHttpMessageHandler(CredentialProvider credentialProvider)
        {
            _credentialProvider = credentialProvider;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AccessToken token = await _credentialProvider.GetTokenAsync(cancellationToken);

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
