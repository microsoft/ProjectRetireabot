using Microsoft.RetireaBot.Models.Azure;

namespace Microsoft.RetireaBot.Domain
{
    public sealed record RetirementReport()
    {
        public required GetRetirementsResult Result;
        public required string Description;
        public IReadOnlyList<Advisory> Advisories = [];
        public IReadOnlyList<BackendOutputResult> BackendOutputs = [];
        public IReadOnlyList<DataSinkOutputResult> SinkOutputs = [];
        public double TimeElapsed;
        public bool WhatIf;
    }
}