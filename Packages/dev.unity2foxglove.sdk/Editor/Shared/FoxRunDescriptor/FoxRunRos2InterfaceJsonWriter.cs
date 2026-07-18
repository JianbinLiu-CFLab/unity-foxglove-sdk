// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Fixed-order JSON serialization for the static interface lock and settings.

using System;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunRos2InterfaceJsonWriter
    {
        public static string WriteLock(FoxRunRos2InterfaceLock value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            var builder = new StringBuilder();
            builder.Append('{');
            WriteProperty(builder, "lockSchemaVersion", value.LockSchemaVersion.ToString(CultureInfo.InvariantCulture), raw: true);
            builder.Append(',');
            WriteProperty(builder, "interfaceSchemaVersion", value.InterfaceSchemaVersion.ToString(CultureInfo.InvariantCulture), raw: true);
            builder.Append(',');
            WriteProperty(builder, "unityPackageId", value.UnityPackageId);
            builder.Append(',');
            WriteProperty(builder, "rosPackageName", value.RosPackageName);
            builder.Append(',');
            WriteProperty(builder, "interfaceRevision", value.InterfaceRevision.ToString(CultureInfo.InvariantCulture), raw: true);
            builder.Append(',');
            WriteProperty(builder, "generatorVersion", value.GeneratorVersion);
            builder.Append(',');
            WriteProperty(builder, "namingPolicyVersion", value.NamingPolicyVersion.ToString(CultureInfo.InvariantCulture), raw: true);
            builder.Append(',');
            WriteProperty(builder, "interfaceDigest", value.InterfaceDigest);
            builder.Append(',');
            WriteString(builder, "contracts");
            builder.Append(':');
            builder.Append('[');
            for (var index = 0; index < value.Contracts.Count; index++)
            {
                if (index > 0)
                    builder.Append(',');
                WriteContract(builder, value.Contracts[index]);
            }
            builder.Append(']');
            builder.Append('}');
            builder.Append('\n');
            return builder.ToString();
        }

        public static string WriteSettings(string defaultRosPackageName, bool isLocked)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            WriteProperty(builder, "settingsSchemaVersion", "1", raw: true);
            builder.Append(',');
            WriteProperty(builder, "defaultRosPackageName", defaultRosPackageName ?? string.Empty);
            builder.Append(',');
            WriteString(builder, "locked");
            builder.Append(':');
            builder.Append(isLocked ? "true" : "false");
            builder.Append('}');
            builder.Append('\n');
            return builder.ToString();
        }

        private static void WriteContract(StringBuilder builder, FoxRunRos2InterfaceContractLock value)
        {
            builder.Append('{');
            WriteProperty(builder, "declaringType", value.DeclaringType);
            builder.Append(',');
            WriteProperty(builder, "memberName", value.MemberName);
            builder.Append(',');
            WriteProperty(builder, "topic", value.Topic);
            builder.Append(',');
            WriteProperty(builder, "dtoIdentity", value.DtoIdentity);
            builder.Append(',');
            WriteProperty(builder, "payloadMessageName", value.PayloadMessageName);
            builder.Append(',');
            WriteProperty(builder, "envelopeMessageName", value.EnvelopeMessageName);
            builder.Append(',');
            WriteProperty(builder, "messageDigest", value.MessageDigest);
            builder.Append(',');
            WriteProperty(builder, "envelopeDigest", value.EnvelopeDigest);
            builder.Append('}');
        }

        private static void WriteProperty(StringBuilder builder, string name, string value, bool raw = false)
        {
            WriteString(builder, name);
            builder.Append(':');
            if (raw)
                builder.Append(value);
            else
                WriteString(builder, value);
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
