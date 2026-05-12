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
        /// Emits a C# change-detection expression comparing current and last values.
        /// The generated expression is part of the AOT-safe source and must not
        /// rely on runtime reflection.
        /// </summary>
        public static string ChangeExpr(string member, string type, string lastVar, float epsilon)
        {
            var t = type.StartsWith("UnityEngine.") ? type.Substring(12) : type;
            var eps = FloatLiteral(epsilon < 0 ? 0 : epsilon);
            switch (t)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return $"__foxrun_float_changed(this.{member}, {lastVar}, {eps})";
                case "double":
                case "Double":
                case "System.Double":
                    return $"__foxrun_double_changed(this.{member}, {lastVar}, {eps})";
                case "Vector3":
                    return $"__foxrun_float_changed(this.{member}.x, {lastVar}.x, {eps}) || __foxrun_float_changed(this.{member}.y, {lastVar}.y, {eps}) || __foxrun_float_changed(this.{member}.z, {lastVar}.z, {eps})";
                case "Vector2":
                    return $"__foxrun_float_changed(this.{member}.x, {lastVar}.x, {eps}) || __foxrun_float_changed(this.{member}.y, {lastVar}.y, {eps})";
                case "Quaternion":
                    return $"__foxrun_float_changed(this.{member}.x, {lastVar}.x, {eps}) || __foxrun_float_changed(this.{member}.y, {lastVar}.y, {eps}) || __foxrun_float_changed(this.{member}.z, {lastVar}.z, {eps}) || __foxrun_float_changed(this.{member}.w, {lastVar}.w, {eps})";
                case "Color":
                    return $"__foxrun_float_changed(this.{member}.r, {lastVar}.r, {eps}) || __foxrun_float_changed(this.{member}.g, {lastVar}.g, {eps}) || __foxrun_float_changed(this.{member}.b, {lastVar}.b, {eps}) || __foxrun_float_changed(this.{member}.a, {lastVar}.a, {eps})";
                default:
                    return $"!EqualityComparer<{type}>.Default.Equals(this.{member}, {lastVar})";
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
        public static string EmitClass(string ns, string className, IReadOnlyList<TopicMember> members)
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
            var hasPolicy = members.Any(m => m.PublishMode != 0);
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
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
                var mode = fields.Max(m => m.PublishMode);
                var eps = fields.Max(m => m.ChangeEpsilon);
                var forceInt = fields.Max(m => m.ForceIntervalSeconds);
                var topic = CSharpStringLiteral(topics[i]);
                sb.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, (FoxRunPublishMode){4}, {5}f, {6}f);", pad, i, topic, rate, mode, eps, forceInt));
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
                sb.Append($"{pad}            case {i}: mgr.PublishJson(\"{topic}\", \"{schema}\", new {{ ");
                for (int j = 0; j < fields.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var cleanName = fields[j].MemberName.TrimStart('_');
                    sb.Append($"{cleanName} = {ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
                }
                sb.AppendLine($" }}, nowNs); break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            // Policy methods are emitted only when at least one topic uses a
            // non-FixedRate mode; fixed-rate sources keep the smaller legacy shape.
            if (hasPolicy)
            {
                sb.AppendLine();
                // Last-value storage per topic
                for (int i = 0; i < topics.Count; i++)
                {
                    var fields = topicMap[topics[i]];
                    if (fields.All(f => f.PublishMode == 0)) continue;
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
                    if (fields.All(f => f.PublishMode == 0))
                    {
                        sb.AppendLine($"{pad}            case {i}: return true;");
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
                    var mode = fields.Max(f => f.PublishMode);
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
                    if (fields.All(f => f.PublishMode == 0)) continue;
                    sb.AppendLine($"{pad}            case {i}:");
                    for (int j = 0; j < fields.Count; j++)
                        sb.AppendLine($"{pad}                __last_{i}_{j} = this.{fields[j].MemberName};");
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
            var access = "this." + name;
            switch (t)
            {
                case "Vector3": return $"new {{ x = {access}.x, y = {access}.y, z = {access}.z }}";
                case "Vector2": return $"new {{ x = {access}.x, y = {access}.y }}";
                case "Quaternion": return $"new {{ x = {access}.x, y = {access}.y, z = {access}.z, w = {access}.w }}";
                case "Color": return $"new {{ r = {access}.r, g = {access}.g, b = {access}.b, a = {access}.a }}";
                default: return access;
            }
        }

        private static string FloatLiteral(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        private static string PublishModeLiteral(int mode)
        {
            switch (mode)
            {
                case 0: return "FoxRunPublishMode.FixedRate";
                case 1: return "FoxRunPublishMode.OnChange";
                case 2: return "FoxRunPublishMode.OnChangeOrInterval";
                default: return FormattableString.Invariant($"(FoxRunPublishMode){mode}");
            }
        }
    }
}
