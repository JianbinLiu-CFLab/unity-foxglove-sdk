// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Resolves generated FoxRun wire declarations against Manager policy.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Pure wire-policy resolver shared by Manager and FoxRun routing.</summary>
    public static class FoxRunWireEncodingResolver
    {
        /// <summary>Resolves an attribute declaration to a concrete wire encoding.</summary>
        public static FoxRunWireEncoding Resolve(
            FoxRunWireEncoding declaredEncoding,
            FoxRunWireEncoding managerDefault)
        {
            switch (declaredEncoding)
            {
                case FoxRunWireEncoding.Protobuf:
                case FoxRunWireEncoding.Json:
                    return declaredEncoding;
                case FoxRunWireEncoding.Inherit:
                    return ValidateManagerDefault(managerDefault);
                default:
                    throw new ArgumentOutOfRangeException(nameof(declaredEncoding));
            }
        }

        /// <summary>Validates the concrete Manager-owned default.</summary>
        public static FoxRunWireEncoding ValidateManagerDefault(FoxRunWireEncoding managerDefault)
        {
            if (managerDefault == FoxRunWireEncoding.Protobuf || managerDefault == FoxRunWireEncoding.Json)
                return managerDefault;

            throw new ArgumentOutOfRangeException(
                nameof(managerDefault),
                "FoxRun Manager default must be Protobuf or Json.");
        }

        /// <summary>Returns the Foxglove protocol spelling for a concrete encoding.</summary>
        public static string ToProtocolEncoding(FoxRunWireEncoding encoding)
        {
            switch (encoding)
            {
                case FoxRunWireEncoding.Protobuf: return "protobuf";
                case FoxRunWireEncoding.Json: return "json";
                default: throw new ArgumentOutOfRangeException(nameof(encoding));
            }
        }

        /// <summary>Parses a generated legacy protocol spelling into a declaration.</summary>
        public static FoxRunWireEncoding FromProtocolEncoding(string encoding)
        {
            if (string.Equals(encoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                return FoxRunWireEncoding.Protobuf;
            if (string.Equals(encoding, "json", StringComparison.OrdinalIgnoreCase))
                return FoxRunWireEncoding.Json;
            if (string.Equals(encoding, "inherit", StringComparison.OrdinalIgnoreCase))
                return FoxRunWireEncoding.Inherit;

            throw new ArgumentException("Unsupported FoxRun wire encoding: " + (encoding ?? string.Empty), nameof(encoding));
        }
    }
}
