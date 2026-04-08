using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Octokit;

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
        public GitHubAuthMode AuthMode { get; }

        public GitHubCredentialProvider(IConfiguration config, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger<GitHubCredentialProvider>();
            string? pat = config.GetSection("GitHub:PAT").Get<string>();
            string? appId = config.GetSection("GitHub:AppId").Get<string>();

            AuthMode = (!string.IsNullOrEmpty(pat) ? GitHubAuthMode.PAT : GitHubAuthMode.None) | (!string.IsNullOrEmpty(appId) ? GitHubAuthMode.App : GitHubAuthMode.None);
        
            switch (AuthMode)
            {
                case GitHubAuthMode.Hybrid:
                    logger.LogInformation("GitHub AuthMode: Hybrid - App (Primary) + PAT (Secondary)");
                    _primaryClient = CreateClient(pat!);
                    _coPilotClient = CreateClient(pat!);
                    break;
                case GitHubAuthMode.PAT:
                    logger.LogInformation("GitHub AuthMode: PAT mode");
                    _primaryClient = CreateClient(pat!);
                    _coPilotClient = _primaryClient;
                    break;
                case GitHubAuthMode.App:
                    logger.LogInformation("GitHub AuthMode: App mode");
                    throw new NotImplementedException("GitHub App login not yet implemented");
                case GitHubAuthMode.None:
                default:
                    throw new InvalidOperationException("No supported GitHub credentials are available. Provide GitHub:PAT and/or GitHub:AppId");
            }
        }

        public GitHubClient GetPrimaryClient() => _primaryClient;
        public GitHubClient? GetCopilotCapableClient() => _coPilotClient;

        private static GitHubClient CreateClient(string token)
        {
            return new GitHubClient(new ProductHeaderValue("Retirebot"))
            {
                Credentials = new Credentials(token)
            };
        }
    }
}
