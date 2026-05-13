using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Tests.Models
{
    public class AdvisoryTest
    {
        [Fact]
        public void GetSubscriptionId_ParsesFromId()
        {
            var advisory = new Advisory
            {
                Id = "/subscriptions/sub-123/resourceGroups/rg-1/providers/Microsoft.Advisor/recommendations/rec-1"
            };

            Assert.Equal("sub-123", advisory.GetSubscriptionId());
        }

        [Fact]
        public void GetResourceGroupName_ParsesFromId()
        {
            var advisory = new Advisory
            {
                Id = "/subscriptions/sub-123/resourceGroups/my-rg/providers/Microsoft.Advisor/recommendations/rec-1"
            };

            Assert.Equal("my-rg", advisory.GetResourceGroupName());
        }

        [Fact]
        public void GetResourceName_ReturnsImpactedValue()
        {
            var advisory = new Advisory
            {
                Properties = new AdvisoryProperties { ImpactedValue = "myapp" }
            };

            Assert.Equal("myapp", advisory.GetResourceName());
        }

        [Fact]
        public void GetSubscriptionId_MalformedId_ThrowsIndexOutOfRange()
        {
            var advisory = new Advisory { Id = "/subscriptions" };

            Assert.Throws<IndexOutOfRangeException>(() => advisory.GetSubscriptionId());
        }
    }
}