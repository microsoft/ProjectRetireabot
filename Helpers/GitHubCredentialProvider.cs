using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.KeyVaultExtensions;
using Microsoft.IdentityModel.Tokens;
using Octokit;
using System.IdentityModel.Tokens.Jwt;

namespace Retirebot.Helpers
{
    [Flags]
    public enum GitHubAuthMode
    {
        None = 0,
        PAT = 1 << 0,     
        App = 1 << 1,     
        Hybrid = PAT | App  
    }

    public class GitHubCredentialProvider
    {
        private readonly GitHubClient _primaryClient;
        private readonly GitHubClient? _coPilotClient;

        private readonly KeyClient? _keyClient;
        private readonly DefaultAzureCredential _credentials;

        public GitHubAuthMode AuthMode { get; }

        public GitHubCredentialProvider(IConfiguration config, ILoggerFactory loggerFactory, DefaultAzureCredential credentials, KeyClient? keyClient)
        {
            _keyClient = keyClient;
            _credentials = credentials;

            ILogger logger = loggerFactory.CreateLogger<GitHubCredentialProvider>();

            string? pat = config.GetSection("GitHub:PAT").Get<string>();
            
            string? appId = config.GetSection("GitHub:AppId").Get<string>();
            string? appPrivateKeyId = config.GetSection("GitHub:AppPrivateKeyId").Get<string>();

            AuthMode = (!string.IsNullOrEmpty(pat) ? GitHubAuthMode.PAT : GitHubAuthMode.None) | (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(appPrivateKeyId) ? GitHubAuthMode.App : GitHubAuthMode.None);
        
            switch (AuthMode)
            {
                case GitHubAuthMode.Hybrid:
                    logger.LogInformation("GitHub AuthMode: Hybrid - App (Primary) + PAT (Secondary)");
                    _primaryClient = CreateClient(appId!, appPrivateKeyId!);
                    _coPilotClient = CreateClient(pat!);
                    break;
                case GitHubAuthMode.PAT:
                    logger.LogInformation("GitHub AuthMode: PAT mode");
                    _primaryClient = CreateClient(pat!);
                    _coPilotClient = _primaryClient;
                    break;
                case GitHubAuthMode.App:
                    logger.LogInformation("GitHub AuthMode: App mode");
                    _primaryClient = CreateClient(appId!, appPrivateKeyId!);
                    break;
                case GitHubAuthMode.None:
                default:
                    throw new InvalidOperationException("No supported GitHub credentials are available. Provide GitHub:PAT and/or GitHub:AppId");
            }
        }

        public GitHubClient GetPrimaryClient() => _primaryClient;
        public GitHubClient? GetCopilotCapableClient() => _coPilotClient;

        private GitHubClient CreateClient(string token)
        {
            return new GitHubClient(new ProductHeaderValue("Retirebot"))
            {
                Credentials = new Credentials(token)
            };
        }

        private GitHubClient CreateClient(string appId, string privateKeyId)
        {
            if (_keyClient == null)
            {
                throw new ArgumentNullException("_keyClient", "When using AuthMode \"App\", a valid connection to a KeyVault has to be present. Please check your configuration.");
            }

            KeyVaultKey key = _keyClient.GetKey(privateKeyId);
            if (key.KeyType != KeyType.Rsa && key.KeyType != KeyType.RsaHsm)
            {
                throw new InvalidOperationException($"GitHub App Private Key must be an RSA based key, got KeyType \"{key.KeyType}\"");
            }

            CryptographyClient cryptoClient = _keyClient.GetCryptographyClient(key.Name, key.Properties.Version);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            List<System.Security.Claims.Claim> claims = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString()),
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Exp, now.AddMinutes(9).ToUnixTimeSeconds().ToString()),
                    new System.Security.Claims.Claim("iss", appId.ToString())
                };

            SigningCredentials signingCredentials = new SigningCredentials(new EmptySecurityKey(), SecurityAlgorithms.RsaSha256);

            JwtHeader jwtHeader = new JwtHeader(signingCredentials);
            JwtPayload jwtPayload = new JwtPayload((IEnumerable<System.Security.Claims.Claim>)claims);
            JwtSecurityToken jwt = new JwtSecurityToken(jwtHeader, jwtPayload);

            string headerBase64 = jwtHeader.Base64UrlEncode();
            string payloadBase64 = jwtPayload.Base64UrlEncode();
            string unsignedToken = $"{headerBase64}.{payloadBase64}";

            byte[] digest = System.Security.Cryptography.SHA256.HashData(
               System.Text.Encoding.UTF8.GetBytes(unsignedToken));

            SignResult signResult = cryptoClient.Sign(SignatureAlgorithm.RS256, digest);

            string signature = Base64UrlEncoder.Encode(signResult.Signature);
            string signedToken = $"{unsignedToken}.{signature}";

            return new GitHubClient(new ProductHeaderValue("Retirebot"))
            {
                Credentials = new Credentials(signedToken, AuthenticationType.Bearer)
            };
        }
    }
}
