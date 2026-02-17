using Google.Protobuf.WellKnownTypes;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace Retirebot;

public static class GitHubAuthenticationHandler
{
    public static string GetJWT(long appId, string privateKey)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKey);

        var signingCredentials = new SigningCredentials(
            new RsaSecurityKey(rsa),
            SecurityAlgorithms.RsaSha256);

        var now = DateTimeOffset.UtcNow;
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString()),
            new System.Security.Claims.Claim(JwtRegisteredClaimNames.Exp, now.AddMinutes(9).ToUnixTimeSeconds().ToString()),
            new System.Security.Claims.Claim("iss", appId.ToString())
        };

        var jwtHeader = new JwtHeader(signingCredentials);
        var jwtPayload = new JwtPayload((IEnumerable<System.Security.Claims.Claim>)claims);
        var jwt = new JwtSecurityToken(jwtHeader, jwtPayload);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}