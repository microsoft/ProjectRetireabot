using Azure.Core;
using System.Net.Http.Headers;

namespace Retirebot.Helpers
{
    public class AzureCredentialTokenHandler : DelegatingHandler
    {
        private readonly TokenCredential _credential;
        private readonly string[] _scopes;

        public AzureCredentialTokenHandler(TokenCredential credential, string[] scopes)
        {
            _credential = credential;
            _scopes = scopes;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tokenResult = await _credential.GetTokenAsync(new TokenRequestContext(_scopes), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Token);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}