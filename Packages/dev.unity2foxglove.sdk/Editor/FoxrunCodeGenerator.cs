// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor
// Purpose: Scans assemblies for [FoxRun] attributed fields and generates
// IFoxgloveLogSource implementation .cs files for FoxgloveLogHub.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Generates real .g.cs files for [FoxRun] annotated classes.
    /// Used by IPreprocessBuildWithReport to produce IL2CPP-compatible source files.
    /// Shares code generation logic with FoxgloveLogSourceGenerator (Roslyn ISG).
    /// </summary>
    public static class FoxrunCodeGenerator
    {
        const string OutputDir = "Assets/Scripts/Generated/";

        /// <summary>
        /// Scan all loaded assemblies for [FoxRun] fields/properties on partial classes,
        /// and generate _FoxRun.g.cs files under Assets/Scripts/Generated/.
        /// Returns the list of generated file paths.
        /// Files are only written if content differs from existing file (prevents recompile loop).
        /// </summary>
        public static List<string> GenerateSourceFiles()
        {
            var result = new List<string>();
            var byClass = new Dictionary<(string Ns, string ClassName), List<MemberData>>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!type.IsClass || type.IsAbstract) continue;
                        if (!IsPartial(type)) continue;
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;

                        var members = ScanType(type);
                        if (members.Count == 0) continue;

                        var ns = type.Namespace ?? "";
                        var key = (ns, type.Name);
                        if (!byClass.TryGetValue(key, out var list))
                            byClass[key] = list = new List<MemberData>();
                        list.AddRange(members);
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded (common in Unity Editor)
                }
            }

            if (byClass.Count == 0) return result;

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scripts/Generated"));

            foreach (var kv in byClass)
            {
                var source = EmitSourceFile(kv.Value.ToArray());
                var fileName = $"{kv.Key.ClassName}_FoxRun.g.cs";
                var absolutePath = Path.Combine(Application.dataPath, "Scripts/Generated", fileName);

                bool shouldWrite = true;
                if (File.Exists(absolutePath))
                {
                    var existing = File.ReadAllText(absolutePath);
                    if (existing == source)
                        shouldWrite = false;
                }

                if (shouldWrite)
                {
                    File.WriteAllText(absolutePath, source, Encoding.UTF8);
                    Debug.Log($"[FoxrunCodeGenerator] Generated {fileName}");
                }

                result.Add(fileName);
            }

            return result;
        }

        static bool IsPartial(Type type)
        {
            // Partial classes in C# have no runtime metadata at the type level.
            // We detect by checking if the generated members (via ISG) would create
            // a partial extension. For the build step, we assume any MonoBehaviour
            // with [FoxRun] members was declared partial in source.
            // If the class was NOT partial, ISG would have emitted FOXRUN001 at Editor compile time,
            // so this is a safe assumption for Player builds.
            return true;
        }

        static List<MemberData> ScanType(Type type)
        {
            var result = new List<MemberData>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var ns = type.Namespace ?? "";
            var cn = type.Name;

            foreach (var fi in type.GetFields(flags))
            {
                var attrs = fi.GetCustomAttributes<FoxRunAttribute>();
                foreach (var a in attrs)
                {
                    result.Add(new MemberData(
                        fi.Name, fi.FieldType, ns, cn, a.Topic, a.RateHz, a.SchemaName ?? ""));
                }
            }
            foreach (var pi in type.GetProperties(flags))
            {
                var attrs = pi.GetCustomAttributes<FoxRunAttribute>();
                foreach (var a in attrs)
                {
                    result.Add(new MemberData(
                        pi.Name, pi.PropertyType, ns, cn, a.Topic, a.RateHz, a.SchemaName ?? ""));
                }
            }
            return result;
        }

        /// <summary>
        /// Generate a complete .g.cs source file string for a single class.
        /// Mirrors FoxgloveLogSourceGenerator.EmitClass logic.
        /// </summary>
        public static string EmitSourceFile(MemberData[] members)
        {
            var first = members[0];
            string ns = first.Ns;
            string className = first.ClassName;
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";

            // Build topic map (same logic as ISG EmitClass)
            var topicMap = new Dictionary<string, List<MemberData>>();
            foreach (var m in members)
            {
                if (!topicMap.TryGetValue(m.Topic, out var list))
                    topicMap[m.Topic] = list = new List<MemberData>();
                list.Add(m);
            }

            var topics = topicMap.Keys.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("// Generated as a physical fallback for Player/IL2CPP builds.");
            sb.AppendLine("// In the Unity Editor, the Roslyn analyzer already generates this partial type in memory.");
            sb.AppendLine("#if !UNITY_EDITOR");
            sb.AppendLine("#pragma warning disable");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.Scripting;");
            sb.AppendLine("using Unity.FoxgloveSDK.Components;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns)) { sb.AppendLine($"namespace {ns}"); sb.AppendLine("{"); }

            sb.AppendLine($"{pad}[Preserve]");
            sb.AppendLine($"{pad}partial class {className} : IFoxgloveLogSource");
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
                var rate = topicMap[topics[i]].Max(f => f.RateHz);
                sb.AppendLine($"{pad}            case {i}: return new FoxgloveLogTopicInfo(\"{topics[i]}\", {rate}f);");
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
                var schema = fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "";
                sb.Append($"{pad}            case {i}: mgr.PublishJson(\"{topics[i]}\", \"{schema}\", new {{ ");
                for (int j = 0; j < fields.Count; j++)
                {
                    if (j > 0) sb.Append(", ");
                    var cleanName = fields[j].MemberName.TrimStart('_');
                    sb.Append($"{cleanName} = {ValueExpr(fields[j].MemberName, fields[j].RawTypeName)}");
                }
                sb.AppendLine($" }}, nowNs); break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");
            sb.AppendLine("#endif");

            return sb.ToString();
        }

        static string ValueExpr(string name, string typeName)
        {
            var t = typeName;
            if (t.StartsWith("UnityEngine.")) t = t.Substring(12);
            switch (t)
            {
                case "Vector3": return $"new {{ x = {name}.x, y = {name}.y, z = {name}.z }}";
                case "Vector2": return $"new {{ x = {name}.x, y = {name}.y }}";
                case "Quaternion": return $"new {{ x = {name}.x, y = {name}.y, z = {name}.z, w = {name}.w }}";
                case "Color": return $"new {{ r = {name}.r, g = {name}.g, b = {name}.b, a = {name}.a }}";
                default: return name;
            }
        }

        public sealed class MemberData
        {
            public readonly string MemberName, RawTypeName, Topic, SchemaName, ClassName, Ns;
            public readonly float RateHz;

            public MemberData(string name, Type type, string ns, string cn, string topic, float rate, string schema)
            {
                MemberName = name;
                RawTypeName = type.FullName ?? type.Name;
                Ns = ns;
                ClassName = cn;
                Topic = topic;
                RateHz = rate;
                SchemaName = schema;
            }

            public MemberData(string name, string rawType, string topic, float rate, string schema)
            {
                MemberName = name;
                RawTypeName = rawType;
                Topic = topic;
                RateHz = rate;
                SchemaName = schema;
                Ns = "";
                ClassName = "";
            }
        }
    }
}
