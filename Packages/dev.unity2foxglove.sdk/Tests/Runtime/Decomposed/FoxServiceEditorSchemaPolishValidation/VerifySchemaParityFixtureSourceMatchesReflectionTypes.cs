// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxService schema payload and Unity Editor polish contracts.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class FoxServiceEditorSchemaPolishValidation
    {
        private static void VerifySchemaParityFixtureSourceMatchesReflectionTypes()
        {
            var source = SchemaParityFixtureSource();
            Check(SourceClassContainsPublicProperty(source, "SchemaParityRequest", "int", "UserId")
                  && SourceClassContainsPublicProperty(source, "SchemaParityRequest", "Mode", "Mode")
                  && SourceClassContainsPublicProperty(source, "SchemaParityRequest", "string", "ReadOnlyScalar")
                  && SourceClassContainsPublicProperty(source, "SchemaParityResponse", "string", "StatusText")
                  && SourceClassContainsPublicProperty(source, "SchemaParityResponse", "Mode", "Mode"),
                "141E-7g: fixture source schema DTO shape matches reflection parity anchors");
            Check(typeof(SchemaParityRequest).GetProperty("UserId") != null
                  && typeof(SchemaParityRequest).GetProperty("Mode") != null
                  && typeof(SchemaParityRequest).GetProperty("ReadOnlyScalar") != null
                  && typeof(SchemaParityResponse).GetProperty("StatusText") != null
                  && typeof(SchemaParityResponse).GetProperty("Mode") != null,
                "141E-7h: reflection parity DTO shape is explicitly anchored");
        }

        private static bool SourceClassContainsPublicProperty(string source, string className, string typeName, string propertyName)
        {
            var classIndex = source.IndexOf("class " + className, StringComparison.Ordinal);
            if (classIndex < 0)
                return false;

            var nextClassIndex = source.IndexOf("class ", classIndex + 1, StringComparison.Ordinal);
            var endIndex = nextClassIndex >= 0 ? nextClassIndex : source.Length;
            var classBody = source.Substring(classIndex, endIndex - classIndex);
            return classBody.Contains("public " + typeName + " " + propertyName, StringComparison.Ordinal);
        }

        private static MetadataReference[] References()
            => CachedReferences.Value;

        private static MetadataReference[] CreateReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141E Roslyn reference resolution.");

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(FoxServiceAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(JToken).Assembly.Location)
                })
                .ToArray();
        }

        private static string ServiceFixtureSource()
            => @"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace Phase141E
{
    public sealed class SchemaRequest
    {
        public bool enabled { get; set; }
        public int count { get; set; }
        public List<string> labels { get; set; }
    }

    public sealed class SchemaResponse
    {
        public string status { get; set; }
        public Dictionary<string, float> metrics { get; set; }
    }

    public partial class SchemaFixture
    {
        [FoxService(""/phase141e/schema"", Type = ""Phase141E.Schema"")]
        private SchemaResponse Check(SchemaRequest request)
        {
            return new SchemaResponse { status = request != null ? ""ok"" : ""missing"" };
        }
    }
}
";

        private static string SchemaParityFixtureSource()
            => @"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Phase141EParity
{
    public enum Mode
    {
        Idle = 0,
        Run = 1
    }

    public sealed class SchemaParityRequest
    {
        [JsonProperty(""user_id"")]
        public int UserId { get; set; }

        private string Secret { get; set; }

        internal string InternalNote { get; set; }

        public Mode Mode { get; set; }

        public string ReadOnlyScalar { get; } = ""readonly"";
    }

    public sealed class SchemaParityResponse
    {
        [JsonProperty(""status_text"")]
        public string StatusText { get; set; }

        public Mode Mode { get; set; }
    }

    public partial class SchemaParityFixture
    {
        [FoxService(""/phase141e/schema_parity"", Type = ""Phase141E.SchemaParity"", RequestSchemaName = ""Phase141E.SchemaParity.Request"", ResponseSchemaName = ""Phase141E.SchemaParity.Response"")]
        private SchemaParityResponse Check(SchemaParityRequest request)
        {
            return new SchemaParityResponse { StatusText = ""ok"", Mode = Mode.Run };
        }
    }
}
";

        private enum SchemaParityMode
        {
            Idle = 0,
            Run = 1
        }

        private sealed class SchemaParityRequest
        {
            [JsonProperty("user_id")]
            public int UserId { get; set; }

            private string Secret { get; set; }

            internal string InternalNote { get; set; }

            public SchemaParityMode Mode { get; set; }

            public string ReadOnlyScalar { get; } = "readonly";
        }

        private sealed class SchemaParityResponse
        {
            [JsonProperty("status_text")]
            public string StatusText { get; set; }

            public SchemaParityMode Mode { get; set; }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            Interlocked.Increment(ref _passCount);
        }

        private static string ReadRepoText(string relativePath)
        {
            try
            {
                return File.ReadAllText(RepoPath(relativePath));
            }
            catch (FileNotFoundException ex)
            {
                Check(false, "141E file read failed for " + relativePath + ": " + ex.GetType().Name);
                return string.Empty;
            }
            catch (DirectoryNotFoundException ex)
            {
                Check(false, "141E file read failed for " + relativePath + ": " + ex.GetType().Name);
                return string.Empty;
            }
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
            {
                Check(false, "141E repository root could not be located for " + relativePath);
                return relativePath;
            }

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
