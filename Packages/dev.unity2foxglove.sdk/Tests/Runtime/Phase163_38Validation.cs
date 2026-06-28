// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-38 Foxglove extension cursor bridge review closure.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_38Validation
    {
        public static void Validate()
        {
            var repoRoot = Phase16Validation.FindRepoRoot()
                           ?? throw new DirectoryNotFoundException("Could not locate repository root.");

            VerifyCursorEndpoint(repoRoot);
            VerifyTickCoordinator(repoRoot);
            VerifyExtensionTooling(repoRoot);
            VerifyWiring(repoRoot);

            Console.WriteLine("Phase 163-38: Foxglove cursor bridge checks passed.");
        }

        private static void VerifyCursorEndpoint(string repoRoot)
        {
            var source = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");

            Check(source.Contains("private volatile HttpListener _listener;", StringComparison.Ordinal),
                "163-38A-1: cursor endpoint listener field is volatile for cross-thread shutdown");
            Check(source.Contains("Access-Control-Allow-Private-Network", StringComparison.Ordinal),
                "163-38A-2: cursor endpoint emits Private Network Access CORS header");
            Check(source.Contains("new byte[Math.Min(_options.MaxBodyBytes + 1, 4096)]", StringComparison.Ordinal)
                  && source.Contains("total > _options.MaxBodyBytes", StringComparison.Ordinal)
                  && !source.Contains("new char[_options.MaxBodyBytes + 1]", StringComparison.Ordinal),
                "163-38A-3: cursor endpoint enforces request body limit in bytes");
            Check(source.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal)
                  < source.IndexOf("IsAuthorized(context.Request)", StringComparison.Ordinal),
                "163-38A-4: browser OPTIONS preflight is answered before bearer authorization");
            Check(source.Contains("TryWrite(context, 401", StringComparison.Ordinal)
                  && source.Contains("IsCorsOriginAllowed(origin)", StringComparison.Ordinal),
                "163-38A-5: TryWrite keeps CORS headers available for early error responses");
        }

        private static void VerifyTickCoordinator(string repoRoot)
        {
            var source = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");

            Check(source.Contains("replay.Play();", StringComparison.Ordinal)
                  && source.Contains("finally", StringComparison.Ordinal)
                  && source.Contains("replay.Pause();", StringComparison.Ordinal),
                "163-38B-1: external cursor forward advance pauses replay in a finally block");
            Check(source.Contains("private bool TryConsumeReplaySceneSnapshot(out ulong timeNs)", StringComparison.Ordinal)
                  && !source.Contains("TryConsumeReplaySceneSnapshot(out ulong timeNs, IFoxgloveClock wallClock)", StringComparison.Ordinal),
                "163-38B-2: scene snapshot consumption no longer carries an unused wall-clock parameter");
        }

        private static void VerifyExtensionTooling(string repoRoot)
        {
            var packageJson = Read(repoRoot, "Tools/foxglove-extensions/unity-cursor-bridge/package.json");
            var mainConfig = Read(repoRoot, "Tools/foxglove-extensions/unity-cursor-bridge/tsconfig.json");
            var testConfig = Read(repoRoot, "Tools/foxglove-extensions/unity-cursor-bridge/tsconfig.test.json");
            var readme = Read(repoRoot, "Tools/foxglove-extensions/unity-cursor-bridge/README.md");

            Check(packageJson.Contains("tsc --noEmit -p tsconfig.json && tsc --noEmit -p tsconfig.test.json", StringComparison.Ordinal)
                  && mainConfig.Contains("../../../build/foxglove-extensions/unity-cursor-bridge/tsconfig.tsbuildinfo", StringComparison.Ordinal)
                  && testConfig.Contains("../../../build/foxglove-extensions/unity-cursor-bridge/tsconfig.test.tsbuildinfo", StringComparison.Ordinal),
                "163-38C-1: extension package exposes product and test TypeScript typecheck");
            Check(testConfig.Contains("\"include\": [\"src/**/*.ts\"]", StringComparison.Ordinal)
                  && testConfig.Contains("\"exclude\": []", StringComparison.Ordinal),
                "163-38C-2: extension test tsconfig includes test sources");
            Check(readme.Contains("npm run typecheck", StringComparison.Ordinal)
                  && readme.Contains("Bearer-token authentication", StringComparison.Ordinal)
                  && readme.Contains("Foxglove Desktop", StringComparison.Ordinal)
                  && readme.Contains("actual cursor request", StringComparison.Ordinal),
                "163-38C-3: README documents typecheck and browser/token preflight boundary");
        }

        private static void VerifyWiring(string repoRoot)
        {
            var project = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_38Validation.cs", StringComparison.Ordinal),
                "163-38D-1: runtime test project compiles Phase163_38Validation");
            Check(registry.Contains("Ci(\"--phase163-38\", \"Phase 163-38\", Phase163_38Validation.Validate", StringComparison.Ordinal),
                "163-38D-2: validation registry exposes --phase163-38");
        }

        private static string Read(string repoRoot, string relativePath)
            => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string description)
        {
            if (!condition)
                throw new Exception("[FAIL] " + description);

            Console.WriteLine("[PASS] " + description);
        }
    }
}
