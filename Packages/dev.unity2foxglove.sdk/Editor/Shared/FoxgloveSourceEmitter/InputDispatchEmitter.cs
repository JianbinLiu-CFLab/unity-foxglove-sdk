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
                var encoding = string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal)
                    ? FoxRunGenerationDescriptorConstants.ProtobufEncoding
                    : FoxRunGenerationDescriptorConstants.JsonEncoding;
                sb.AppendLine($"{pad}            case {i}: return new FoxgloveInputTopicInfo(\"{topic}\", \"{encoding}\", {mode});");
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
                var protobuf = string.Equals(member.Encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, System.StringComparison.Ordinal);
                var protobufFieldNumber = FoxRunProtobufFieldNumber.Resolve(
                    member.Topic + "|" + member.SchemaName + "|" + member.MemberName,
                    member.ProtobufFieldNumber);
                var reader = protobuf
                    ? ProtobufInputDispatchEmitter.ReaderCall(protobufFieldNumber, typeName, member.ProtobufTypeShape, i)
                    : $"FoxRunInboundJson.TryRead(payload, \"{fieldName}\", out {typeName} __value, out error)";
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                {{");
                sb.AppendLine($"{pad}                    if (!{reader})");
                sb.AppendLine($"{pad}                        return false;");
                sb.AppendLine($"{pad}                    {access} = __value;");
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
    }
}
