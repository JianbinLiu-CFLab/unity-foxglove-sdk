// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Shared code-generation template for [FoxRun] IFoxgloveLogSource
// implementation. Used by both the Roslyn ISG (FoxgloveLogSourceGenerator) and
// the Editor build-time physical fallback (FoxrunCodeGenerator) to keep the
// generated class body consistent across Editor and Player build paths.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Pure C# string-builder that produces the generated partial class
    /// implementing <c>IFoxgloveLogSource</c>. Both the Roslyn ISG and the
    /// build-time physical fallback call this emitter so policy and payload
    /// generation cannot drift between Editor and Player paths.
    /// </summary>
    /// <remarks>
    /// This file lives under <c>Editor/Shared/</c> and is compiled by both:
    /// <list type="bullet">
    ///   <item>Unity Editor assembly (via <c>Editor/</c> .asmdef)</item>
    ///   <item>Source Generator project (via linked compile item in the
    ///       <c>.csproj</c>)</item>
    /// </list>
    /// It must NOT depend on any Roslyn, UnityEngine, or UnityEditor types.
    /// </remarks>
    public static class FoxgloveSourceEmitter
    {
        /// <summary>
        /// Descriptor for a single topic-member mapping used by the shared
        /// emitter. Backs both <c>FoxrunCodeGenerator.MemberData</c> and the
        /// ISG's <c>ExtractMember</c> output.
        /// </summary>
        public sealed class TopicMember
        {
            /// <summary>Field or property name as declared in source.</summary>
            public readonly string MemberName;
            /// <summary>Fully-qualified type name (e.g.
            /// <c>UnityEngine.Vector3</c>).</summary>
            public readonly string TypeName;
            /// <summary>Topic string from <c>[FoxRun("/topic")]</c>.</summary>
            public readonly string Topic;
            /// <summary>Publishing rate in Hz.</summary>
            public readonly float RateHz;
            /// <summary>Optional schema name.</summary>
            public readonly string SchemaName;
            /// <summary>Publish mode from the attribute.</summary>
            public readonly int PublishMode;
            /// <summary>Change epsilon for numeric comparison.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval for OnChangeOrInterval.</summary>
            public readonly float ForceIntervalSeconds;

            /// <summary>
            /// Creates a topic-member descriptor for the shared emitter.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName)
                : this(memberName, typeName, topic, rateHz, schemaName, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic-member descriptor with publish policy.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName,
                int publishMode, float changeEpsilon, float forceIntervalSeconds)
            {
                MemberName = memberName;
                TypeName = typeName;
                Topic = topic;
                RateHz = rateHz;
                SchemaName = schemaName;
                PublishMode = publishMode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
            }
        }

        /// <summary>
        /// Returns the stable generated source file name for a FoxRun partial
        /// class. Global-namespace classes keep the historical
        /// <c>ClassName_FoxRun.g.cs</c> shape; namespaced classes include the
        /// namespace identity to avoid Roslyn hint-name and physical fallback
        /// file collisions.
        /// </summary>
        public static string GeneratedSourceName(string ns, string className)
        {
            var identity = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            return SanitizeFileStem(identity) + "_FoxRun.g.cs";
        }

        /// <summary>
        /// Emits a C# change-detection expression comparing current and last values.
        /// The generated expression is part of the AOT-safe source and must not
        /// rely on runtime reflection.
        /// </summary>
        public static string ChangeExpr(string member, string type, string lastVar, float epsilon)
        {
            var t = type.StartsWith("UnityEngine.") ? type.Substring(12) : type;
            var eps = FloatLiteral(epsilon < 0 ? 0 : epsilon);
            var access = MemberAccess(member);
            switch (t)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return $"__foxrun_float_changed({access}, {lastVar}, {eps})";
                case "double":
                case "Double":
                case "System.Double":
                    return $"__foxrun_double_changed({access}, {lastVar}, {eps})";
                case "Vector3":
                    return $"__foxrun_float_changed({access}.x, {lastVar}.x, {eps}) || __foxrun_float_changed({access}.y, {lastVar}.y, {eps}) || __foxrun_float_changed({access}.z, {lastVar}.z, {eps})";
                case "Vector2":
                    return $"__foxrun_float_changed({access}.x, {lastVar}.x, {eps}) || __foxrun_float_changed({access}.y, {lastVar}.y, {eps})";
                case "Quaternion":
                    return $"__foxrun_float_changed({access}.x, {lastVar}.x, {eps}) || __foxrun_float_changed({access}.y, {lastVar}.y, {eps}) || __foxrun_float_changed({access}.z, {lastVar}.z, {eps}) || __foxrun_float_changed({access}.w, {lastVar}.w, {eps})";
                case "Color":
                    return $"__foxrun_float_changed({access}.r, {lastVar}.r, {eps}) || __foxrun_float_changed({access}.g, {lastVar}.g, {eps}) || __foxrun_float_changed({access}.b, {lastVar}.b, {eps}) || __foxrun_float_changed({access}.a, {lastVar}.a, {eps})";
                default:
                    return $"!EqualityComparer<{type}>.Default.Equals({access}, {lastVar})";
            }
        }

        private static string CSharpStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Emits the generated partial class source for one class name / namespace
        /// pair.
        /// </summary>
        /// <param name="ns">Containing namespace (empty for global).</param>
        /// <param name="className">Declaring class name.</param>
        /// <param name="members">All <c>[FoxRun]</c> attributed members of this class.</param>
        /// <returns>Generated C# source as a string.</returns>
        public static string EmitClass(FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return EmitClassCore(type.Namespace, type.ClassName, type.Members.Select(member => member.ToTopicMember()).ToList());
        }

        public static string EmitClass(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            return EmitClassCore(ns, className, members);
        }

        private static string EmitClassCore(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            if (members == null || members.Count == 0)
                throw new ArgumentException("At least one member is required.", nameof(members));

            var topicMap = new Dictionary<string, List<TopicMember>>();
            foreach (var m in members)
            {
                if (!topicMap.TryGetValue(m.Topic, out var list))
                    topicMap[m.Topic] = list = new List<TopicMember>();
                list.Add(m);
            }

            var topics = topicMap.Keys.ToList();
            var topicModes = topicMap.ToDictionary(kvp => kvp.Key, kvp => TopicPublishMode(kvp.Value));
            var triggerTopicIndexes = topics
                .Select((topic, index) => new { topic, index })
                .Where(x => topicModes[x.topic] == 3)
                .Select(x => x.index)
                .ToList();
            var triggerMembers = BuildTriggerMembers(members, topics, topicModes);
            var hasPolicy = members.Any(m => m.PublishMode != 0);
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.");
            sb.AppendLine("// SPDX-License-Identifier: Apache-2.0");
            sb.AppendLine("// Generated by the Unity2Foxglove [FoxRun] source emitter.");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.Scripting;");
            sb.AppendLine("using Unity.FoxgloveSDK.Components;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns)) { sb.AppendLine($"namespace {ns}"); sb.AppendLine("{"); }

            sb.AppendLine($"{pad}[Preserve]");
            sb.Append(hasPolicy
                ? $"{pad}partial class {className} : IFoxgloveLogSource, IFoxgloveLogPolicySource\n"
                : $"{pad}partial class {className} : IFoxgloveLogSource\n");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    int IFoxgloveLogSource.FoxgloveLog_TopicCount => {topics.Count};");
            sb.AppendLine();

            // GetTopic
            sb.AppendLine($"{pad}    FoxgloveLogTopicInfo IFoxgloveLogSource.FoxgloveLog_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var rate = fields.Max(m => m.RateHz);
                var mode = topicModes[topics[i]];
                var eps = fields.Max(m => m.ChangeEpsilon);
                var forceInt = fields.Max(m => m.ForceIntervalSeconds);
                var topic = CSharpStringLiteral(topics[i]);
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, {4}, {5}f, {6}f);",
                    pad, i, topic, rate, PublishModeLiteral(mode), eps, forceInt));
            }
            sb.AppendLine($"{pad}            default: return default;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();

            // Publish
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveLogSource.FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var schema = CSharpStringLiteral(fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "");
                var topic = CSharpStringLiteral(topics[i]);
                sb.AppendLine($"{pad}            case {i}: mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields)}, nowNs); break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            if (triggerMembers.Count > 0)
            {
                sb.AppendLine();
                foreach (var trigger in triggerMembers)
                {
                    sb.AppendLine($"{pad}    public bool {trigger.MethodName}()");
                    sb.AppendLine($"{pad}    {{");
                    sb.AppendLine($"{pad}        var published = false;");
                    foreach (var topicIndex in trigger.TopicIndexes)
                        sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
                    sb.AppendLine($"{pad}        return published;");
                    sb.AppendLine($"{pad}    }}");
                    sb.AppendLine();
                }

                sb.AppendLine($"{pad}    public bool FoxRun_TriggerAll()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var published = false;");
                foreach (var topicIndex in triggerTopicIndexes)
                    sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
                sb.AppendLine($"{pad}        return published;");
                sb.AppendLine($"{pad}    }}");
            }

            // Policy methods are emitted only when at least one topic uses a
            // non-FixedRate mode; fixed-rate sources keep the smaller legacy shape.
            if (hasPolicy)
            {
                sb.AppendLine();
                // Last-value storage per topic
                for (int i = 0; i < topics.Count; i++)
                {
                    var fields = topicMap[topics[i]];
                    var mode = topicModes[topics[i]];
                    if (mode == 0 || mode == 3) continue;
                    sb.AppendLine($"{pad}    private bool __hasLast_{i};");
                    sb.AppendLine($"{pad}    private double __lastPublishSec_{i};");
                    for (int j = 0; j < fields.Count; j++)
                        sb.AppendLine($"{pad}    private {fields[j].TypeName} __last_{i}_{j};");
                }
                sb.AppendLine();

                sb.AppendLine($"{pad}    private static bool __foxrun_float_changed(float current, float last, float epsilon)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        if (float.IsNaN(current) || float.IsNaN(last)) return !(float.IsNaN(current) && float.IsNaN(last));");
                sb.AppendLine($"{pad}        return Math.Abs(current - last) > epsilon;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private static bool __foxrun_double_changed(double current, double last, double epsilon)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        if (double.IsNaN(current) || double.IsNaN(last)) return !(double.IsNaN(current) && double.IsNaN(last));");
                sb.AppendLine($"{pad}        return Math.Abs(current - last) > epsilon;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();

                // ShouldPublish
                sb.AppendLine($"{pad}    bool IFoxgloveLogPolicySource.FoxgloveLog_ShouldPublish(int topicIndex, double nowSec)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        bool changed;");
                sb.AppendLine($"{pad}        switch (topicIndex)");
                sb.AppendLine($"{pad}        {{");
                for (int i = 0; i < topics.Count; i++)
                {
                    var fields = topicMap[topics[i]];
                    var mode = topicModes[topics[i]];
                    if (mode == 0)
                    {
                        sb.AppendLine($"{pad}            case {i}: return true;");
                        continue;
                    }
                    if (mode == 3)
                    {
                        sb.AppendLine($"{pad}            case {i}: return false;");
                        continue;
                    }
                    sb.AppendLine($"{pad}            case {i}:");
                    sb.AppendLine($"{pad}                changed = !__hasLast_{i};");
                    for (int j = 0; j < fields.Count; j++)
                    {
                        var f = fields[j];
                        var eps = f.ChangeEpsilon;
                        sb.AppendLine($"{pad}                if (!changed) changed = {ChangeExpr(f.MemberName, f.TypeName, "__last_" + i + "_" + j, eps)};");
                    }
                    var forceInt = fields.Max(f => f.ForceIntervalSeconds);
                    sb.AppendLine($"{pad}                return Unity.FoxgloveSDK.Util.FoxRunPublishPolicy.ShouldPublish(" +
                        $"{PublishModeLiteral(mode)}, nowSec, __hasLast_{i}, changed, __lastPublishSec_{i}, {FloatLiteral(forceInt < 0 ? 0 : forceInt)});");
                }
                sb.AppendLine($"{pad}            default: return true;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();

                // MarkPublished
                sb.AppendLine($"{pad}    void IFoxgloveLogPolicySource.FoxgloveLog_MarkPublished(int topicIndex, double nowSec)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        switch (topicIndex)");
                sb.AppendLine($"{pad}        {{");
                for (int i = 0; i < topics.Count; i++)
                {
                    var fields = topicMap[topics[i]];
                    var mode = topicModes[topics[i]];
                    if (mode == 0 || mode == 3) continue;
                    sb.AppendLine($"{pad}            case {i}:");
                    for (int j = 0; j < fields.Count; j++)
                        sb.AppendLine($"{pad}                __last_{i}_{j} = {MemberAccess(fields[j].MemberName)};");
                    sb.AppendLine($"{pad}                __hasLast_{i} = true;");
                    sb.AppendLine($"{pad}                __lastPublishSec_{i} = nowSec;");
                    sb.AppendLine($"{pad}                break;");
                }
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");
            }

            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// Returns a C# anonymous-object expression string for a Unity type
        /// (<c>Vector3</c>, <c>Vector2</c>, <c>Quaternion</c>, <c>Color</c>),
        /// or the raw member name for all other types.
        /// </summary>
        public static string ValueExpr(string name, string type)
        {
            var t = type;
            if (t.StartsWith("UnityEngine.")) t = t.Substring(12);
            var access = MemberAccess(name);
            switch (t)
            {
                case "Vector3": return $"new {{ x = {access}.x, y = {access}.y, z = {access}.z }}";
                case "Vector2": return $"new {{ x = {access}.x, y = {access}.y }}";
                case "Quaternion": return $"new {{ x = {access}.x, y = {access}.y, z = {access}.z, w = {access}.w }}";
                case "Color": return $"new {{ r = {access}.r, g = {access}.g, b = {access}.b, a = {access}.a }}";
                default: return access;
            }
        }

        private static string PayloadExpr(IReadOnlyList<TopicMember> fields)
        {
            var jsonNames = fields.Select(f => JsonFieldName(f.MemberName)).ToList();
            if (jsonNames.All(IsAnonymousPropertyName))
            {
                var sb = new StringBuilder("new { ");
                for (int j = 0; j < fields.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append($"{EscapeIdentifier(jsonNames[j])} = {ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
                }
                sb.Append(" }");
                return sb.ToString();
            }

            var dict = new StringBuilder("new Dictionary<string, object> { ");
            for (int j = 0; j < fields.Count; j++)
            {
                if (j > 0) dict.Append(", ");
                dict.Append($"[\"{CSharpStringLiteral(jsonNames[j])}\"] = {ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
            }
            dict.Append(" }");
            return dict.ToString();
        }

        private static string JsonFieldName(string memberName)
        {
            var name = memberName != null && memberName.StartsWith("@", StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? "";
            return name.TrimStart('_');
        }

        private static string MemberAccess(string memberName)
        {
            return "this." + EscapeIdentifier(memberName);
        }

        private static string FloatLiteral(float value)
        {
            return value.ToString("G9", CultureInfo.InvariantCulture) + "f";
        }

        private static string PublishModeLiteral(int mode)
        {
            switch (mode)
            {
                case 0: return "FoxRunPublishMode.FixedRate";
                case 1: return "FoxRunPublishMode.OnChange";
                case 2: return "FoxRunPublishMode.OnChangeOrInterval";
                case 3: return "FoxRunPublishMode.OnTrigger";
                default: return FormattableString.Invariant($"(FoxRunPublishMode){mode}");
            }
        }

        private static int TopicPublishMode(IReadOnlyList<TopicMember> fields)
        {
            if (fields.Any(f => f.PublishMode == 3))
                return 3;
            if (fields.Any(f => f.PublishMode == 2))
                return 2;
            if (fields.Any(f => f.PublishMode == 1))
                return 1;
            return fields.Max(f => f.PublishMode);
        }

        private sealed class TriggerMember
        {
            public readonly string MethodName;
            public readonly List<int> TopicIndexes;

            public TriggerMember(string methodName, List<int> topicIndexes)
            {
                MethodName = methodName;
                TopicIndexes = topicIndexes;
            }
        }

        private static List<TriggerMember> BuildTriggerMembers(
            IReadOnlyList<TopicMember> members,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, int> topicModes)
        {
            var usedNames = new HashSet<string>();
            var result = new List<TriggerMember>();

            foreach (var group in members.Where(m => m.PublishMode == 3).GroupBy(m => m.MemberName).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var topicIndexes = group
                    .Select(m => IndexOfTopic(topics, m.Topic))
                    .Where(i => i >= 0 && topicModes[topics[i]] == 3)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();
                if (topicIndexes.Count == 0)
                    continue;

                var baseName = "FoxRun_Trigger_" + SanitizeIdentifier(group.Key.TrimStart('_'));
                var methodName = baseName;
                var suffix = 2;
                while (!usedNames.Add(methodName))
                    methodName = baseName + "_" + suffix++;

                result.Add(new TriggerMember(methodName, topicIndexes));
            }

            return result;
        }

        private static int IndexOfTopic(IReadOnlyList<string> topics, string topic)
        {
            for (var i = 0; i < topics.Count; i++)
                if (topics[i] == topic)
                    return i;
            return -1;
        }

        private static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Member";

            var sb = new StringBuilder(value.Length + 1);
            if (!IsIdentifierStart(value[0]))
                sb.Append('_');

            foreach (var ch in value)
                sb.Append(IsIdentifierPart(ch) ? ch : '_');

            return sb.ToString();
        }

        private static string SanitizeFileStem(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "FoxRunSource";

            var sb = new StringBuilder(value.Length + 1);
            foreach (var ch in value)
                sb.Append(IsIdentifierPart(ch) ? ch : '_');

            return sb.Length == 0 ? "FoxRunSource" : sb.ToString();
        }

        private static string EscapeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var bare = value.StartsWith("@", StringComparison.Ordinal) ? value.Substring(1) : value;
            return IsCSharpKeyword(bare) ? "@" + bare : value;
        }

        private static bool IsAnonymousPropertyName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            var bare = value.StartsWith("@", StringComparison.Ordinal) ? value.Substring(1) : value;
            if (string.IsNullOrEmpty(bare) || !IsIdentifierStart(bare[0]))
                return false;

            for (var i = 1; i < bare.Length; i++)
                if (!IsIdentifierPart(bare[i]))
                    return false;

            return true;
        }

        private static bool IsCSharpKeyword(string value)
        {
            switch (value)
            {
                case "abstract":
                case "as":
                case "base":
                case "bool":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "checked":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "goto":
                case "if":
                case "implicit":
                case "in":
                case "int":
                case "interface":
                case "internal":
                case "is":
                case "lock":
                case "long":
                case "namespace":
                case "new":
                case "null":
                case "object":
                case "operator":
                case "out":
                case "override":
                case "params":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "ref":
                case "return":
                case "sbyte":
                case "sealed":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unsafe":
                case "ushort":
                case "using":
                case "virtual":
                case "void":
                case "volatile":
                case "while":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIdentifierStart(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        private static bool IsIdentifierPart(char ch)
        {
            return ch == '_' || char.IsLetterOrDigit(ch);
        }
    }
}
