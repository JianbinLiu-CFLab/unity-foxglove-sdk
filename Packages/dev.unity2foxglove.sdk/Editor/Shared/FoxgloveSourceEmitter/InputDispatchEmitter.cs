// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits generated, statically typed FoxRun inbound assignment.

using System.Collections.Generic;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class InputDispatchEmitter
    {
        internal static void EmitInput(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> publishTopics,
            string pad)
        {
            if (members == null || members.Count == 0)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    int IFoxgloveInputSource.FoxgloveInput_TopicCount => {members.Count};");
            sb.AppendLine();
            sb.AppendLine($"{pad}    FoxgloveInputTopicInfo IFoxgloveInputSource.FoxgloveInput_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var topic = StringLiteralEmitter.CSharpStringLiteral(member.Topic);
                var mode = member.Mode == 2 ? "FoxRunMode.PublishAndSubscribe" : "FoxRunMode.SubscribeOnly";
                sb.AppendLine($"{pad}            case {i}: return new FoxgloveInputTopicInfo(\"{topic}\", {WireEncodingLiteral(member.Encoding)}, {mode});");
            }
            sb.AppendLine($"{pad}            default: throw new ArgumentOutOfRangeException(nameof(index));");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxgloveInputSource.FoxgloveInput_TryApply(int topicIndex, byte[] payload, string encoding, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var fieldName = StringLiteralEmitter.CSharpStringLiteral(member.JsonFieldName);
                var typeName = GlobalTypeName(member.TypeName);
                var access = TypeExprEmitter.MemberAccess(member.MemberName);
                var protobuf = UsesProtobuf(member.Encoding);
                var inherited = IsInherited(member.Encoding);
                var protobufFieldNumber = FoxRunProtobufFieldNumber.Resolve(
                    member.Topic + "|" + member.SchemaName + "|" + member.MemberName,
                    member.ProtobufFieldNumber);
                var protobufReader = protobuf
                    ? ProtobufInputDispatchEmitter.ReaderCall(protobufFieldNumber, typeName, member.ProtobufTypeShape, i)
                    : string.Empty;
                var jsonReader = $"FoxRunInboundJson.TryRead(payload, \"{fieldName}\", out {typeName} __value, out error)";
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                {{");
                if (inherited)
                {
                    sb.AppendLine($"{pad}                    if (string.Equals(encoding, \"protobuf\", global::System.StringComparison.OrdinalIgnoreCase))");
                    sb.AppendLine($"{pad}                    {{");
                    sb.AppendLine($"{pad}                        if (!{protobufReader}) return false;");
                    sb.AppendLine($"{pad}                        {access} = __value;");
                    sb.AppendLine($"{pad}                    }}");
                    if (SupportsJsonInbound(member))
                    {
                        sb.AppendLine($"{pad}                    else if (string.Equals(encoding, \"json\", global::System.StringComparison.OrdinalIgnoreCase))");
                        sb.AppendLine($"{pad}                    {{");
                        sb.AppendLine($"{pad}                        if (!{jsonReader}) return false;");
                        sb.AppendLine($"{pad}                        {access} = __value;");
                        sb.AppendLine($"{pad}                    }}");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                    else if (string.Equals(encoding, \"json\", global::System.StringComparison.OrdinalIgnoreCase))");
                        sb.AppendLine($"{pad}                    {{");
                        sb.AppendLine($"{pad}                        error = \"This inherited FoxRun input requires Protobuf for its declared type.\";");
                        sb.AppendLine($"{pad}                        return false;");
                        sb.AppendLine($"{pad}                    }}");
                    }
                    sb.AppendLine($"{pad}                    else");
                    sb.AppendLine($"{pad}                    {{");
                    sb.AppendLine($"{pad}                        error = \"Unsupported FoxRun inbound wire encoding.\";");
                    sb.AppendLine($"{pad}                        return false;");
                    sb.AppendLine($"{pad}                    }}");
                }
                else
                {
                    var reader = protobuf ? protobufReader : jsonReader;
                    sb.AppendLine($"{pad}                    if (!{reader})");
                    sb.AppendLine($"{pad}                        return false;");
                    sb.AppendLine($"{pad}                    {access} = __value;");
                }
                if (member.Mode == 2)
                {
                    var publishIndex = IndexOf(publishTopics, member.Topic);
                    if (publishIndex >= 0)
                        sb.AppendLine($"{pad}                    __foxRunSuppressNextPublish_{publishIndex} = true;");
                }
                sb.AppendLine($"{pad}                    return true;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            default:");
            sb.AppendLine($"{pad}                error = \"Unknown FoxRun inbound topic index.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            ProtobufInputDispatchEmitter.EmitReaders(sb, members, pad);
        }

        private static string GlobalTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", System.StringComparison.Ordinal))
                return typeName;
            if (typeName.EndsWith("[]", System.StringComparison.Ordinal))
                return GlobalTypeName(typeName.Substring(0, typeName.Length - 2)) + "[]";
            switch (typeName)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "char":
                case "object":
                    return typeName;
            }
            return "global::" + typeName;
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            if (values == null)
                return -1;
            for (var i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, System.StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static bool UsesProtobuf(string encoding)
            => string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal)
               || IsInherited(encoding);

        private static bool IsInherited(string encoding)
            => string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, System.StringComparison.Ordinal);

        private static string WireEncodingLiteral(string encoding)
        {
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal))
                return "FoxRunWireEncoding.Protobuf";
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.JsonEncoding, System.StringComparison.Ordinal))
                return "FoxRunWireEncoding.Json";
            return "FoxRunWireEncoding.Inherit";
        }

        private static bool SupportsJsonInbound(FoxgloveSourceEmitter.TopicMember member)
        {
            if (member.ProtobufTypeShape != null
                && (member.ProtobufTypeShape.Kind == FoxRunProtobufTypeShapeKind.Object
                    || member.ProtobufTypeShape.Kind == FoxRunProtobufTypeShapeKind.Enum))
            {
                return false;
            }

            var type = member.TypeName ?? string.Empty;
            return !type.EndsWith("[]", System.StringComparison.Ordinal)
                   && type.IndexOf("List<", System.StringComparison.Ordinal) < 0
                   && type.IndexOf("IList<", System.StringComparison.Ordinal) < 0
                   && type.IndexOf("IReadOnlyList<", System.StringComparison.Ordinal) < 0;
        }
    }
}
