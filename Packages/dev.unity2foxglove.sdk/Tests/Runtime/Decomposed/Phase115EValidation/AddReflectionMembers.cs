// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxRun analyzer diagnostics and generation-model equivalence.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase115EValidation.
    /// </summary>
    public static partial class Phase115EValidation
    {
        private static void AddReflectionMembers(
            Type declaringType,
            MemberInfo member,
            string memberKind,
            Type memberType,
            int rawMemberOrder,
            List<FoxRunReflectionGenerationMember> members)
        {
            foreach (var attribute in CustomAttributeData.GetCustomAttributes(member)
                         .Where(data => data.AttributeType.FullName == typeof(FoxRunAttribute).FullName))
            {
                var topic = (string)attribute.ConstructorArguments[0].Value;
                var hz = -1f;
                var schemaName = string.Empty;
                var policy = (int)FoxRunPolicy.FixedRate;
                var tolerance = 0f;
                var presence = FoxRunNamedArgumentPresence.None;
                foreach (var argument in attribute.NamedArguments)
                {
                    switch (argument.MemberName)
                    {
                        case "Hz":
                            hz = Convert.ToSingle(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Hz;
                            break;
                        case "SchemaName":
                            schemaName = (string)argument.TypedValue.Value ?? string.Empty;
                            presence |= FoxRunNamedArgumentPresence.SchemaName;
                            break;
                        case "Policy":
                            policy = Convert.ToInt32(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Policy;
                            break;
                        case "Tolerance":
                            tolerance = Convert.ToSingle(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Tolerance;
                            break;
                    }
                }
                var isArray = TryGetArrayElementType(memberType, out var elementType);
                members.Add(new FoxRunReflectionGenerationMember(
                    declaringType.Namespace ?? string.Empty,
                    declaringType.Name,
                    member.Name,
                    memberKind,
                    memberType.FullName ?? memberType.Name,
                    FoxRunEmissionTypeNameFormatter.FromReflectionType(memberType),
                    isValueType: memberType.IsValueType,
                    isArray: isArray,
                    elementTypeName: elementType == null ? string.Empty : elementType.FullName ?? elementType.Name,
                    topic: topic,
                    schemaName: schemaName,
                    hz: hz,
                    policy: policy,
                    tolerance: tolerance,
                    rawMemberOrder: rawMemberOrder,
                    conditionalSymbols: "FOXRUN_FIXTURE_EXTRA",
                    typeShape: FoxRunReflectionTypeShapeBuilder.Build(memberType),
                    namedArgumentPresence: presence));
            }
        }

        private static bool TryGetArrayElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        private static bool BytesContainText(byte[] bytes, string text)
        {
            return IndexOf(bytes, Encoding.UTF8.GetBytes(text)) >= 0
                   || IndexOf(bytes, Encoding.Unicode.GetBytes(text)) >= 0;
        }

        private static int IndexOf(byte[] source, byte[] pattern)
        {
            if (source == null || pattern == null || pattern.Length == 0 || source.Length < pattern.Length)
                return -1;
            for (var i = 0; i <= source.Length - pattern.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] == pattern[j])
                        continue;
                    matched = false;
                    break;
                }
                if (matched)
                    return i;
            }
            return -1;
        }

        private static JToken SemanticDescriptor(string descriptorJson)
        {
            var json = JToken.Parse(descriptorJson);
            foreach (var property in ((JContainer)json).DescendantsAndSelf().OfType<JProperty>().ToList())
            {
                if (property.Name == "hostKind"
                    || property.Name == "rawTypeName"
                    || property.Name == "rawMemberOrder"
                    || property.Name == "conditionalSymbols")
                    property.Remove();
            }
            return json;
        }

        private static FoxRunGenerationMember Member(
            string ns,
            string className,
            string memberName,
            string topic,
            string rawType,
            string hostKind,
            int rawMemberOrder)
        {
            return new FoxRunGenerationMember(
                ns: ns,
                className: className,
                memberName: memberName,
                memberKind: "field",
                rawTypeName: rawType,
                isValueType: rawType == "float" || rawType == "System.Single",
                isArray: false,
                elementTypeName: string.Empty,
                topic: topic,
                hz: 10f,
                schemaName: string.Empty,
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                hostKind: hostKind,
                rawMemberOrder: rawMemberOrder,
                conditionalSymbols: string.Empty);
        }

        private static string ReadRepoText(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static string NormalizeNewlines(string value)
            => value.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
