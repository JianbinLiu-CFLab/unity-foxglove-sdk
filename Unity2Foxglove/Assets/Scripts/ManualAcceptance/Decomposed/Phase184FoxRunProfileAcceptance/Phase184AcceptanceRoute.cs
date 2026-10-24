// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Case-isolated Unity evidence surface for Phase184 profile acceptance.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Unity2Foxglove.Ros2Bridge;
using Debug = UnityEngine.Debug;

namespace Unity2Foxglove.ManualAcceptance
{
    using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures;

    public abstract class Phase184AcceptanceRoute : MonoBehaviour
    {
        private const int MaximumMarkerCount = 64;

        [Header("Read-only Route Evidence")]
        [SerializeField] private string _caseId = string.Empty;
        [SerializeField] private string _status = "Inactive";
        [SerializeField] private int _emittedMarkers;
        [SerializeField] private bool _terminal;
        [SerializeField] private bool _passed;

        private readonly HashSet<string> _markerKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private Phase184AcceptanceRunContext _context;
        private bool _armed;

        protected string RunToken => _context.Token;
        protected int NegativeSeconds => _context.NegativeSeconds;
        protected bool IsArmed => _armed;
        protected bool IsTerminal => _terminal;
        public bool Passed => _passed;
        protected abstract string RouteCaseId { get; }

        internal void Arm(Phase184AcceptanceRunContext context)
        {
            _context = context;
            _caseId = context.CaseId;
            _status = "Armed";
            _terminal = false;
            _passed = false;
            _emittedMarkers = 0;
            _markerKeys.Clear();
            _armed = true;
        }

        protected void Ready(string fields)
        {
            _status = "Ready";
            Emit("PHASE184G_ROUTE_READY", fields);
        }

        protected void Pass(string fields)
        {
            if (_terminal)
                return;
            _terminal = true;
            _passed = true;
            _status = "PASS";
            Emit("PHASE184G_CASE_PASS", fields);
        }

        protected void Fail(string reason)
        {
            if (_terminal)
                return;
            _terminal = true;
            _passed = false;
            _status = Phase184AcceptanceText.Bound(reason, 512);
            Emit(
                "PHASE184G_CASE_FAIL",
                "reason=" + Phase184AcceptanceText.SafeMarker(_status));
        }

        protected void Emit(string marker, string fields)
        {
            if (!_armed || _emittedMarkers >= MaximumMarkerCount)
                return;

            var bounded = Phase184AcceptanceText.Bound(fields, 512);
            var line = marker
                       + " case=" + RouteCaseId
                       + " token=" + Phase184AcceptanceText.SafeMarker(RunToken)
                       + (string.IsNullOrEmpty(bounded) ? string.Empty : " " + bounded);
            if (!_markerKeys.Add(line))
                return;
            _emittedMarkers++;
            Debug.Log(line, this);
        }

        protected static Phase181State State(string token, string stage, int count)
        {
            var label = token + "-" + stage;
            return new Phase181State
            {
                Count = count,
                Kind = Phase181StateKind.Active,
                Message = label,
                Bytes = new byte[] { 0x18, 0x04, (byte)(count & 0xff) },
                Values = new List<long> { count, count + 1L, count + 2L },
                Nested = new Phase181NestedState { Enabled = true, Label = label },
                OptionalCount = count,
                OptionalText = label,
            };
        }

        protected bool IsState(Phase181State value, string stage)
            => value != null
               && value.Kind == Phase181StateKind.Active
               && string.Equals(
                   value.Message,
                   RunToken + "-" + stage,
                   StringComparison.Ordinal);

        protected bool HasActivePublishTransport(string transportId)
        {
            var manager = FindFirstObjectByType<FoxgloveManager>();
            var snapshot = manager?.ActiveFoxRunTransportSession;
            if (snapshot == null)
                return false;
            try
            {
                return snapshot.TryGetPublishTransport(
                    new FoxRunTransportId(transportId),
                    out _);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        protected bool HasActivePublishTransports(
            params string[] transportIds)
        {
            foreach (var id in transportIds)
            {
                if (!HasActivePublishTransport(id))
                    return false;
            }
            return true;
        }
    }

}
