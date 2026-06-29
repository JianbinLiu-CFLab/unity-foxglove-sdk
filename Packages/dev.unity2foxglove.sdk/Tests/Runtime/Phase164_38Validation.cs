using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_38Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-38 Tests ---");
            _passed = 0;

            VerifyCursorEndpointUsesPooledRequestBuffersAndCachedResponses();
            VerifyCursorEndpointAvoidsCorsTrimAllocationsOnAllowCheck();
            VerifyExternalCursorDrainAvoidsIdleLock();
            VerifyExtensionAvoidsPerSendAndPerFollowAllocations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-38: " + _passed + " checks passed.\n");
        }

        private static void VerifyCursorEndpointUsesPooledRequestBuffersAndCachedResponses()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var readBody = PhaseValidationSourceHelpers.SourceMethod(source, "private string ReadBody");
            var handle = PhaseValidationSourceHelpers.SourceMethod(source, "private void Handle");

            Check(source.Contains("using System.Buffers;", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Rent(_options.MaxBodyBytes + 1)", StringComparison.Ordinal)
                  && readBody.Contains("ArrayPool<byte>.Shared.Return(buffer)", StringComparison.Ordinal)
                  && !source.Contains("private byte[] _readBodyBuffer", StringComparison.Ordinal),
                "164-38A-1: cursor endpoint rents a per-request body buffer instead of sharing a mutable field");
            Check(source.Contains("private static readonly byte[] DuplicateCursorResponseBytes", StringComparison.Ordinal)
                  && handle.Contains("TryWrite(context, 409, DuplicateCursorResponseBytes)", StringComparison.Ordinal),
                "164-38A-2: duplicate cursor responses use cached UTF-8 bytes");
            Check(source.Contains("private static readonly byte[] AcceptedCursorResponseBytes", StringComparison.Ordinal)
                  && handle.Contains("TryWrite(context, 202, AcceptedCursorResponseBytes)", StringComparison.Ordinal),
                "164-38A-3: accepted cursor responses keep cached UTF-8 bytes");
        }

        private static void VerifyCursorEndpointAvoidsCorsTrimAllocationsOnAllowCheck()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");
            var cors = PhaseValidationSourceHelpers.SourceMethod(source, "private bool IsCorsOriginAllowed(string origin)");
            var bounds = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool TryGetOriginBounds");

            Check(cors.Contains("TryGetOriginBounds(origin, out var start, out var length)", StringComparison.Ordinal)
                  && cors.Contains("string.Compare(origin, start, allowedOrigin, 0, length, StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal)
                  && !cors.Contains("origin.Trim().TrimEnd", StringComparison.Ordinal),
                "164-38B-1: CORS allow checks compare origin spans without allocating trimmed strings");
            Check(bounds.Contains("while (start <= end && char.IsWhiteSpace(origin[start]))", StringComparison.Ordinal)
                  && bounds.Contains("while (end >= start && origin[end] == '/')", StringComparison.Ordinal),
                "164-38B-2: CORS origin bounds trim whitespace and trailing slashes by index");
        }

        private static void VerifyExternalCursorDrainAvoidsIdleLock()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ExternalReplayCursorController.cs");
            var drain = PhaseValidationSourceHelpers.SourceMethod(source, "public bool TryDrainLatest(out ReplayCursorRequest request)");

            Check(source.Contains("private int _hasPendingFast;", StringComparison.Ordinal)
                  && drain.Contains("Volatile.Read(ref _hasPendingFast) == 0", StringComparison.Ordinal)
                  && drain.IndexOf("Volatile.Read(ref _hasPendingFast) == 0", StringComparison.Ordinal)
                     < drain.IndexOf("lock (_gate)", StringComparison.Ordinal),
                "164-38C-1: external cursor drain returns before locking when idle");
            Check(source.Contains("Volatile.Write(ref _hasPendingFast, 1)", StringComparison.Ordinal)
                  && CountOccurrences(source, "Volatile.Write(ref _hasPendingFast, 0)") >= 2,
                "164-38C-2: external cursor pending fast flag is published on enqueue, drain, and clear");
        }

        private static void VerifyExtensionAvoidsPerSendAndPerFollowAllocations()
        {
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var sendCursor = SourceFunction(source, "async function sendCursor");
            var buildFollowPayload = SourceFunction(source, "function buildFollowPayload");
            var maxHzHandler = Slice(source, "panel.maxHzInput.addEventListener", "panel.followInput?.addEventListener");
            var renderTail = Slice(source, "context.onRender = (renderState, done) =>", "return () =>");

            Check(source.Contains("const NO_TOKEN_HEADERS", StringComparison.Ordinal)
                  && sendCursor.Contains(": NO_TOKEN_HEADERS", StringComparison.Ordinal)
                  && !sendCursor.Contains("const headers: Record<string, string> = { \"Content-Type\": \"application/json\" };", StringComparison.Ordinal),
                "164-38D-1: extension sendCursor reuses no-token headers");
            Check(buildFollowPayload.Contains("startTime: lastStartTime", StringComparison.Ordinal)
                  && buildFollowPayload.Contains("endTime: lastEndTime", StringComparison.Ordinal)
                  && !buildFollowPayload.Contains("{ ...lastStartTime }", StringComparison.Ordinal)
                  && !buildFollowPayload.Contains("{ ...lastEndTime }", StringComparison.Ordinal),
                "164-38D-2: follow payload reuses cached timeline bounds without object spreads");
            Check(source.Contains("let minIntervalMs = 1000 / state.maxHz", StringComparison.Ordinal)
                  && maxHzHandler.Contains("minIntervalMs = 1000 / state.maxHz", StringComparison.Ordinal)
                  && !renderTail.Contains("const minIntervalMs = 1000 / state.maxHz", StringComparison.Ordinal),
                "164-38D-3: extension caches cursor interval outside render frames");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-38\"", StringComparison.Ordinal), "164-38E-1: validation registry exposes Phase164-38");
            Check(project.Contains("Phase164_38Validation.cs", StringComparison.Ordinal), "164-38E-2: runtime validation project compiles Phase164-38");
        }

        private static string SourceFunction(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing function: " + signature);

            var next = source.IndexOf("\nfunction ", start + signature.Length, StringComparison.Ordinal);
            var nextExport = source.IndexOf("\nexport function ", start + signature.Length, StringComparison.Ordinal);
            if (next < 0 || (nextExport >= 0 && nextExport < next))
                next = nextExport;
            return next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        }

        private static string Slice(string source, string startToken, string endToken)
        {
            var start = source.IndexOf(startToken, StringComparison.Ordinal);
            if (start < 0)
                throw new Exception("[FAIL] missing start token: " + startToken);
            var end = source.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
                throw new Exception("[FAIL] missing end token: " + endToken);
            return source.Substring(start, end - start);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var offset = 0;
            while (true)
            {
                var index = text.IndexOf(value, offset, StringComparison.Ordinal);
                if (index < 0)
                    return count;
                count++;
                offset = index + value.Length;
            }
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
