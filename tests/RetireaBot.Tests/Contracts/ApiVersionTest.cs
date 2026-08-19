using Microsoft.RetireaBot.Contracts;

namespace Microsoft.RetireaBot.Tests.Contracts
{
    public class ApiVersionTest
    {
        private static readonly IReadOnlyList<ApiVersion> Supported = new[]
        {
            new ApiVersion(new DateOnly(2026,6,25), true),
        };

        [Theory]
        [InlineData("2026-06-25-preview", true)]
        [InlineData("2026-06-25", false)]
        [InlineData("2026-06-25-PREVIEW", true)]
        [InlineData("2026-13-01-preview", false)]
        [InlineData("not-a-date", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("2026/06/25-preview", false)]
        public void TryParse_Cases(string? raw, bool expected) => Assert.Equal(expected, ApiVersion.TryParse(raw, Supported, out _));

        [Fact]
        public void TryParse_Valid_SetsDateAndPreview()
        {
            Assert.True(ApiVersion.TryParse("2026-06-25-preview", Supported, out var v));
            Assert.Equal(new DateOnly(2026, 6, 25), v.Date);
            Assert.True(v.IsPreview);
        }
    }
}