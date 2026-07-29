// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Direct, reflection-free generated member access shared with providers.

using System;

namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxRunGeneratedMemberAccess
    {
        string StableMemberId { get; }
        Type ValueType { get; }
        bool CanWrite { get; }
    }

    /// <summary>
    /// Neutral generated surface. Optional Provider partials consume the same
    /// stable accessors without reflecting over user fields or properties.
    /// </summary>
    public interface IFoxRunGeneratedTransportSource
    {
        int FoxRunTransport_MemberCount { get; }
        IFoxRunGeneratedMemberAccess FoxRunTransport_GetMember(int index);
    }

    /// <summary>
    /// A generated partial constructs this once with direct method-group
    /// delegates. Provider hot paths invoke those delegates and never inspect
    /// fields or properties through reflection.
    /// </summary>
    public sealed class FoxRunGeneratedMemberAccess<T> : IFoxRunGeneratedMemberAccess
    {
        private readonly Func<T> _read;
        private readonly Action<T> _write;

        public FoxRunGeneratedMemberAccess(
            string stableMemberId,
            Func<T> read,
            Action<T> write = null)
        {
            if (string.IsNullOrWhiteSpace(stableMemberId))
                throw new ArgumentException(
                    "Stable member ID cannot be empty.",
                    nameof(stableMemberId));
            StableMemberId = stableMemberId;
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write;
        }

        public string StableMemberId { get; }
        public Type ValueType => typeof(T);
        public bool CanWrite => _write != null;

        public T Read() => _read();

        public bool TryWrite(T value)
        {
            if (_write == null)
                return false;
            _write(value);
            return true;
        }
    }
}
