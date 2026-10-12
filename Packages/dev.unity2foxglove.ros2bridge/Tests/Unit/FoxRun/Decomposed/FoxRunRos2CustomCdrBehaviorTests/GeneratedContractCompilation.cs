// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Editor;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunRos2CustomCdrBehaviorTests
    {
        private static GeneratedContract CompileGeneratedContract()
        {
            return CompileGeneratedContract(
                CreateCustomMember(FoxRunFlow.PublishAndSubscribe));
        }

        private static GeneratedContract CompileGeneratedContract(
            FoxRunGenerationMember member)
        {
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                "Phase181",
                "GeneratedCdrProbe",
                    new[] { member }));

            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("namespace Phase181");
            source.AppendLine("{");
            source.AppendLine("    public enum StateKind { Faulted = -1, Unknown = 0, Ready = 1 }");
            source.AppendLine("    public sealed class NestedState");
            source.AppendLine("    {");
            source.AppendLine("        public bool Enabled;");
            source.AppendLine("        public string Label;");
            source.AppendLine("    }");
            source.AppendLine("    public sealed class State");
            source.AppendLine("    {");
            source.AppendLine("        public byte[] Bytes;");
            source.AppendLine("        public int Count;");
            source.AppendLine("        public StateKind Kind;");
            source.AppendLine("        public StateKind[] Kinds;");
            source.AppendLine("        public List<string> Labels;");
            source.AppendLine("        public string Message;");
            source.AppendLine("        public NestedState Nested;");
            source.AppendLine("        public int? OptionalCount;");
            source.AppendLine("        private string _optionalText;");
            source.AppendLine("        public int OptionalTextReadCount;");
            source.AppendLine("        public string OptionalText");
            source.AppendLine("        {");
            source.AppendLine("            get { OptionalTextReadCount++; return _optionalText; }");
            source.AppendLine("            set { _optionalText = value; }");
            source.AppendLine("        }");
            source.AppendLine("        public List<long> Values;");
            source.AppendLine("    }");
            source.AppendLine("    public sealed partial class GeneratedCdrProbe : global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource");
            source.AppendLine("    {");
            source.AppendLine("        private readonly string __foxRunOrigin;");
            source.AppendLine("        private readonly State __foxRunCapture_0_0;");
            source.AppendLine("        private readonly ulong __foxRunCaptureSequence_0;");
            source.AppendLine("        public State State;");
            source.AppendLine("        public bool RemoteOwned;");
            source.AppendLine("        public string RemoteTransportId;");
            source.AppendLine("        public ulong RemoteGeneration;");
            source.AppendLine("        public GeneratedCdrProbe(string origin, State source, ulong sequence)");
            source.AppendLine("        {");
            source.AppendLine("            __foxRunOrigin = origin;");
            source.AppendLine("            __foxRunCapture_0_0 = source;");
            source.AppendLine("            __foxRunCaptureSequence_0 = sequence;");
            source.AppendLine("            State = source;");
            source.AppendLine("        }");
            source.AppendLine("        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_MarkRemoteApplied(int topicIndex, string transportId, ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            RemoteOwned = true;");
            source.AppendLine("            RemoteTransportId = transportId ?? string.Empty;");
            source.AppendLine("            RemoteGeneration = generation;");
            source.AppendLine("        }");
            source.AppendLine("        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_ClearRemoteApplied(int topicIndex, string transportId, ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            if (!RemoteOwned || !string.Equals(RemoteTransportId, transportId ?? string.Empty, StringComparison.Ordinal) || RemoteGeneration != generation) return;");
            source.AppendLine("            RemoteOwned = false;");
            source.AppendLine("            RemoteTransportId = null;");
            source.AppendLine("            RemoteGeneration = 0;");
            source.AppendLine("        }");
            source.AppendLine("        bool global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_TryGetRemoteApplied(int topicIndex, out string transportId, out ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            transportId = RemoteTransportId ?? string.Empty;");
            source.AppendLine("            generation = RemoteGeneration;");
            source.AppendLine("            return RemoteOwned;");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            source.Append(emitted);

            var compilation = CSharpCompilation.Create(
                "Phase184CustomCdrBehavior_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source.ToString(),
                        new CSharpParseOptions(LanguageVersion.CSharp9)),
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            if (!emit.Success)
            {
                throw new InvalidOperationException(
                    "Generated custom CDR fixture failed to compile: "
                    + string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString()))
                    + Environment.NewLine
                    + source);
            }

            image.Position = 0;
            return new GeneratedContract(AssemblyLoadContext.Default.LoadFromStream(image));
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                    ?? string.Empty;
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(Ros2CdrWriter).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

    }
}
