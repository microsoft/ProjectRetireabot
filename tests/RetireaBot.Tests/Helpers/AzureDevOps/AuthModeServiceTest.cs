using Microsoft.Extensions.Configuration;
using Microsoft.RetireaBot.Helpers.AzureDevOps;
using Microsoft.RetireaBot.Models;
using Microsoft.RetireaBot.Models.AzureDevOps;

namespace Microsoft.RetireaBot.Tests.Helpers.AzureDevOps
{
    public class AuthModeServiceTest
    {
        private static IConfiguration BuildConfig(Dictionary<string, string?> settings) =>
           new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        [Fact]
        public void GetAuthMode_NoCredentials_ReturnsNone()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>()));
            Assert.Equal(AuthMode.BuiltIn, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_AzureClientId_ReturnsBuiltIn()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
            }));

            Assert.Equal(AuthMode.BuiltIn, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_CertificateOnly_ReturnsCertificate()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.ClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
                [ConfigKeys.AzureDevOps.TenantId] = "27d78fb1-38c6-4059-b33c-8b77243b0fe1",
                [ConfigKeys.AzureDevOps.CertificateId] = "retireabot",
            }));

            Assert.Equal(AuthMode.Certificate, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_SecretOnly_ReturnsSecret()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.ClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
                [ConfigKeys.AzureDevOps.TenantId] = "27d78fb1-38c6-4059-b33c-8b77243b0fe1",
                [ConfigKeys.AzureDevOps.ClientSecret] = "03b0bf30-3ed5-4e8a-826b-b8f8f9f31698",
            }));

            Assert.Equal(AuthMode.ClientSecret, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_ManagedIdentityOnly_ReturnsManagedIdentity()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.ClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
                [ConfigKeys.AzureDevOps.TenantId] = "27d78fb1-38c6-4059-b33c-8b77243b0fe1",
            }));

            Assert.Equal(AuthMode.ManagedIdentity, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_PATOnly_ReturnsPAT()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.PAT] = "ado_test123"
            }));

            Assert.Equal(AuthMode.PAT, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_AllPopulated_ReturnsCertificate()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
                [ConfigKeys.AzureDevOps.ClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
                [ConfigKeys.AzureDevOps.TenantId] = "27d78fb1-38c6-4059-b33c-8b77243b0fe1",
                [ConfigKeys.AzureDevOps.CertificateId] = "retireabot",
                [ConfigKeys.AzureDevOps.ClientSecret] = "03b0bf30-3ed5-4e8a-826b-b8f8f9f31698",
                [ConfigKeys.AzureDevOps.PAT] = "ado_test123"
            }));

            Assert.Equal(AuthMode.Certificate, service.GetAuthMode());
        }

        [Fact]
        public void GetAuthMode_PartialAppConfig_ReturnsBuiltIn()
        {
            var service = new AuthModeService(BuildConfig(new Dictionary<string, string?>
            {
                [ConfigKeys.AzureDevOps.ClientId] = "a443af02-50e0-4b7a-910b-3a87ce09c674",
            }));

            Assert.Equal(AuthMode.BuiltIn, service.GetAuthMode());
        }
    }
}
