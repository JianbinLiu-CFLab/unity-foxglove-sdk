// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: FoxgloveRuntime optional schema registration helpers.

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveRuntime
    {
        /// <summary>
        /// Try to load protobuf schema registration from the optional Proto assembly.
        /// If the assembly is present, registers all 46 official Foxglove protobuf schemas.
        /// This is a no-op if the proto assembly is not available.
        /// </summary>
        private void TryRegisterProtobufSchemas()
        {
            try
            {
                var type = Type.GetType(
                    "Foxglove.Schemas.ProtobufSchemasSetup, Unity.FoxgloveSDK.Proto");
                if (type == null) return;

                var method = type.GetMethod("RegisterSchemas",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    _logger.LogWarning(
                        "Optional protobuf schema registration type was found, but RegisterSchemas was missing; continuing without protobuf support.");
                    return;
                }

                var register = (Action<ISchemaRegistry>)Delegate.CreateDelegate(
                    typeof(Action<ISchemaRegistry>),
                    method,
                    throwOnBindFailure: false);
                if (register == null)
                {
                    _logger.LogWarning(
                        "Optional protobuf schema registration method has an incompatible signature; continuing without protobuf support.");
                    return;
                }

                register(_schemaRegistry);
                _protobufSchemasRegistered = true;
            }
            catch (Exception ex)
            {
                // Protobuf support is optional. Keep startup non-fatal, but emit
                // one diagnostic so real schema-registration failures are visible.
                _logger.LogWarning($"Optional protobuf schema registration failed; continuing without protobuf support: {ex.Message}");
            }
        }
    }
}
