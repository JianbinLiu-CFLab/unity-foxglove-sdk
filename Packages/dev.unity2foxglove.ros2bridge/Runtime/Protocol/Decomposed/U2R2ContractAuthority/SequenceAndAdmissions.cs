// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Strict U2R2 contract parsing, readiness, sequencing, and removal.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{


    public enum U2R2MessageAdmission
    {
        Accepted = 1,
        LateTombstone = 2,
    }

    public sealed class U2R2ContractSequence
    {
        private ulong _lastAccepted;

        public U2R2ContractSequence(ulong startingSequence = 0)
        {
            _lastAccepted = startingSequence;
        }

        public ulong LastAccepted => _lastAccepted;
        public bool IsFaulted { get; private set; }

        public void Admit(ulong sequence)
        {
            if (IsFaulted)
            {
                throw new U2R2ProtocolException(
                    "contract_sequence_fault",
                    "The U2R2 contract sequence is already faulted.",
                    terminal: false);
            }
            if (_lastAccepted == ulong.MaxValue)
            {
                IsFaulted = true;
                throw new U2R2ProtocolException(
                    "contract_sequence_exhausted",
                    "The U2R2 contract sequence exhausted before wrap.",
                    terminal: false);
            }
            if (sequence != _lastAccepted + 1)
            {
                IsFaulted = true;
                throw new U2R2ProtocolException(
                    "contract_sequence_fault",
                    "The U2R2 contract sequence is not strictly monotonic.",
                    terminal: false);
            }
            _lastAccepted = sequence;
        }
    }

    public sealed class U2R2RegistrationAdmission : IDisposable
    {
        internal U2R2RegistrationAdmission(
            U2R2ContractAuthority owner,
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            Owner = owner;
            Identity = identity;
            Scheduler = scheduler;
            Replay = replay;
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public bool Replayed { get; internal set; }
        internal U2R2ContractAuthority Owner { get; }
        internal U2R2ContractIdentity Identity { get; }
        internal U2R2BoundedOutboundScheduler Scheduler { get; }
        internal U2R2RequestReplayAuthority Replay { get; }
        internal U2R2ReplayAdmission Response { get; }
        internal ulong ResponseRequestId => Response.RequestId;
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.AbandonRegistration(this);
    }

    public sealed class U2R2RemovalAdmission : IDisposable
    {
        internal U2R2RemovalAdmission(
            U2R2ContractAuthority owner,
            U2R2ContractIdentity identity,
            U2R2BoundedOutboundScheduler scheduler,
            U2R2RequestReplayAuthority replay,
            U2R2ReplayAdmission response)
        {
            Owner = owner;
            Identity = identity;
            Scheduler = scheduler;
            Replay = replay;
            Response = response ?? throw new ArgumentNullException(nameof(response));
        }

        public bool Replayed { get; internal set; }
        internal U2R2ContractAuthority Owner { get; }
        internal U2R2ContractIdentity Identity { get; }
        internal U2R2BoundedOutboundScheduler Scheduler { get; }
        internal U2R2RequestReplayAuthority Replay { get; }
        internal U2R2ReplayAdmission Response { get; }
        internal ulong ResponseRequestId => Response.RequestId;
        internal bool IsSettled { get; set; }

        public void Dispose()
            => Owner.AbandonRemoval(this);
    }
}
