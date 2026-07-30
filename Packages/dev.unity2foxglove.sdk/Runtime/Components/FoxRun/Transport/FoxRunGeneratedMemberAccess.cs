// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Direct, reflection-free generated member access shared with providers.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public interface IFoxRunGeneratedMemberAccess
    {
        string StableMemberId { get; }
        Type ValueType { get; }
        string Topic { get; }
        string LogicalSchemaName { get; }
        FoxRunFlow Flow { get; }
        IReadOnlyList<string> PublishTransportIds { get; }
        string SubscribeTransportId { get; }
        FoxRunEncoding DeclaredEncoding { get; }
        FoxRunDeliveryPolicy DeliveryPolicy { get; }
        bool CanRead { get; }
        bool CanWrite { get; }
        object ReadBoxed();
        bool TryWriteBoxed(object value);
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
        private readonly IReadOnlyList<string> _publishTransportIds;

        public FoxRunGeneratedMemberAccess(
            string stableMemberId,
            Func<T> read,
            Action<T> write = null)
            : this(
                stableMemberId,
                string.Empty,
                string.Empty,
                FoxRunFlow.Publish,
                null,
                null,
                (FoxRunEncoding)0,
                FoxRunDeliveryPolicy.ProviderDefault,
                read,
                write)
        {
        }

        public FoxRunGeneratedMemberAccess(
            string stableMemberId,
            string topic,
            string logicalSchemaName,
            FoxRunFlow flow,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId,
            FoxRunEncoding declaredEncoding,
            FoxRunDeliveryPolicy deliveryPolicy,
            Func<T> read,
            Action<T> write = null)
        {
            if (string.IsNullOrWhiteSpace(stableMemberId))
                throw new ArgumentException(
                    "Stable member ID cannot be empty.",
                    nameof(stableMemberId));
            StableMemberId = stableMemberId;
            Topic = topic ?? string.Empty;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Flow = flow;
            _publishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .Select(value => new FoxRunTransportId(value).Value)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId == null
                ? null
                : new FoxRunTransportId(subscribeTransportId).Value;
            DeclaredEncoding = declaredEncoding;
            DeliveryPolicy = deliveryPolicy;
            _read = read;
            _write = write;
            if (_read == null && _write == null)
                throw new ArgumentException(
                    "At least one generated accessor is required.");
        }

        public string StableMemberId { get; }
        public Type ValueType => typeof(T);
        public string Topic { get; }
        public string LogicalSchemaName { get; }
        public FoxRunFlow Flow { get; }
        public IReadOnlyList<string> PublishTransportIds =>
            _publishTransportIds;
        public string SubscribeTransportId { get; }
        public FoxRunEncoding DeclaredEncoding { get; }
        public FoxRunDeliveryPolicy DeliveryPolicy { get; }
        public bool CanRead => _read != null;
        public bool CanWrite => _write != null;

        public T Read()
            => _read != null
                ? _read()
                : throw new InvalidOperationException(
                    "This generated member is write-only.");

        public object ReadBoxed() => Read();

        public bool TryWrite(T value)
        {
            if (_write == null)
                return false;
            _write(value);
            return true;
        }

        public bool TryWriteBoxed(object value)
        {
            if (_write == null || !(value is T typed))
                return false;
            _write(typed);
            return true;
        }
    }
}
