using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core.Serialization;
using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Contracts.V2026_06_25
{
    public sealed class ResponseProjector : IResponseProjector
    {
        private static readonly ApiVersion _version = new ApiVersion(new DateOnly(2026, 6, 25), true);

        // Per-version, frozen, cached. A future version can differ without touching this one.
        private static readonly ObjectSerializer _serializer = new JsonObjectSerializer(new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        });

        public ObjectSerializer Serializer => _serializer;
        public ApiVersion Version => _version;

        public async Task<ProjectedResponse> Project(Domain.RetirementReport r, string mediaType) => mediaType switch
        {
            "application/json" => await ToJSON(r),
            "text/csv" => ToCSV(r),
            _ => throw new InvalidOperationException($"Unsupported media type \"{mediaType}\"")
        };

        private async Task<ProjectedResponse> ToJSON(Domain.RetirementReport r)
        {
            return new ProjectedResponse()
            {
                ContentType = "application/json; charset=utf-8",
                Body = await Serializer.SerializeAsync(new GetRetirementsResponse
                {
                    Result = MapResult(r.Result),
                    ResultDescription = r.Description,
                    Advisories = r.Advisories?.Select(MapAdvisory).ToList(),
                    BackendOutputs = r.BackendOutputs.Select(MapBackend).ToList(),
                    SinkOutputs = r.SinkOutputs.Select(MapSink).ToList(),
                    TimeElapsed = r.TimeElapsed,
                    WhatIf = r.WhatIf,
                })
            };
        }

        private ProjectedResponse ToCSV(Domain.RetirementReport r)
        {
            StringBuilder sb = new StringBuilder();
            AppendLine(sb, RetirementRowSchema.Columns);                       // header
            foreach (var row in (r.Advisories ?? []).Select(RetirementRow.FromAdvisory))
                AppendLine(sb, RetirementRowSchema.Values(row));               // data
            return new ProjectedResponse
            {
                ContentType = "text/csv; charset=utf-8",
                Body = BinaryData.FromString(sb.ToString()),
            };
        }

        private static void AppendLine(StringBuilder sb, IEnumerable<string?> fields)
        {
            bool first = true;
            foreach (var f in fields)
            {
                if (!first) sb.Append(',');
                sb.Append(Escape(f));
                first = false;
            }
            sb.Append("\r\n");
        }

        private static string Escape(string? value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }

        private static GetRetirementsResult MapResult(Domain.GetRetirementsResult s) => s switch
        {
            Domain.GetRetirementsResult.Success => GetRetirementsResult.Success,
            Domain.GetRetirementsResult.Partial => GetRetirementsResult.Partial,
            Domain.GetRetirementsResult.Failure => GetRetirementsResult.Failure,
            _ => GetRetirementsResult.Unknown,
        };

        private static WorkItemState MapState(Models.WorkItemState s) => s switch
        {
            Models.WorkItemState.Closed => WorkItemState.Closed,
            _ => WorkItemState.Open,
        };

        private static ParentWorkItemAction MapAction(Domain.ParentWorkItemAction a) => a switch
        {
            Domain.ParentWorkItemAction.Created => ParentWorkItemAction.Created,
            Domain.ParentWorkItemAction.Updated => ParentWorkItemAction.Updated,
            _ => ParentWorkItemAction.Unchanged,
        };

        // ---- type mappings ----

        private static Advisory MapAdvisory(Models.Azure.Advisory a) => new()
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type,
            SubscriptionId = a.GetSubscriptionId(),
            ResourceGroup = a.GetResourceGroupName(),
            ResourceName = a.GetResourceName(),
            Category = a.Properties.Category,
            Impact = a.Properties.Impact,
            RetirementDate = a.Properties.ExtendedProperties.RetirementDate,
            RetirementFeatureName = a.Properties.ExtendedProperties.RetirementFeatureName,
            LastUpdated = a.Properties.LastUpdated,
            Problem = a.Properties.ShortDescription.Problem,
            Solution = a.Properties.ShortDescription.Solution,
        };

        private static BackendOutputResult MapBackend(Domain.BackendOutputResult b) => new()
        {
            BackendName = b.BackendName,
            Status = MapResult(b.Status),
            Error = b.Error,
            ExistingCount = b.Existing.Count,
            Created = b.Created.Select(MapWorkItem).ToList(),
            Parents = b.Parents.Select(MapParent).ToList(),
        };

        private static DataSinkOutputResult MapSink(Domain.DataSinkOutputResult d) => new()
        {
            BackendName = d.BackendName,
            Status = MapResult(d.Status),
            Error = d.Error,
            PushedCount = d.PushedCount,
        };

        private static WorkItem MapWorkItem(Models.WorkItem w) => new()
        {
            Id = w.Id,
            Number = w.Number,
            Title = w.Title,
            State = MapState(w.State),
            Url = w.Url,
        };

        private static ParentWorkItemResult MapParent(Domain.ParentWorkItemResult p) => new()
        {
            WorkItem = MapWorkItem(p.WorkItem),
            Action = MapAction(p.Action),
            RecommendationTypeId = p.RecommendationTypeId,
            ChildCount = p.ChildCount,
        };
    }
}