// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 116 validation for the local MCAP DataLoader facade.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase116Validation.
    /// </summary>
    public static partial class Phase116Validation
    {
        private const string CurrentHash = "1111111111111111111111111111111111111111111111111111111111111111";
        private const string MismatchedHash = "2222222222222222222222222222222222222222222222222222222222222222";
        private const string CurrentFoxRunHash = "3333333333333333333333333333333333333333333333333333333333333333";
        private const string MismatchedFoxRunHash = "4444444444444444444444444444444444444444444444444444444444444444";
        private static readonly Dictionary<string, MethodInfo> MethodCache = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        private static readonly Dictionary<string, MemberInfo> MemberCache = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
        private static int _passed;
        private static string _repoRoot;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 116: Local MCAP DataLoader v1 ===");
            _passed = 0;
            MethodCache.Clear();
            MemberCache.Clear();

            VerifyPublicApiShape();
            VerifyInitializationAndIteratorQueries();
            VerifyBackfillSemantics();
            VerifySchemaGovernanceDiagnostics();
            VerifyReaderBoundaries();
            VerifyValidationWiringAndDocs();

            Console.WriteLine($"Phase 116: {_passed} checks passed.");
        }

        private static void VerifyPublicApiShape()
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            var init = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderInitialization");
            var channel = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderChannel");
            var schema = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderSchema");
            var problem = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderProblem");
            var timeRange = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderTimeRange");
            var message = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderMessage");
            var query = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            var backfill = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");

            Check(typeof(IDisposable).IsAssignableFrom(loader), "116-A1: McapDataLoader implements IDisposable");
            Check(loader.GetConstructor(new[] { typeof(string) }) != null,
                "116-A2: McapDataLoader exposes path constructor");
            Check(loader.GetConstructor(new[] { typeof(Stream), typeof(bool) }) != null,
                "116-A3: McapDataLoader exposes Stream/leaveOpen constructor");
            Check(loader.GetMethod("Initialize", Type.EmptyTypes)?.ReturnType == init,
                "116-A4: Initialize returns initialization DTO");
            Check(loader.GetMethod("CreateIterator", new[] { query })?.ReturnType == typeof(IEnumerable<>).MakeGenericType(message),
                "116-A5: CreateIterator returns typed message enumerable");
            Check(loader.GetMethod("GetBackfill", new[] { backfill })?.ReturnType == typeof(IReadOnlyList<>).MakeGenericType(message),
                "116-A6: GetBackfill returns typed message list");

            foreach (var dto in new[] { init, channel, schema, problem, timeRange, message, query, backfill })
            {
                Check(!TypeReferencesUnityEngine(dto),
                    "116-A7: DataLoader DTO avoids UnityEngine dependency: " + dto.Name);
            }
        }

        private static void VerifyInitializationAndIteratorQueries()
        {
            using var ms = CreateFixture();
            using var loader = (IDisposable)CreateLoader(ms);

            var first = Invoke(loader, "Initialize");
            var second = Invoke(loader, "Initialize");
            Check(!object.ReferenceEquals(first, second), "116-B1: Initialize returns independent cached snapshots");

            Check(Count(Member(first, "Channels")) == 3, "116-B2: initialization exposes three channels");
            Check(Count(Member(first, "Schemas")) == 3, "116-B3: initialization exposes three schemas");
            Check(Count(Member(first, "MetadataIndexes")) == 1, "116-B4: initialization exposes metadata index summaries");
            Check(Count(Member(first, "AttachmentIndexes")) == 1, "116-B5: initialization exposes attachment index summaries");
            Check(BoolMember(Member(first, "TimeRange"), "HasRange")
                  && ULongMember(Member(first, "TimeRange"), "StartTimeNs") == 10
                  && ULongMember(Member(first, "TimeRange"), "EndTimeNs") == 80,
                "116-B6: initialization exposes inclusive message time range");
            Check(BoolMember(first, "HasTotalMessageCount")
                  && ULongMember(first, "TotalMessageCount") == 9,
                "116-B7: initialization exposes message count when statistics are available");

            var topicQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(topicQuery, "Topics", new List<string> { "/phase116/a" });
            CheckTimes(InvokeMessages(loader, "CreateIterator", topicQuery), new ulong[] { 10, 30, 50 },
                "116-C1: iterator filters by topic");

            var channelQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(channelQuery, "ChannelIds", new List<ushort> { 2 });
            CheckTimes(InvokeMessages(loader, "CreateIterator", channelQuery), new ulong[] { 20, 40, 60 },
                "116-C2: iterator filters by channel ID");

            var rangeQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(rangeQuery, "StartTimeNs", 30UL);
            Set(rangeQuery, "EndTimeNs", 60UL);
            CheckTimes(InvokeMessages(loader, "CreateIterator", rangeQuery), new ulong[] { 30, 40, 50, 50, 60 },
                "116-C3: iterator filters by inclusive time range");

            var latestQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery");
            Set(latestQuery, "MaxMessages", 2);
            CheckTimes(InvokeMessages(loader, "CreateIterator", latestQuery), new ulong[] { 80, 80 },
                "116-C4: iterator MaxMessages keeps latest matches like McapIndexedReader");
        }

        private static void VerifyBackfillSemantics()
        {
            using var ms = CreateFixture();
            using var loader = (IDisposable)CreateLoader(ms);
            Invoke(loader, "Initialize");

            var query = New("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");
            Set(query, "TimeNs", 55UL);
            Set(query, "ChannelIds", new List<ushort> { 1, 2, 3 });
            var backfill = InvokeMessages(loader, "GetBackfill", query);
            Check(ChannelTimes(backfill).SequenceEqual(new[] { "1:50", "2:40", "3:50" }),
                "116-D1: backfill returns one latest message per requested channel");

            var tiedQuery = New("Unity.FoxgloveSDK.IO.McapDataLoaderBackfillQuery");
            Set(tiedQuery, "TimeNs", 80UL);
            Set(tiedQuery, "Topics", new List<string> { "/phase116/tie" });
            var tied = InvokeMessages(loader, "GetBackfill", tiedQuery);
            Check(tied.Count == 1
                  && UShortMember(tied[0], "ChannelId") == 3
                  && ULongMember(tied[0], "LogTime") == 80
                  && UIntMember(tied[0], "Sequence") == 2,
                "116-D2: same-channel same-time backfill tie uses deterministic sequence/publish ordering");
        }

        private static void VerifySchemaGovernanceDiagnostics()
        {
            FoxRunSchemaInfoRegistry.ClearForTests();
            FoxRunSchemaInfoRegistry.RegisterGenerated(CreateSchemaInfo(CurrentHash));
            try
            {
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(CurrentHash), out var matchingJson);
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(MismatchedHash), out var mismatchedJson);

                using (var matching = CreateFixture(matchingJson))
                using (var loader = (IDisposable)CreateLoader(matching))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMatch"),
                        "116-E1: matching FoxRun metadata is surfaced as an info diagnostic");
                }

                FoxRunSchemaMcapMetadata.TryCreateJson(
                    CreateSchemaInfo(CurrentHash, MismatchedFoxRunHash),
                    out var sameGlobalDifferentFoxRunJson);
                using (var sameGlobalDifferentFoxRun = CreateFixture(sameGlobalDifferentFoxRunJson))
                using (var loader = (IDisposable)CreateLoader(sameGlobalDifferentFoxRun))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMatch"),
                        "116-E1b: schema governance compares global hash rather than FoxRun section hash");
                }

                using (var missing = CreateFixture(includeFoxRunMetadata: false))
                using (var loader = (IDisposable)CreateLoader(missing))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMissing"),
                        "116-E2: missing FoxRun metadata initializes with a diagnostic");
                }

                using (var malformed = CreateFixture("{not-json}"))
                using (var loader = (IDisposable)CreateLoader(malformed))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p => StringMember(p, "Code") == "FoxRunSchemaMetadataMalformed"),
                        "116-E3: malformed FoxRun metadata initializes with a warning diagnostic");
                }

                using (var mismatch = CreateFixture(mismatchedJson))
                using (var loader = (IDisposable)CreateLoader(mismatch))
                {
                    var problems = Problems(Invoke(loader, "Initialize"));
                    Check(problems.Any(p =>
                              StringMember(p, "Code") == "FoxRunSchemaMetadataMismatch"
                              && StringMember(p, "Severity") == "Error"),
                        "116-E4: confirmed FoxRun mismatch is diagnostic-only but high severity");
                    Check(InvokeMessages(loader, "CreateIterator", New("Unity.FoxgloveSDK.IO.McapDataLoaderQuery")).Count > 0,
                        "116-E5: DataLoader continues raw local iteration after FoxRun mismatch");
                }
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        private static void VerifyReaderBoundaries()
        {
            Check(Throws<NotSupportedException>(() => CreateLoader(new NonSeekableStream(CreateFixture()))),
                "116-F1: DataLoader stream constructor preserves seekable-stream boundary");

            var invalidPath = TempInvalidMcap();
            try
            {
                Check(Throws<EndOfStreamException>(() => CreatePathLoader(invalidPath)),
                    "116-F2: truncated MCAP fails with EndOfStreamException through existing reader behavior");
            }
            finally
            {
                TryDelete(invalidPath);
            }
        }

        private static void VerifyValidationWiringAndDocs()
        {
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(validationRegistry.Contains("--phase116", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase116Validation.Validate", StringComparison.Ordinal),
                "116-G1: validation registry wires --phase116");
            Check(project.Contains("Phase116Validation.cs", StringComparison.Ordinal),
                "116-G2: runtime test project compiles Phase116Validation");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md")
                    .Contains("McapDataLoader", StringComparison.Ordinal),
                "116-G3: English MCAP docs describe local DataLoader v1");
            Check(ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/08_MCAP录制回放.md")
                    .Contains("McapDataLoader", StringComparison.Ordinal),
                "116-G4: Chinese MCAP docs describe local DataLoader v1");
        }

        private static MemoryStream CreateFixture(string foxRunMetadataJson = null, bool includeFoxRunMetadata = true)
        {
            if (includeFoxRunMetadata && foxRunMetadataJson == null)
                FoxRunSchemaMcapMetadata.TryCreateJson(CreateSchemaInfo(CurrentHash), out foxRunMetadataJson);

            var ms = new MemoryStream();
            using (var recorder = new McapRecorder(ms, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase116/a", "json", "phase116.A", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(2, "/phase116/b", "json", "phase116.B", "jsonschema", "{\"type\":\"object\"}");
                recorder.AddChannel(3, "/phase116/tie", "json", "phase116.Tie", "jsonschema", "{\"type\":\"object\"}");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{\"a\":10}"));
                recorder.WriteMessage(2, 20, Encoding.UTF8.GetBytes("{\"b\":20}"));
                recorder.WriteMessage(1, 30, Encoding.UTF8.GetBytes("{\"a\":30}"));
                recorder.WriteMessage(2, 40, Encoding.UTF8.GetBytes("{\"b\":40}"));
                recorder.WriteMessage(1, 50, Encoding.UTF8.GetBytes("{\"a\":50}"));
                recorder.WriteMessage(3, 50, Encoding.UTF8.GetBytes("{\"tie\":50}"));
                recorder.WriteMessage(2, 60, Encoding.UTF8.GetBytes("{\"b\":60}"));
                recorder.WriteMessage(3, 80, Encoding.UTF8.GetBytes("{\"tie\":\"80a\"}"));
                recorder.WriteMessage(3, 80, Encoding.UTF8.GetBytes("{\"tie\":\"80b\"}"));
                recorder.AddAttachment("phase116.txt", "text/plain", Encoding.UTF8.GetBytes("phase116"), 75);
                if (includeFoxRunMetadata)
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, foxRunMetadataJson);
                recorder.Close();
            }

            ms.Position = 0;
            return ms;
        }

        private static FoxRunSchemaManifestInfo CreateSchemaInfo(
            string globalHash,
            string foxRunHash = CurrentFoxRunHash)
            => new FoxRunSchemaManifestInfo(
                1,
                "dev.unity2foxglove.sdk",
                "Phase116Fixture",
                1,
                globalHash,
                foxRunHash,
                new List<FoxRunSchemaTypeInfo>());

        private static object CreateLoader(Stream stream)
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            return Activator.CreateInstance(loader, stream, true);
        }

        private static object CreatePathLoader(string path)
        {
            var loader = RequiredType("Unity.FoxgloveSDK.IO.McapDataLoader");
            return Activator.CreateInstance(loader, path);
        }

        private static string TempInvalidMcap()
        {
            var path = Path.Combine(Path.GetTempPath(), "phase116-invalid-" + Guid.NewGuid().ToString("N") + ".mcap");
            File.WriteAllText(path, "not an mcap");
            return path;
        }

        private static Type RequiredType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name, throwOnError: false))
                .FirstOrDefault(candidate => candidate != null);
            Check(type != null, "116-type-exists: required type exists in loaded assemblies: " + name);
            return type;
        }

        private static object New(string name)
            => Activator.CreateInstance(RequiredType(name));

        private static object Invoke(object target, string method, params object[] args)
        {
            var type = target.GetType();
            var cacheKey = MethodKey(type, method, args);
            if (!MethodCache.TryGetValue(cacheKey, out var methodInfo))
            {
                methodInfo = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(candidate => candidate.Name == method && candidate.GetParameters().Length == args.Length)
                    .SingleOrDefault(candidate => ParametersAssignable(candidate.GetParameters(), args));
                if (methodInfo != null)
                    MethodCache[cacheKey] = methodInfo;
            }

            if (methodInfo == null)
                throw new MissingMethodException(type.FullName, method + "(" + args.Length + " args)");
            return methodInfo.Invoke(target, args);
        }

        private static string MethodKey(Type type, string method, object[] args)
        {
            var key = new StringBuilder(type.AssemblyQualifiedName)
                .Append('|')
                .Append(method)
                .Append('|')
                .Append(args.Length);
            foreach (var arg in args)
            {
                key.Append('|');
                key.Append(arg?.GetType().AssemblyQualifiedName ?? "<null>");
            }

            return key.ToString();
        }
        private static object Member(object target, string name)
        {
            var type = target.GetType();
            var cacheKey = type.AssemblyQualifiedName + "|" + name;
            if (!MemberCache.TryGetValue(cacheKey, out var member))
            {
                member = (MemberInfo)type.GetField(name) ?? type.GetProperty(name);
                if (member != null)
                    MemberCache[cacheKey] = member;
            }

            if (member is FieldInfo field)
                return field.GetValue(target);
            if (member is PropertyInfo property)
                return property.GetValue(target);

            throw new MissingMemberException(type.FullName, name);
        }
}
}
