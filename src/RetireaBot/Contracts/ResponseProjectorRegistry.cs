namespace Microsoft.RetireaBot.Contracts
{
    public sealed class ResponseProjectorRegistry
    {
        private readonly IReadOnlyList<IResponseProjector> _projectors;
        public ResponseProjectorRegistry(IEnumerable<IResponseProjector> projectors) =>
        _projectors = projectors.OrderByDescending(p => p.Version.Date).ToList();

        public IReadOnlyList<ApiVersion> SupportedVersions => _projectors.Select(p => p.Version).ToList();

        public bool TryResolve(ApiVersion requested, out IResponseProjector projector)
        {
            projector = _projectors.FirstOrDefault(p =>
                p.Version.IsPreview == requested.IsPreview &&
                p.Version.Date <= requested.Date)!;
            return projector != null;
        }
    }
}