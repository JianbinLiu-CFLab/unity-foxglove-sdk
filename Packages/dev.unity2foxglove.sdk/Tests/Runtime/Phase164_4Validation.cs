using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_4Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-4 Tests ---");
            _passed = 0;

            VerifyAssetRegistryAvoidsFetchAllocations();
            VerifyParameterPathsAvoidTransientCopies();
            VerifyConnectionGraphBroadcastUsesScratchSubscribers();
            VerifyServiceDrainAvoidsEmptyListAllocations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-4: " + _passed + " checks passed.\n");
        }

        private static void VerifyAssetRegistryAvoidsFetchAllocations()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            var tryResolve = SourceMethod(source, "private bool TryResolve(string uri, out string path, out long maxBytes, out string error)");
            var tryRead = SourceMethod(source, "private static bool TryReadResolvedFile(string path, long maxBytes, out byte[] bytes, out string error)");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !tryResolve.Contains(".OrderByDescending", StringComparison.Ordinal)
                  && !tryResolve.Contains(".ToList()", StringComparison.Ordinal)
                  && tryResolve.Contains("bestPrefixLength", StringComparison.Ordinal),
                "164-4A-1: asset URI resolution scans roots in-place without LINQ/list snapshots");
            Check(source.Contains("using System.Buffers;", StringComparison.Ordinal)
                  && tryRead.Contains("ArrayPool<byte>.Shared.Rent", StringComparison.Ordinal)
                  && tryRead.Contains("ArrayPool<byte>.Shared.Return", StringComparison.Ordinal)
                  && !tryRead.Contains("new byte[81920]", StringComparison.Ordinal),
                "164-4A-2: asset reads use ArrayPool for the read buffer");
        }

        private static void VerifyParameterPathsAvoidTransientCopies()
        {
            var store = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/FoxgloveParameterStore.cs");
            var parameters = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Parameters.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var normalize = SourceMethod(store, "public static bool TryNormalizeValueForType(string type, JToken value, out JToken normalized)");
            var setParameters = SourceMethod(parameters, "private void HandleSetParameters(uint clientId, string json)");
            var broadcast = SourceMethod(parameters, "public void BroadcastParameterValues(IEnumerable<string> parameterNames)");

            Check(normalize.Contains("normalized = value;", StringComparison.Ordinal)
                  && normalize.Contains("copy.Add(item.DeepClone())", StringComparison.Ordinal),
                "164-4B-1: scalar parameter normalization reuses immutable scalar tokens while arrays are cloned");
            Check(!parameters.Contains("using System.Linq;", StringComparison.Ordinal)
                  && !setParameters.Contains(".Select", StringComparison.Ordinal)
                  && !setParameters.Contains(".Where", StringComparison.Ordinal)
                  && setParameters.Contains("requestedNames", StringComparison.Ordinal),
                "164-4B-2: setParameters reuses explicit requested/changed name lists instead of LINQ chains");
            Check(store.Contains("GetWireParameters(IReadOnlyList<string> names)", StringComparison.Ordinal)
                  && !SourceMethod(store, "public List<Parameter> GetWireParameters(IReadOnlyList<string> names)").Contains("requestedNames", StringComparison.Ordinal),
                "164-4B-3: parameter store has a no-copy IReadOnlyList wire lookup path");
            Check(session.Contains("_parameterBroadcastSeen", StringComparison.Ordinal)
                  && session.Contains("_parameterBroadcastNames", StringComparison.Ordinal)
                  && broadcast.Contains("_parameterBroadcastSeen.Clear()", StringComparison.Ordinal)
                  && !broadcast.Contains("new HashSet<string>()", StringComparison.Ordinal),
                "164-4B-4: parameter broadcasts reuse dedupe scratch collections");
        }

        private static void VerifyConnectionGraphBroadcastUsesScratchSubscribers()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ConnectionGraphRegistry.cs");
            var handler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionGraphHandler.cs");
            var copySubscribers = SourceMethod(registry, "public void CopySubscribersTo(List<uint> destination)");
            var broadcast = SourceMethod(handler, "public void BroadcastUpdate()");

            Check(copySubscribers.Contains("destination.Clear()", StringComparison.Ordinal)
                  && !broadcast.Contains("_graph.GetSubscribers()", StringComparison.Ordinal)
                  && handler.Contains("_subscriberScratch", StringComparison.Ordinal),
                "164-4C: graph broadcasts reuse a subscriber scratch list instead of allocating a subscriber snapshot");
        }

        private static void VerifyServiceDrainAvoidsEmptyListAllocations()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Services.cs");

            Check(registry.Contains("public void CopyPendingCallsTo(List<FoxgloveServiceCall> destination)", StringComparison.Ordinal)
                  && registry.Contains("public void DrainCompletedTo(List<FoxgloveServiceCall> destination)", StringComparison.Ordinal)
                  && session.Contains("_pendingServiceCallsScratch", StringComparison.Ordinal)
                  && session.Contains("_completedServiceCallsScratch", StringComparison.Ordinal)
                  && !session.Contains("_services.GetPendingCalls()", StringComparison.Ordinal)
                  && !session.Contains("_services.DrainCompleted()", StringComparison.Ordinal),
                "164-4D: service drain reuses caller-owned pending/completed scratch lists");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-4\"", StringComparison.Ordinal), "164-4E-1: validation registry exposes Phase164-4");
            Check(project.Contains("Phase164_4Validation.cs", StringComparison.Ordinal), "164-4E-2: runtime validation project compiles Phase164-4");
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                throw new InvalidOperationException("Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(
                        dir.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "Runtime",
                        "FoxgloveSdk.Tests.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
