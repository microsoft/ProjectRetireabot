namespace Microsoft.RetireaBot.Contracts
{
    public readonly record struct ApiVersion(DateOnly Date, bool IsPreview)
    {
        public static bool TryParse(string? raw, IReadOnlyList<ApiVersion> supported, out ApiVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            bool requestedPreview = raw.EndsWith("-preview", StringComparison.OrdinalIgnoreCase);
            var datePart = requestedPreview ? raw[..^"-preview".Length] : raw;

            if (!DateOnly.TryParseExact(datePart, "yyyy-MM-dd", out var d)) return false;
            if (!supported.Any(ver => ver.Date == d && ver.IsPreview == requestedPreview)) return false; // unknown date

            version = new ApiVersion(d, requestedPreview);
            return true;
        }
    }
}