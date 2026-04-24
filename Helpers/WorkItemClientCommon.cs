using Retirebot.Models.Azure;

namespace Retirebot.Helpers
{
    public class WorkItemClientCommon
    {
        public static string GenerateAdvisoryLabel(string prefix, string advisoryName, int maxLength)
        {
            var label = $"{prefix}{advisoryName}";
            if (label.Length > maxLength)
            {
                label = label[..maxLength];
            }
            return label;
        }

        public static string GenerateWorkItemTitle(Advisory advisory)
        {
            return $"{advisory.Properties.ShortDescription.Problem} - {advisory.Properties.ImpactedValue}";
        }
    }
}
