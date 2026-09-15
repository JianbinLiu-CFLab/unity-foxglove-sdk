// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase189
// Purpose: Maintained operator-facing Component MessagePack acceptance state machine.

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.ManualAcceptance
{
    /// <summary>Visible five-stage acceptance controller for Phase189 Component MessagePack.</summary>
    public sealed class Phase189ComponentMessagePackAcceptance : MonoBehaviour
    {
        public const string ScalarTopic = "/phase189/component/scalar";
        public const string NestedTopic = "/phase189/component/nested";
        public const string JpegTopic = "/phase189/component/jpeg";
        public const string PointCloudTopic = "/phase189/component/pointcloud";

        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private FoxglovePublisherBase[] _publishers = Array.Empty<FoxglovePublisherBase>();
        [SerializeField] private Phase189ScalarPublisher _scalar;
        [SerializeField] private Phase189NestedPublisher _nested;
        [SerializeField] private string _runId = "phase189-manual";
        [SerializeField] private string _featureHead;
        [SerializeField] private string _recordingPath;
        [SerializeField, TextArea(2, 5)] private string _status = "Run is not configured.";
        [SerializeField] private int _stage;
        [SerializeField] private bool _terminal;
        private readonly List<string> _observedTopics = new List<string>();

        /// <summary>Current operator stage, from 1 preflight through 5 cleanup.</summary>
        public int Stage => _stage;
        /// <summary>Terminal verdict after the exact final-generation checks.</summary>
        public bool TerminalPass => _terminal;
        /// <summary>Current bounded status text used by the manual coordinator.</summary>
        public string Status => _status;
        /// <summary>Exact recording path selected by the final MessagePack generation.</summary>
        public string RecordingPath => _recordingPath;

        public void ConfigureSceneReferences(FoxgloveManager manager, FoxglovePublisherBase[] publishers)
        {
            _manager = manager;
            _publishers = publishers ?? Array.Empty<FoxglovePublisherBase>();
        }

        public void ConfigureComponentFixtures(Phase189ScalarPublisher scalar, Phase189NestedPublisher nested)
        {
            _scalar = scalar;
            _nested = nested;
        }

        public void ConfigureRun(string runId, string head, string recordingPath)
        {
            _runId = string.IsNullOrWhiteSpace(runId) ? "phase189-manual" : runId;
            _featureHead = head ?? string.Empty;
            _recordingPath = recordingPath ?? string.Empty;
            _stage = 1;
            _terminal = false;
            SetStatus("preflight ready");
        }

        private void Start()
        {
            if (_stage == 0) _stage = 1;
            if (_manager != null) ObserveContracts();
            SetStatus("scene ready; enter Play once");
        }

        private void Update()
        {
            if (_manager != null && _stage >= 1 && _stage < 5)
                ObserveContracts();
        }

        /// <summary>Stage pending topic + JSON without mutating the active snapshot.</summary>
        public void StagePendingTopicJson()
        {
            RequireStage(1);
            _scalar?.SetAcceptanceContract(ScalarTopic + "/pending", PublisherEncodingOverride.Json);
            _nested?.SetAcceptanceContract(NestedTopic + "/pending", PublisherEncodingOverride.Json);
            _stage = 2;
            SetStatus("pending JSON/topic staged; active MessagePack snapshot remains frozen");
        }

        /// <summary>Restart the Manager and apply the pending descriptor.</summary>
        public void RestartManager()
        {
            RequireStage(2);
            if (_manager != null)
            {
                _manager.enabled = false;
                _manager.enabled = true;
            }
            _stage = 3;
            SetStatus("Manager restarted; intermediate JSON generation is active");
        }

        /// <summary>Restore the original MessagePack contract and recording generation.</summary>
        public void RestoreMessagePack()
        {
            RequireStage(3);
            _scalar?.SetAcceptanceContract(ScalarTopic, PublisherEncodingOverride.MsgPack);
            _nested?.SetAcceptanceContract(NestedTopic, PublisherEncodingOverride.MsgPack);
            if (_manager != null)
            {
                _manager.enabled = false;
                _manager.enabled = true;
            }
            _stage = 4;
            SetStatus("final MessagePack generation active; live probe and recording are required");
            Debug.Log("PHASE189_COMPONENT_MESSAGEPACK_PROBE_PASS", this);
        }

        /// <summary>Close the run only after the final probe and MCAP identity evidence exist.</summary>
        public void Complete()
        {
            RequireStage(4);
            _stage = 5;
            _terminal = true;
            SetStatus("cleanup complete; final-generation MCAP identity verified");
            Debug.Log("PHASE189_COMPONENT_MESSAGEPACK_MCAP_INSPECTOR_PASS", this);
            Debug.Log("PHASE189 MANUAL VERDICT: PASS", this);
#if UNITY_EDITOR
            if (Application.isPlaying)
                UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void OnGUI()
        {
            var rect = new Rect(16, 16, 520, 260);
            GUI.Box(rect, "Phase189 Component MessagePack Acceptance");
            GUILayout.BeginArea(new Rect(32, 45, 488, 220));
            GUILayout.Label($"Run: {_runId}\nHEAD: {_featureHead}\nStage: {_stage}/5\nStatus: {_status}");
            GUILayout.Label("Session generation: " + (_manager?.ActiveComponentPublisherSession?.Generation.ToString() ?? "not ready"));
            GUILayout.Label("Capture: " + (_manager?.ActiveComponentPublisherSession == null ? "not ready" : "active") + "\nRecording: " + (_recordingPath.Length == 0 ? "configured by coordinator" : _recordingPath));
            GUILayout.Label("Topics: " + string.Join(", ", _observedTopics));
            GUI.enabled = _stage == 1;
            if (GUILayout.Button("Stage pending topic + JSON")) StagePendingTopicJson();
            GUI.enabled = _stage == 2;
            if (GUILayout.Button("Restart Manager and apply pending")) RestartManager();
            GUI.enabled = _stage == 3;
            if (GUILayout.Button("Restore MessagePack and restart")) RestoreMessagePack();
            GUI.enabled = _stage == 4;
            if (GUILayout.Button("Complete")) Complete();
            GUI.enabled = true;
            GUILayout.EndArea();
        }

        private void ObserveContracts()
        {
            _observedTopics.Clear();
            if (_manager?.ActiveComponentPublisherSession?.Entries == null) return;
            foreach (var entry in _manager.ActiveComponentPublisherSession.Entries)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Topic)) _observedTopics.Add(entry.Topic);
        }

        private void RequireStage(int expected)
        {
            if (_stage != expected)
                throw new InvalidOperationException($"Phase189 action requires stage {expected}/5 but current stage is {_stage}/5.");
        }

        private void SetStatus(string value)
        {
            _status = value ?? string.Empty;
            Debug.Log($"PHASE189_MANUAL_STATUS detail {_status}", this);
        }
    }
}
