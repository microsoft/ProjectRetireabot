using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.KeyVaultExtensions;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using Microsoft.RetireaBot.Models.GitHub;
using System.IdentityModel.Tokens.Jwt;

namespace Microsoft.RetireaBot.Helpers.GitHub
{
    public class CredentialProvider
    {
        private GitHubClient _primaryClient;
        private GitHubClient? _coPilotClient;

        private readonly KeyClient? _keyClient;
        private readonly TokenCredential _credentials;
        private readonly ILogger _logger;

        private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private readonly AuthModeService _authModeSrv;

        public CredentialProvider(ILoggerFactory loggerFactory, TokenCredential credentials, AuthModeService authModeService, KeyClient? keyClient)
        {
            _authModeSrv = authModeService;
            _keyClient = keyClient;
            _credentials = credentials;
            _logger = loggerFactory.CreateLogger<CredentialProvider>();

            switch (_authModeSrv.GetAuthMode())
            {
                case AuthMode.Hybrid:
                    _logger.LogInformation("GitHub AuthMode: Hybrid - App (Primary) + PAT (Secondary)");
                    _primaryClient = CreateClient();
                    _coPilotClient = CreateClient(_authModeSrv.GetPAT()!);
                    break;
                case AuthMode.PAT:
                    _logger.LogInformation("GitHub AuthMode: PAT mode");
                    _primaryClient = CreateClient(_authModeSrv.GetPAT()!);
                    _coPilotClient = _primaryClient;
                    break;
                case AuthMode.App:
                    _logger.LogInformation("GitHub AuthMode: App mode");
                    _primaryClient = CreateClient();
                    break;
                case AuthMode.None:
                default:
                    throw new InvalidOperationException("No supported GitHub credentials are available. Provide GitHub:PAT and/or GitHub:AppId");
            }
        }

        public async Task<GitHubClient> GetPrimaryClient()
        {
            if (_authModeSrv.GetAuthMode().HasFlag(AuthMode.App) && DateTimeOffset.UtcNow >= _tokenExpiry)
            {
                await RefreshAppTokenAsync();
            }
            return _primaryClient;
        }

        public GitHubClient? GetCopilotCapableClient() => _coPilotClient;

        private async Task RefreshAppTokenAsync()
        {
            await _refreshLock.WaitAsync();
            try
            {
                if (DateTimeOffset.UtcNow < _tokenExpiry) return;

                _logger.LogInformation("Refreshing GitHub App Installation Client");
                _primaryClient = CreateClient();
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private GitHubClient CreateClient(string token)
        {
            return new GitHubClient(new ProductHeaderValue("RetireaBot"))
            {
                Credentials = new Credentials(token)
            };
        }

        private GitHubClient CreateClient()
        {
            if (_keyClient == null)
            {
                throw new ArgumentNullException("_keyClient", "When using AuthMode \"App\", a valid connection to a KeyVault has to be present. Please check your configuration.");
            }

            _logger.LogDebug("Loading GitHub App signing key {KeyName} from {VaultUri}", _authModeSrv.GetPrivateKeyId(), _keyClient.VaultUri);
            KeyVaultKey key = _keyClient.GetKey(_authModeSrv.GetPrivateKeyId());
            if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
            {
                throw new InvalidOperationException($"GitHub App Private Key must be an RSA based key, got KeyType \"{key.KeyType}\"");
            }

            CryptographyClient cryptoClient = _keyClient.GetCryptographyClient(key.Name, key.Properties.Version);
            KeyVaultSecurityKey securityKey = new KeyVaultSecurityKey(key.Id.ToString(), async (auth, resource, scope) =>
            {
                var token = await _credentials.GetTokenAsync(
                    new TokenRequestContext(new[] { resource + "/.default" }), CancellationToken.None);
                return token.Token;
            });

            SigningCredentials signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256)
            {
                CryptoProviderFactory = new CryptoProviderFactory() { CustomCryptoProvider = new KeyVaultCryptoProvider() }
            };

            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<System.Security.Claims.Claim> claims = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString()),
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Exp, now.AddMinutes(9).ToUnixTimeSeconds().ToString()),
                    new System.Security.Claims.Claim("iss", _authModeSrv.GetAppId()!.ToString())
                };

            JwtHeader jwtHeader = new JwtHeader(signingCredentials);
            JwtPayload jwtPayload = new JwtPayload((IEnumerable<System.Security.Claims.Claim>)claims);
            JwtSecurityToken jwt = new JwtSecurityToken(jwtHeader, jwtPayload);

            _logger.LogDebug("Signing GitHub App JWT for AppId {AppId} with key {KeyId}", _authModeSrv.GetAppId(), key.Id);
            string token = new JwtSecurityTokenHandler().WriteToken(jwt);

            // have to get an install authenticated GitHubClient to do operations
            GitHubClient intermediate = new GitHubClient(new ProductHeaderValue("RetireaBot"))
            {
                Credentials = new Credentials(token, AuthenticationType.Bearer)
            };

            var response = RequestInstallationTokenAsync(
                intermediate.GitHubApps, _authModeSrv.GetAppId()!, _authModeSrv.GetAppInstallId()!.Value, key.Id, _logger
            ).GetAwaiter().GetResult();
            _tokenExpiry = response.ExpiresAt.AddMinutes(-5);

            _logger.LogInformation("Created GitHub App installation client for AppId {AppId}, InstallationId {InstallationId}; refresh after {Expiry}",
                _authModeSrv.GetAppId(), _authModeSrv.GetAppInstallId(), _tokenExpiry);

            return new GitHubClient(new ProductHeaderValue("RetireaBot"))
            {
                Credentials = new Credentials(response.Token)
            };
        }

        internal static async Task<Octokit.AccessToken> RequestInstallationTokenAsync(
            IGitHubAppsClient apps, string appId, long installationId, Uri keyId, ILogger logger)
        {
            logger.LogInformation("Requesting GitHub installation token for AppId {AppId}, InstallationId {InstallationId}, signing key {KeyId}",
                appId, installationId, keyId);
            try
            {
                return await apps.CreateInstallationToken(installationId);
            }
            catch (ApiException exception)
            {
                string guidance = exception.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized =>
                        "GitHub rejected the App JWT. Check GitHub:AppId and that the configured Key Vault key belongs to that same GitHub App and has not been revoked or replaced. Stale App/key configuration, JWT formatting, or clock drift can cause this; the response does not identify which.",
                    System.Net.HttpStatusCode.NotFound =>
                        "Check GitHub:AppInstallId, that the installation still exists, and that it belongs to GitHub:AppId.",
                    System.Net.HttpStatusCode.Forbidden =>
                        "Check whether the GitHub App installation is suspended or access is restricted. Consult the GitHub response for permission or rate-limit details.",
                    _ => "Check the GitHub response and service status."
                };
                string message = $"GitHub App authentication failed while requesting an installation token " +
                    $"(HTTP {(int)exception.StatusCode}, AppId '{appId}', InstallationId '{installationId}', signing key '{keyId}'). " +
                    $"{guidance} GitHub response: {exception.Message}";
                logger.LogError(exception,
                    "GitHub installation token request failed: HTTP {StatusCode}, AppId {AppId}, InstallationId {InstallationId}, KeyId {KeyId}. {Guidance}",
                    (int)exception.StatusCode, appId, installationId, keyId, guidance);
                throw new InvalidOperationException(message, exception);
            }
        }
    }
}
