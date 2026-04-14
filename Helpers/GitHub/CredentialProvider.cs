using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.KeyVaultExtensions;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using Retirebot.Models.GitHub;
using System.IdentityModel.Tokens.Jwt;

namespace Retirebot.Helpers.GitHub
{
    public class CredentialProvider
    {
        private GitHubClient _primaryClient;
        private GitHubClient? _coPilotClient;

        private readonly KeyClient? _keyClient;
        private readonly DefaultAzureCredential _credentials;
        private readonly ILogger _logger;

        private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private readonly AuthModeService _authModeSrv;

        public CredentialProvider(IConfiguration config, ILoggerFactory loggerFactory, DefaultAzureCredential credentials, AuthModeService authModeService, KeyClient? keyClient)
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
            } finally
            {
                _refreshLock.Release();
            }
        }

        private GitHubClient CreateClient(string token)
        {
            return new GitHubClient(new ProductHeaderValue("Retirebot"))
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

            KeyVaultKey key = _keyClient.GetKey(_authModeSrv.GetPrivateKeyId());
            if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
            {
                throw new InvalidOperationException($"GitHub App Private Key must be an RSA based key, got KeyType \"{key.KeyType}\"");
            }

            CryptographyClient cryptoClient = _keyClient.GetCryptographyClient(key.Name, key.Properties.Version);
            KeyVaultSecurityKey securityKey = new KeyVaultSecurityKey(key.Id.ToString(), async(auth, resource, scope) =>
            {
                var token = await _credentials.GetTokenAsync(
                    new TokenRequestContext(new[] { resource + "/.default" }));
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

            string token = new JwtSecurityTokenHandler().WriteToken(jwt);

            // have to get an install authenticated GitHubClient to do operations
            GitHubClient intermediate = new GitHubClient(new ProductHeaderValue("Retirebot"))
            {
                Credentials = new Credentials(token, AuthenticationType.Bearer)
            };

            var response = intermediate.GitHubApps.CreateInstallationToken(_authModeSrv.GetAppInstallId()!.Value);
            _tokenExpiry = DateTimeOffset.UtcNow.AddMinutes(55); // Install tokens expire every hour, refresh the token 5 minutes early to avoid any weirdness

            _logger.LogInformation($"Created new App Client, valid until {_tokenExpiry.ToString()}");

            return new GitHubClient(new ProductHeaderValue("Retirebot"))
            {
                Credentials = new Credentials(response.Result.Token)
            };
        }
    }
}
