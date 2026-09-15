using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    /// <summary>Pure, deterministic projection of an immutable Component session.</summary>
    public static class ComponentPublisherContractCatalog
    {
        public const int Version = 1;

        public static JObject BuildResponse(ComponentPublisherSessionSnapshot snapshot, string requestedTopic = null, bool includeTypeShape = false)
        {
            if (snapshot == null || !snapshot.IsAvailable)
            {
                return new JObject
                {
                    ["contractVersion"] = Version,
                    ["sessionReady"] = false,
                    ["generation"] = snapshot?.Generation ?? 0,
                    ["results"] = new JArray(new JObject
                    {
                        ["code"] = "session_not_ready",
                        ["diagnostic"] = snapshot?.Diagnostic ?? "component session is not ready"
                    })
                };
            }

            var projections = snapshot.Entries
                .Where(e => e != null && (string.IsNullOrEmpty(requestedTopic) || string.Equals(e.Topic, requestedTopic, StringComparison.Ordinal)))
                .Select(e => new ComponentPublisherContractProjection(snapshot, e))
                .OrderBy(e => e.Topic, StringComparer.Ordinal)
                .ThenBy(e => e.PublisherType, StringComparer.Ordinal)
                .ThenBy(e => e.CaptureIdentity, StringComparer.Ordinal)
                .ThenBy(e => e.Mode, StringComparer.Ordinal)
                .Take(ComponentPublisherContractCatalogLimits.MaxResults + 1)
                .ToArray();

            var truncated = projections.Length > ComponentPublisherContractCatalogLimits.MaxResults;
            var response = new JObject
            {
                ["contractVersion"] = Version,
                ["sessionReady"] = true,
                ["generation"] = snapshot.Generation,
                ["sourceKind"] = "component",
                ["responseTruncated"] = truncated,
                ["results"] = new JArray(projections.Take(ComponentPublisherContractCatalogLimits.MaxResults).Select(p => p.ToJson(includeTypeShape)))
            };
            var bytes = Encoding.UTF8.GetByteCount(response.ToString(Newtonsoft.Json.Formatting.None));
            if (bytes > ComponentPublisherContractCatalogLimits.MaxResponseBytes)
            {
                return new JObject
                {
                    ["contractVersion"] = Version,
                    ["sessionReady"] = true,
                    ["generation"] = snapshot.Generation,
                    ["error"] = "response_limit_exceeded",
                    ["results"] = new JArray()
                };
            }
            return response;
        }
    }
}
