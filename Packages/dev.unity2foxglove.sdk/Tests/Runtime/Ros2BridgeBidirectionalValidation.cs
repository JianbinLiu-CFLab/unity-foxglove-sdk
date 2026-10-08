// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase186 structural gate for the frozen Bridge authority and portable loop primitive.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Ros2BridgeBidirectionalValidation
    {
        private const string ProtocolFixture =
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/u2r2_protocol_vectors.json";
        private const string PreMoveFixture =
            "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Fixtures/pre_move_bridge_and_mcap_vectors.json";
        private const string InventoryFixture =
            "Packages/dev.unity2foxglove.sdk/Tests/Unit/Phase186/Fixtures/pre_move_sdk_ros_inventory.json";
        private const string Provenance =
            "Tools/ros2_bridge/unity2foxglove_ros2_bridge/PROVENANCE.json";
        private const string CanonicalType =
            "unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope";
        private const string InterfaceDigest =
            "120864853239fae290b5199cd02dbf02f107299bccd8972b06d8cf59fc7594fd";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 186: ROS-free core and bidirectional ROS2 Bridge ===");
            _passed = 0;

            VerifyProtocolAuthority();
            VerifyPreMoveBehaviorAuthority();
            VerifyProvenanceAuthority();
            VerifyPortableOriginProbe();
            VerifyProviderGenerationBoundary();
            VerifyGeneratedStandardCdrBehavior();
            VerifyPhysicalLeaseSharingBehavior();
            VerifyGeneratedDuplexProbeAuthority();
            VerifySplitPythonSourceUsesDeclaredExports();

            Console.WriteLine($"Phase 186: {_passed} checks passed.");
        }

        private static void VerifySplitPythonSourceUsesDeclaredExports()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "unity2foxglove-phase186-source-layout-" + Guid.NewGuid().ToString("N"));
            var package = Path.Combine(root, "fixture");
            Directory.CreateDirectory(package);
            var facade = Path.Combine(root, "fixture.py");
            try
            {
                File.WriteAllText(facade, "LIVE_AUTHORITY\n");
                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live\n");
                File.WriteAllText(Path.Combine(package, "live.py"), "LIVE_SECTION\n");
                File.WriteAllText(Path.Combine(package, "other.py"), "OTHER_SECTION\n");
                File.WriteAllText(Path.Combine(package, "obsolete.py"), "ORPHAN_SECTION\n");

                var source = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    source.Contains("LIVE_SECTION", StringComparison.Ordinal)
                    && !source.Contains("ORPHAN_SECTION", StringComparison.Ordinal),
                    "186-F7: split-source validation follows __init__.py exports and ignores orphan sections");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import (\n    live,\n)\n");
                var multilineSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    multilineSource.Contains("LIVE_SECTION", StringComparison.Ordinal),
                    "186-F8: split-source validation accepts multiline __init__.py imports");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import \\\n    live\n");
                var continuedSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    continuedSource.Contains("LIVE_SECTION", StringComparison.Ordinal),
                    "186-F9: split-source validation accepts backslash-continued imports");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live # comment \\\nfrom . import other\n");
                var commentSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    commentSource.Contains("LIVE_SECTION", StringComparison.Ordinal)
                    && commentSource.Contains("OTHER_SECTION", StringComparison.Ordinal),
                    "186-F10: split-source validation ignores backslashes inside comments");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "value = \\\n    1\nfrom . import live\n");
                var unrelatedContinuationSource =
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    unrelatedContinuationSource.Contains("LIVE_SECTION", StringComparison.Ordinal),
                    "186-F11: split-source validation ignores non-import continuations");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live\nfrom . import live\n");
                var duplicateImportRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    duplicateImportRejected = true;
                }
                Check(
                    duplicateImportRejected,
                    "186-F12: split-source validation rejects repeated imports");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live\nfrom . import missing\n");
                var missingSectionRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (FileNotFoundException)
                {
                    missingSectionRejected = true;
                }
                Check(
                    missingSectionRejected,
                    "186-F13: split-source validation rejects missing declared sections");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live; from . import other\n");
                var semicolonSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    semicolonSource.Contains("LIVE_SECTION", StringComparison.Ordinal)
                    && semicolonSource.Contains("OTHER_SECTION", StringComparison.Ordinal),
                    "186-F16: split-source validation accepts same-line section imports");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import ../outside\n");
                var invalidSectionRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    invalidSectionRejected = true;
                }
                Check(
                    invalidSectionRejected,
                    "186-F17: split-source validation rejects invalid section names");

                File.WriteAllText(Path.Combine(package, "class.py"), "CLASS_SECTION\n");
                File.WriteAllText(Path.Combine(package, "café.py"), "UNICODE_SECTION\n");
                File.WriteAllText(Path.Combine(package, "case.py"), "CASE_SECTION\n");
                File.WriteAllText(Path.Combine(package, "match.py"), "MATCH_SECTION\n");
                File.WriteAllText(Path.Combine(package, "type.py"), "TYPE_SECTION\n");
                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import class\n");
                var keywordSectionRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    keywordSectionRejected = true;
                }
                Check(
                    keywordSectionRejected,
                    "186-F21: split-source validation rejects keyword module names");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live as class\n");
                var keywordAliasRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    keywordAliasRejected = true;
                }
                Check(
                    keywordAliasRejected,
                    "186-F25: split-source validation rejects keyword aliases");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import café\n");
                var unicodeSectionRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    unicodeSectionRejected = true;
                }
                Check(
                    unicodeSectionRejected,
                    "186-F22: split-source validation rejects non-ASCII module names");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import match\n");
                var softKeywordSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    softKeywordSource.Contains("MATCH_SECTION", StringComparison.Ordinal),
                    "186-F23: split-source validation matches Python soft-keyword behavior");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import case\nfrom . import type\n");
                var additionalSoftKeywordSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    additionalSoftKeywordSource.Contains("CASE_SECTION", StringComparison.Ordinal) &&
                    additionalSoftKeywordSource.Contains("TYPE_SECTION", StringComparison.Ordinal),
                    "186-F24: split-source validation accepts every Python soft-keyword module name");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live as\n");
                var malformedAliasRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    malformedAliasRejected = true;
                }
                Check(
                    malformedAliasRejected,
                    "186-F18: split-source validation rejects malformed aliases");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "if TYPE_CHECKING:\n    from . import missing\nfrom . import live\n");
                var nestedImportSource =
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    nestedImportSource.Contains("LIVE_SECTION", StringComparison.Ordinal)
                    && !nestedImportSource.Contains("ORPHAN_SECTION", StringComparison.Ordinal),
                    "186-F19: split-source validation ignores nested imports");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "from . import live\ntext = \"unterminated\n");
                var unterminatedStringRejected = false;
                try
                {
                    PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                }
                catch (InvalidOperationException)
                {
                    unterminatedStringRejected = true;
                }
                Check(
                    unterminatedStringRejected,
                    "186-F20: split-source validation rejects unterminated strings");
                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "\"\"\"\nfrom . import missing\n\"\"\"\nfrom . import live\n");
                var docstringSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    docstringSource.Contains("LIVE_SECTION", StringComparison.Ordinal),
                    "186-F14: split-source validation ignores imports inside docstrings");

                File.WriteAllText(
                    Path.Combine(package, "__init__.py"),
                    "text = \"from . import missing\"\nfrom . import live\n");
                var stringSource = PhaseValidationSourceHelpers.ReadSplitPythonSourcePath(facade);
                Check(
                    stringSource.Contains("LIVE_SECTION", StringComparison.Ordinal),
                    "186-F15: split-source validation ignores imports inside strings");
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static void VerifyProtocolAuthority()
        {
            var fixture = LoadJson(ProtocolFixture);
            var limits = (JObject)fixture["limits"];
            var negativeVectors = (JArray)fixture["negativeVectors"];
            var executableV2Negatives =
                (JArray)fixture["v2"]?["negativeVectors"];
            var executableV1Authority =
                (JObject)fixture["v2"]?["legacyV1NegativeExecution"];
            var executableV1Negatives =
                (JArray)executableV1Authority?["vectors"];

            Check(
                (int?)fixture["fixtureVersion"] == 1
                && (string)fixture["protocol"] == "U2R2"
                && (int?)limits["fixedHeaderBytes"] == 16
                && (int?)limits["maxJsonHeaderBytes"] == 65_536
                && (int?)limits["maxPayloadBytes"] == 67_108_864
                && (int?)limits["defaultQueueCapacityFrames"] == 1_024
                && (long?)limits["maxQueuedPayloadBytes"] == 68_719_476_736L
                && (int?)limits["activeConnectionCount"] == 1
                && (int?)limits["partialFrameStallMs"] == 5_000,
                "186-A1: shared U2R2 v1 limits remain explicit and bounded");

            Check(
                (string)fixture["health"]?["request"]?["header"]?["op"] == "health_ping"
                && (string)fixture["health"]?["response"]?["header"]?["op"] == "health_pong"
                && (string)fixture["preparePublisher"]?["request"]?["header"]?["op"] == "prepare_publisher"
                && (string)fixture["preparePublisher"]?["response"]?["header"]?["op"] == "publisher_ready"
                && (string)fixture["publish"]?["frame"]?["header"]?["op"] == "publish",
                "186-A2: shared health, preparation, and publish operations are frozen");

            var negativeIds = negativeVectors
                .Select(vector => (string)vector["id"])
                .ToArray();
            Check(
                negativeVectors.Count == 19
                && negativeIds.All(id => !string.IsNullOrWhiteSpace(id))
                && negativeIds.Distinct(StringComparer.Ordinal).Count()
                    == negativeIds.Length
                && negativeVectors.All(
                    vector => (string)vector["expected"] == "reject")
                && (int?)executableV1Authority?["schemaVersion"] == 1
                && (string)executableV1Authority?["catalog"]
                    == "negativeVectors"
                && executableV1Negatives != null
                && executableV1Negatives.Count == negativeVectors.Count
                && executableV1Negatives
                    .Select(vector => (string)vector["id"])
                    .SequenceEqual(negativeIds, StringComparer.Ordinal)
                && executableV1Negatives.All(
                    vector => !string.IsNullOrWhiteSpace(
                                  (string)vector["action"])
                              && !string.IsNullOrWhiteSpace(
                                  (string)vector["expectedFailure"])
                              && vector["consumers"] is JArray consumers
                              && consumers.Count > 0
                              && consumers.All(
                                  consumer => (string)consumer == "csharp"
                                              || (string)consumer == "cpp"))
                && executableV2Negatives != null
                && executableV2Negatives.Count == 51
                && executableV2Negatives.All(
                    vector => !string.IsNullOrWhiteSpace(
                                  (string)vector["action"])
                              && !string.IsNullOrWhiteSpace(
                                  (string)vector["expectedErrorCode"])
                              && vector["terminal"]?.Type
                                  == JTokenType.Boolean),
                "186-A3: all 19 frozen v1 negative IDs bind to versioned role-aware executable actions, while all 51 v2 negatives carry cross-language actions and exact errors");
        }
    }
}
