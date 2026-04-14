using Microsoft.Extensions.Configuration;
using Retirebot.Models;
using Retirebot.Models.GitHub;

namespace Retirebot.Helpers.GitHub
{
    public class AuthModeService
    {
        private readonly string? _appId;
        private readonly string? _appPrivateKeyId;
        private readonly string? _pat;
        private readonly long? _appInstallId;

        private AuthMode AuthMode;

        public AuthModeService(IConfiguration config)
        {
            _appId = config.GetSection(ConfigKeys.GitHub.AppId).Get<string>();
            _appPrivateKeyId = config.GetSection(ConfigKeys.GitHub.AppPrivateKeyId).Get<string>();
            _appInstallId = config.GetSection(ConfigKeys.GitHub.AppInstallId).Get<long?>();
            _pat = config.GetSection(ConfigKeys.GitHub.PAT).Get<string>();

            AuthMode = (!string.IsNullOrEmpty(_pat) ? AuthMode.PAT : AuthMode.None) | (!string.IsNullOrEmpty(_appId) && !string.IsNullOrEmpty(_appPrivateKeyId) && _appInstallId != null ? AuthMode.App : AuthMode.None);
        }

        public AuthMode GetAuthMode() { return AuthMode; }

        public string? GetPAT() { return _pat; }
        public string? GetAppId() { return _appId; }
        public string? GetPrivateKeyId() { return _appPrivateKeyId; }
        public long? GetAppInstallId() { return _appInstallId; }
    }
}
