// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: Schema registration and signature helpers for McapRecorder.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.IO
{
    public partial class McapRecorder
    {
        private static readonly object Sha256Gate = new object();
        private static readonly SHA256 SharedSha256 = SHA256.Create();

        /// <summary>
        /// Compute the Base64 SHA-256 hash of a string.
        /// </summary>
        static string Sha256(string c)
        {
            var bytes = Encoding.UTF8.GetBytes(c);
            lock (Sha256Gate)
                return Convert.ToBase64String(SharedSha256.ComputeHash(bytes));
        }

        // Schema management
        ushort GetOrCreateSchema(string sName, string sEnc, string sContent)
        {
            if (string.IsNullOrEmpty(sName) && string.IsNullOrEmpty(sEnc) && string.IsNullOrEmpty(sContent))
                return 0;

            var hash = Sha256(sContent ?? "");
            var key = (sName ?? "", sEnc ?? "", hash);
            if (_schemaIdsBySignature.TryGetValue(key, out var sid))
                return sid;

            byte[] schemaData;
            try
            {
                schemaData = sEnc == "protobuf"
                    ? Convert.FromBase64String(sContent ?? "")
                    : Encoding.UTF8.GetBytes(sContent ?? "");
            }
            catch (FormatException ex)
            {
                Fail("Invalid protobuf schema content: " + ex.Message);
                return 0;
            }

            if (_nextSchemaId == 0) { Fail("Schema ID overflow"); return 0; }
            sid = _nextSchemaId++;
            try
            {
                _writer.WriteSchema(sid, key.Item1, key.Item2, schemaData);
            }
            catch (Exception ex)
            {
                Fail("Schema write failed: " + ex.Message);
                throw;
            }
            _schemaIdsBySignature[key] = sid;
            _schemas.Add(new SchemaRecordState { Id = sid, Name = key.Item1, Encoding = key.Item2, Data = schemaData });
            return sid;
        }

        /// <summary>
        /// Compute a hex-encoded SHA-256 hash from schema name, encoding, and
        /// content, separated by null characters.
        /// </summary>
        static string ComputeSchemaHash(string schemaContent, string schemaName, string schemaEncoding)
        {
            // For schemaless channels, the signature components are all empty.
            // We treat empty schemaContent as an empty hash.
            var content = schemaContent ?? "";
            if (content.Length == 0) return "";
            var input = Encoding.UTF8.GetBytes(schemaName + "\0" + schemaEncoding + "\0" + content);
            byte[] bytes;
            lock (Sha256Gate)
                bytes = SharedSha256.ComputeHash(input);
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        /// <summary>
        /// Normalize an encoding string to a default of "json" when empty or null.
        /// </summary>
        static string NormalizeMessageEncoding(string enc) =>
            string.IsNullOrEmpty(enc) ? "json" : enc;

        static TopicSignature CreateTopicSignature(string enc, string sName, string sEnc, string sContent) =>
            new()
            {
                Encoding = NormalizeMessageEncoding(enc),
                SchemaName = sName ?? "",
                SchemaEncoding = sEnc ?? "",
                Hash = ComputeSchemaHash(sContent, sName, sEnc)
            };
    }
}
