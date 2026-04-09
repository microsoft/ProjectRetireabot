using Microsoft.IdentityModel.Tokens;

namespace Retirebot.Helpers
{
    public class EmptySecurityKey : SecurityKey
    {
        public override int KeySize => 256;
    }
}
