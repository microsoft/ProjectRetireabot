namespace Retirebot.Models
{
    [Flags]
    public enum GitHubAuthMode
    {
        None = 0,
        PAT = 1 << 0,
        App = 1 << 1,
        Hybrid = PAT | App
    }
}
