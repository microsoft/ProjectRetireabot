using System.Text.Json;
using Microsoft.RetireaBot.Models;

namespace Microsoft.RetireaBot.Helpers.Azure
{
    public static class ScanQueryScope
    {
        public static string Apply(string query, string resourceIdExpression, string subscriptionId, ResolvedScanOptions? scan)
        {
            if (scan == null) return query;
            ScanScope[] scopes = scan.Scopes
                .Where(scope => scope.SubscriptionId.Equals(subscriptionId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (scopes.Length == 0) throw new InvalidOperationException("Subscription is outside the configured scan scopes.");

            string predicate = string.Join(" or ", scopes.Select(scope =>
                $"(tolower({resourceIdExpression}) == {JsonSerializer.Serialize(scope.ScopeId.ToLowerInvariant())} or tolower({resourceIdExpression}) startswith {JsonSerializer.Serialize(scope.ScopeId.ToLowerInvariant() + "/")})"));
            return $"{query} | where {predicate}";
        }

        public static bool Contains(ResolvedScanOptions? scan, string resourceId, string subscriptionId)
        {
            if (scan == null) return true;
            if (string.IsNullOrEmpty(resourceId)) return false;
            return scan.Scopes.Any(scope =>
                scope.SubscriptionId.Equals(subscriptionId, StringComparison.OrdinalIgnoreCase)
                && (resourceId.StartsWith(scope.ScopeId + "/", StringComparison.OrdinalIgnoreCase)
                    || resourceId.Equals(scope.ScopeId, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
