// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Stable, validated identity for an optional FoxRun transport.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Immutable reverse-domain-style identity for a FoxRun transport.
    /// Comparison and hashing are always ordinal.
    /// </summary>
    public readonly struct FoxRunTransportId : IEquatable<FoxRunTransportId>
    {
        public const int MaximumLength = 128;

        public FoxRunTransportId(string value)
        {
            Validate(value, nameof(value));
            Value = value;
        }

        public string Value { get; }

        public bool Equals(FoxRunTransportId other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is FoxRunTransportId other && Equals(other);

        public override int GetHashCode()
            => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(FoxRunTransportId left, FoxRunTransportId right)
            => left.Equals(right);

        public static bool operator !=(FoxRunTransportId left, FoxRunTransportId right)
            => !left.Equals(right);

        public static bool TryCreate(string value, out FoxRunTransportId id)
        {
            try
            {
                id = new FoxRunTransportId(value);
                return true;
            }
            catch (ArgumentException)
            {
                id = default;
                return false;
            }
        }

        internal static void Validate(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("FoxRun transport ID cannot be empty.", parameterName);
            if (value.Length > MaximumLength)
                throw new ArgumentException(
                    $"FoxRun transport ID cannot exceed {MaximumLength} characters.",
                    parameterName);
            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
                throw new ArgumentException(
                    "FoxRun transport ID cannot contain leading or trailing whitespace.",
                    parameterName);

            var segmentCount = 1;
            var segmentLength = 0;
            var previous = '\0';
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (current == '.')
                {
                    if (segmentLength == 0 || previous == '-')
                        throw new ArgumentException(
                            "FoxRun transport ID contains an empty or malformed segment.",
                            parameterName);
                    segmentCount++;
                    segmentLength = 0;
                    previous = current;
                    continue;
                }

                var valid = current >= 'a' && current <= 'z'
                            || current >= '0' && current <= '9'
                            || current == '-';
                if (!valid || (segmentLength == 0 && current == '-'))
                    throw new ArgumentException(
                        "FoxRun transport ID uses an invalid character or segment.",
                        parameterName);

                segmentLength++;
                previous = current;
            }

            if (segmentCount < 2 || segmentLength == 0 || previous == '-')
                throw new ArgumentException(
                    "FoxRun transport ID must contain at least two valid segments.",
                    parameterName);
        }
    }

    /// <summary>The SDK-owned built-in Foxglove WebSocket transport identity.</summary>
    public static class FoxgloveWebSocketTransport
    {
        public const string Id = "foxglove.websocket";
        public static readonly FoxRunTransportId TransportId = new FoxRunTransportId(Id);
    }
}
