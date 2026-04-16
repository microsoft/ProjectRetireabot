using Moq;
using Retirebot.Helpers;
using Retirebot.Models.Azure;

namespace Retirebot.Tests.Helpers
{
    public class WorkItemClientTest
    {
        [Fact]
        public async Task WorkItemClient_MockExample()
        {
            var mockClient = new Mock<IWorkItemClient>();

            mockClient
                .Setup(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>()))
                .ReturnsAsync(new Dictionary<string, Models.WorkItem>());

            mockClient
                .Setup(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new List<(Advisory, Models.WorkItem)>());

            IWorkItemClient client = mockClient.Object;
            var result = await client.FindExistingByAdvisoryAsync([], "owner/repo");

            Assert.Empty(result);

            // Verify a method was (or wasn't) called
            mockClient.Verify(c => c.FindExistingByAdvisoryAsync(It.IsAny<List<Advisory>>(), "owner/repo"), Times.Once);
            mockClient.Verify(c => c.CreateBatchAsync(It.IsAny<List<Advisory>>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }
    }
}