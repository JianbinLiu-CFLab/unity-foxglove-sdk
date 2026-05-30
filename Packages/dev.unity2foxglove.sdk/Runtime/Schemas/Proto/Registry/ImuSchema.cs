// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Registry
// Purpose: Vendored descriptor set for the custom Unity2Foxglove IMU schema.

using System;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Pre-compiled FileDescriptorSet bytes for <c>unity2foxglove.Imu</c>.
    /// </summary>
    public static class ImuSchema
    {
        /// <summary>
        /// Schema name for the custom IMU message.
        /// </summary>
        public const string SchemaName = "unity2foxglove.Imu";

        /// <summary>
        /// Raw FileDescriptorSet bytes for <c>unity2foxglove.Imu</c> generated with:
        /// <c>protoc --proto_path=... --include_imports --descriptor_set_out</c>.
        /// </summary>
        public static byte[] FileDescriptorSetData => Convert.FromBase64String(
            "Cv8BCh9nb29nbGUvcHJvdG9idWYvdGltZXN0YW1wLnByb3RvEg9nb29nbGUucHJvdG9idWYiOwoJVGltZXN0YW1wEhgKB3NlY29uZHMYASABKANSB3NlY29u"
            + "ZHMSFAoFbmFub3MYAiABKAVSBW5hbm9zQoUBChNjb20uZ29vZ2xlLnByb3RvYnVmQg5UaW1lc3RhbXBQcm90b1ABWjJnb29nbGUuZ29sYW5nLm9yZy9wcm90"
            + "b2J1Zi90eXBlcy9rbm93bi90aW1lc3RhbXBwYvgBAaICA0dQQqoCHkdvb2dsZS5Qcm90b2J1Zi5XZWxsS25vd25UeXBlc2IGcHJvdG8zCp8FChh1bml0eTJm"
            + "b3hnbG92ZS9JbXUucHJvdG8SDnVuaXR5MmZveGdsb3ZlGh9nb29nbGUvcHJvdG9idWYvdGltZXN0YW1wLnByb3RvIjAKBFZlYzMSDAoBeBgBIAEoAVIBeBIM"
            + "CgF5GAIgASgBUgF5EgwKAXoYAyABKAFSAXoiPgoEUXVhdBIMCgF4GAEgASgBUgF4EgwKAXkYAiABKAFSAXkSDAoBehgDIAEoAVIBehIMCgF3GAQgASgBUgF3"
            + "ItcDCgNJbXUSOAoJdGltZXN0YW1wGAEgASgLMhouZ29vZ2xlLnByb3RvYnVmLlRpbWVzdGFtcFIJdGltZXN0YW1wEhkKCGZyYW1lX2lkGAIgASgJUgdmcmFt"
            + "ZUlkEkUKE2xpbmVhcl9hY2NlbGVyYXRpb24YAyABKAsyFC51bml0eTJmb3hnbG92ZS5WZWMzUhJsaW5lYXJBY2NlbGVyYXRpb24SPwoQYW5ndWxhcl92ZWw"
            + "vY2l0eRgEIAEoCzIULnVuaXR5MmZveGdsb3ZlLlZlYzNSD2FuZ3VsYXJWZWxvY2l0eRI2CgtvcmllbnRhdGlvbhgFIAEoCzIULnVuaXR5MmZveGdsb3ZlLlF1"
            + "YXRSC29yaWVudGF0aW9uEjUKFm9yaWVudGF0aW9uX2NvdmFyaWFuY2UYBiADKAFSFW9yaWVudGF0aW9uQ292YXJpYW5jZRI+Chthbmd1bGFyX3ZlbG9jaXR5"
            + "X2NvdmFyaWFuY2UYByADKAFSGWFuZ3VsYXJWZWxvY2l0eUNvdmFyaWFuY2USRAoebGluZWFyX2FjY2VsZXJhdGlvbl9jb3ZhcmlhbmNlGAggAygBUhxsaW5l"
            + "YXJBY2NlbGVyYXRpb25Db3ZhcmlhbmNlYgZwcm90bzM=");
    }
}
