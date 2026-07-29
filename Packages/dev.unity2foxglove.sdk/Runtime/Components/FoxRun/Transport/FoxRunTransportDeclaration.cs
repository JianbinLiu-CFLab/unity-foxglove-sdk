// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Direction-legal declaration routing and deterministic transport identity.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunTransportDeclaration
    {
        private readonly string[] _publishTransportIds;

        public FoxRunTransportDeclaration(
            FoxRunFlow mode,
            string[] publishTransportIds,
            string subscribeTransportId,
            FoxRunEncoding? encoding = null)
        {
            if (mode != FoxRunFlow.Publish
                && mode != FoxRunFlow.Subscribe
                && mode != FoxRunFlow.PublishAndSubscribe)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            var publishes = mode == FoxRunFlow.Publish
                            || mode == FoxRunFlow.PublishAndSubscribe;
            var subscribes = mode == FoxRunFlow.Subscribe
                             || mode == FoxRunFlow.PublishAndSubscribe;
            if (!publishes && publishTransportIds != null)
                throw new ArgumentException(
                    "Subscribe-only declarations cannot set publish transport IDs.",
                    nameof(publishTransportIds));
            if (!subscribes && subscribeTransportId != null)
                throw new ArgumentException(
                    "Publish-only declarations cannot set a subscribe transport ID.",
                    nameof(subscribeTransportId));
            if (publishes
                && publishTransportIds != null
                && publishTransportIds.Length == 0)
            {
                throw new ArgumentException(
                    "An explicit publish transport array cannot be empty.",
                    nameof(publishTransportIds));
            }

            Mode = mode;
            _publishTransportIds = publishTransportIds == null
                ? null
                : Canonicalize(publishTransportIds, nameof(publishTransportIds));
            SubscribeTransportId = subscribeTransportId == null
                ? null
                : new FoxRunTransportId(subscribeTransportId);
            if (encoding.HasValue)
                FoxRunEncodingResolver.ValidateProfileDefault(encoding.Value);
            Encoding = encoding;
        }

        public FoxRunFlow Mode { get; }
        public IReadOnlyList<string> PublishTransportIds =>
            _publishTransportIds == null
                ? null
                : Array.AsReadOnly((string[])_publishTransportIds.Clone());
        public FoxRunTransportId? SubscribeTransportId { get; }
        public FoxRunEncoding? Encoding { get; }

        public FoxRunResolvedTransportTopology Resolve(
            FoxRunTransportSelection inherited,
            FoxRunEncoding publishDefaultEncoding,
            FoxRunEncoding subscribeDefaultEncoding)
        {
            if (inherited == null)
                throw new ArgumentNullException(nameof(inherited));

            var publishes = Mode == FoxRunFlow.Publish
                            || Mode == FoxRunFlow.PublishAndSubscribe;
            var subscribes = Mode == FoxRunFlow.Subscribe
                             || Mode == FoxRunFlow.PublishAndSubscribe;
            var publishIds = publishes
                ? _publishTransportIds == null
                    ? inherited.PublishTransportIds.ToArray()
                    : _publishTransportIds.Select(value => new FoxRunTransportId(value)).ToArray()
                : Array.Empty<FoxRunTransportId>();
            FoxRunTransportId? subscribeId = null;
            if (subscribes)
            {
                if (!inherited.SubscriptionsEnabled && !SubscribeTransportId.HasValue)
                    throw new InvalidOperationException(
                        "The inherited Manager selection has subscriptions disabled.");
                subscribeId = SubscribeTransportId ?? inherited.SubscribeTransportId;
                if (!subscribeId.HasValue)
                    throw new InvalidOperationException(
                        "A subscribe direction requires exactly one transport ID.");
            }

            var foxglovePublish = publishIds.Any(
                id => id == FoxgloveWebSocketTransport.TransportId);
            var foxgloveSubscribe =
                subscribeId == FoxgloveWebSocketTransport.TransportId;
            if (Encoding.HasValue && !foxglovePublish && !foxgloveSubscribe)
            {
                throw new InvalidOperationException(
                    "FoxRun Encoding requires an effective Foxglove WebSocket direction.");
            }

            var publishEncoding = foxglovePublish
                ? Encoding
                  ?? FoxRunEncodingResolver.ValidateProfileDefault(
                      publishDefaultEncoding)
                : (FoxRunEncoding)0;
            var subscribeEncoding = foxgloveSubscribe
                ? Encoding
                  ?? FoxRunEncodingResolver.ValidateProfileDefault(
                      subscribeDefaultEncoding)
                : (FoxRunEncoding)0;
            return new FoxRunResolvedTransportTopology(
                Mode,
                publishIds,
                subscribeId,
                publishEncoding,
                subscribeEncoding);
        }

        private static string[] Canonicalize(
            IEnumerable<string> values,
            string parameter)
        {
            var ids = values.Select(value => new FoxRunTransportId(value)).ToArray();
            if (ids.Distinct().Count() != ids.Length)
                throw new ArgumentException(
                    "Publish transport IDs must be unique.",
                    parameter);
            Array.Sort(
                ids,
                (left, right) => string.CompareOrdinal(left.Value, right.Value));
            return ids.Select(id => id.Value).ToArray();
        }
    }

    public sealed class FoxRunResolvedTransportTopology
    {
        private readonly IReadOnlyList<FoxRunTransportId> _publishTransportIds;

        internal FoxRunResolvedTransportTopology(
            FoxRunFlow mode,
            FoxRunTransportId[] publishTransportIds,
            FoxRunTransportId? subscribeTransportId,
            FoxRunEncoding publishEncoding,
            FoxRunEncoding subscribeEncoding)
        {
            Mode = mode;
            _publishTransportIds = Array.AsReadOnly(
                (FoxRunTransportId[])publishTransportIds.Clone());
            SubscribeTransportId = subscribeTransportId;
            PublishEncoding = publishEncoding;
            SubscribeEncoding = subscribeEncoding;
            DeterministicKey = BuildKey();
            DeterministicHash = Sha256(DeterministicKey);
        }

        public FoxRunFlow Mode { get; }
        public IReadOnlyList<FoxRunTransportId> PublishTransportIds =>
            _publishTransportIds;
        public FoxRunTransportId? SubscribeTransportId { get; }
        public FoxRunEncoding PublishEncoding { get; }
        public FoxRunEncoding SubscribeEncoding { get; }
        public string DeterministicKey { get; }
        public string DeterministicHash { get; }

        private string BuildKey()
            => ((int)Mode).ToString(System.Globalization.CultureInfo.InvariantCulture)
               + "|"
               + string.Join(",", _publishTransportIds.Select(id => id.Value))
               + "|"
               + (SubscribeTransportId?.Value ?? string.Empty)
               + "|"
               + ((int)PublishEncoding).ToString(
                   System.Globalization.CultureInfo.InvariantCulture)
               + "|"
               + ((int)SubscribeEncoding).ToString(
                   System.Globalization.CultureInfo.InvariantCulture);

        private static string Sha256(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var result = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                result[i * 2] = alphabet[bytes[i] >> 4];
                result[i * 2 + 1] = alphabet[bytes[i] & 0x0f];
            }

            return new string(result);
        }
    }
}
