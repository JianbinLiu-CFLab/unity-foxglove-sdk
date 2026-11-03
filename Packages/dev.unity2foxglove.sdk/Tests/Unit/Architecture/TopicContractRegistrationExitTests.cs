// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 193 production channel-registration exit allowlist.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Architecture
{
    [Trait("Phase", "193")]
    [Trait("Domain", "Architecture")]
    public sealed class TopicContractRegistrationExitTests
    {
        private const string RegistrationCall =
            @"[A-Za-z0-9_\)\]]\s*\.\s*(?:RegisterSchemaChannel|RegisterChannel|RegisterRecordingOnlyChannel)\s*\(";
        private const string StringsAndComments =
            @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\\r\n])*""|'(?:\\.|[^'\\\r\n])+'|//[^\r\n]*|/\*.*?\*/";

        [Fact]
        public void ProductionRegistrationExitsMatchAllowlist()
        {
            var repoRoot = RepoRoot.Value;
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(Path.Combine(repoRoot, "Packages"), "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Tests/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var source = Regex.Replace(File.ReadAllText(file), StringsAndComments, " ", RegexOptions.Singleline);
                if (Regex.IsMatch(source, RegistrationCall))
                    actual.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
            }

            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.MessagePack.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.Publishing.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/Decomposed/FoxgloveSession/FoxgloveSession.TransportEvents.cs",
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeMcapCodecs.cs",
                "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/RemoteGatewayMirrorSink.cs"
            };

            Assert.Equal(expected.OrderBy(path => path), actual.OrderBy(path => path));
        }

        [Theory]
        [InlineData("var u = \"ws://h\"; rt.RegisterChannel(c);", true)]
        [InlineData("session\n    .RegisterRecordingOnlyChannel(c);", true)]
        [InlineData("// rt.RegisterChannel(c);", false)]
        [InlineData("/* rt.RegisterChannel(c); */", false)]
        [InlineData("var s = \"rt.RegisterChannel(c)\";", false)]
        [InlineData("var s = @\"a \"\" rt.RegisterChannel(c)\";", false)]
        [InlineData("// comment text.\nRegisterChannel(c);", false)]
        public void RegistrationScanIgnoresStringsAndComments(string source, bool expected)
        {
            var stripped = Regex.Replace(source, StringsAndComments, " ", RegexOptions.Singleline);

            Assert.Equal(expected, Regex.IsMatch(stripped, RegistrationCall));
        }

        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRoot);

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Unity2Foxglove"))
                    && File.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
