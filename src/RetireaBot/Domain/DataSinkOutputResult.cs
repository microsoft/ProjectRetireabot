namespace Microsoft.RetireaBot.Domain
{
    public class DataSinkOutputResult
    {
        public required string BackendName { get; set; }      // "NoOp", "PowerBI", ...
        public required GetRetirementsResult Status { get; set; }
        public string? Error { get; set; }
        public int PushedCount { get; set; }
    }
}