namespace Microsoft.RetireaBot.Models.Lifecycle
{
    public class LifecycleEntry
    {
        public required LifecycleProduct Product { get; init; }

        public required string Version { get; init; }

        public required DateTime EndOfLife { get; init; }
        public string? SourceUrl { get; init; }

        public override string ToString()
        {
            return $"{Product} {Version} (EOL ({EndOfLife:yyyy-MM-dd}))";
        }
    }
}