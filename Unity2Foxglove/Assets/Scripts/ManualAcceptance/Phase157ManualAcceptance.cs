// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase157 FoxRun inbound and local FoxService acceptance probe.

using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Manual Unity/Foxglove acceptance probe for Phase157.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to an enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. In FoxgloveManager > Connection &amp; Security, enable FoxRun Inbound.
///    Keep the server on a loopback endpoint for this acceptance.
/// 3. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 4. Client-publish JSON {"requestedTargetSpeed":3.5} on
///    /phase157/target-speed and confirm Applied Target Speed becomes 3.5.
/// 5. Client-publish JSON {"sharedState":7} on /phase157/shared-state and
///    confirm Shared State becomes 7 without an immediate duplicate echo.
/// 6. Confirm Local Service Passed is checked and the Console reports the
///    successful /phase157/apply-command local call.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase157 FoxRun Inbound and Service Smoke")]
public sealed partial class Phase157ManualAcceptance : MonoBehaviour
{
    private const string ApplyCommandService = "/phase157/apply-command";

    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Seconds to wait for the generated FoxService registration before failing the local-call check.")]
    [SerializeField] private float serviceTimeoutSeconds = 12f;

    [Header("Inbound Command Buffer")]
    [Tooltip("Remote-authoritative command buffer. Publish {\"requestedTargetSpeed\":3.5} to /phase157/target-speed.")]
    [FoxRun("/phase157/target-speed", Mode = FoxRunFlow.Subscribe)]
    [SerializeField] private float requestedTargetSpeed;
    [Tooltip("Validated state applied from Requested Target Speed during Update.")]
    [FoxRun("/phase157/applied-speed", Hz = 2f)]
    [SerializeField] private float appliedTargetSpeed;

    [Header("Bidirectional State")]
    [Tooltip("Low-frequency shared observed state used to verify PublishAndSubscribe echo suppression; not a closed-loop control command.")]
#pragma warning disable FOXRUN400 // Acceptance intentionally models shared observed state with explicit bidirectional ownership.
    [FoxRun("/phase157/shared-state", Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunEncoding.JSON, Hz = 2f)]
    [SerializeField] private float sharedState;
#pragma warning restore FOXRUN400

    [Header("Observed State")]
    [Tooltip("Number of accepted command-buffer changes applied by this component.")]
    [SerializeField] private int appliedCommandCount;
    [Tooltip("True after the existing FoxServiceHub successfully handles the local Phase157 service call.")]
#pragma warning disable CS0414 // Manual acceptance state is observed in the Unity Inspector.
    [SerializeField] private bool localServicePassed;
#pragma warning restore CS0414
    [Tooltip("Most recent local service response or failure diagnostic.")]
    [SerializeField] private string localServiceStatus;
    [Tooltip("Last acceptance status written by this component.")]
    [SerializeField] private string lastStatus;

    private float lastRequestedTargetSpeed;

    private void Awake()
    {
        if (manager == null)
            manager = FindFirstObjectByType<FoxgloveManager>();
        lastRequestedTargetSpeed = requestedTargetSpeed;
    }

    private void OnEnable()
    {
        if (this is IFoxgloveLogSource logSource)
            FoxgloveLogHub.RegisterSource(logSource);
        if (this is IFoxgloveServiceSource serviceSource)
            FoxgloveServiceHub.RegisterSource(serviceSource);
    }

    private void OnDisable()
    {
        if (this is IFoxgloveLogSource logSource)
            FoxgloveLogHub.UnregisterSource(logSource);
        if (this is IFoxgloveServiceSource serviceSource)
            FoxgloveServiceHub.UnregisterSource(serviceSource);
    }

    private IEnumerator Start()
    {
        if (manager == null)
        {
            Fail("FoxgloveManager was not found.");
            yield break;
        }

        if (!manager.EnableFoxRunInbound)
        {
            Fail("Enable FoxRun Inbound in FoxgloveManager > Connection & Security, then restart Play Mode.");
            yield break;
        }

        var deadline = Time.realtimeSinceStartup + Mathf.Max(1f, serviceTimeoutSeconds);
        while (Time.realtimeSinceStartup < deadline)
        {
            if (FoxgloveServiceHub.TryGetActive(out var hub))
            {
                var result = hub.CallLocal(
                    ApplyCommandService,
                    JObject.FromObject(new ApplyCommandRequest { targetSpeed = 2.5f }),
                    TimeSpan.FromSeconds(1));
                if (result.Status == FoxgloveLocalServiceCallStatus.Succeeded)
                {
                    localServicePassed = true;
                    localServiceStatus = result.Response?.ToString() ?? "null";
                    lastStatus = "Local service passed; publish both Phase157 inbound topics from Foxglove.";
                    Debug.Log("[Phase157] Local FoxService call passed: " + localServiceStatus);
                    yield break;
                }

                if (result.Status != FoxgloveLocalServiceCallStatus.MissingService)
                {
                    Fail("Local service call failed: " + result.Status + " " + result.Error);
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(0.25f);
        }

        Fail("Generated /phase157/apply-command service was not registered before timeout.");
    }

    private void Update()
    {
        if (Mathf.Approximately(requestedTargetSpeed, lastRequestedTargetSpeed))
            return;

        lastRequestedTargetSpeed = requestedTargetSpeed;
        appliedTargetSpeed = Mathf.Clamp(requestedTargetSpeed, 0f, 10f);
        appliedCommandCount++;
        lastStatus = "Applied inbound target speed " + appliedTargetSpeed.ToString("R") + ".";
        Debug.Log("[Phase157] " + lastStatus);
    }

    [FoxService(
        ApplyCommandService,
        Type = "ManualAcceptance.Phase157.ApplyCommand",
        Description = "Apply a bounded Phase157 target-speed command.",
        RequestSchemaName = "ManualAcceptance.Phase157.ApplyCommandRequest",
        ResponseSchemaName = "ManualAcceptance.Phase157.ApplyCommandResponse")]
    private ApplyCommandResponse ApplyCommand(ApplyCommandRequest request)
    {
        requestedTargetSpeed = request != null ? request.targetSpeed : 0f;
        return new ApplyCommandResponse
        {
            accepted = true,
            appliedTargetSpeed = Mathf.Clamp(requestedTargetSpeed, 0f, 10f)
        };
    }

    private void Fail(string message)
    {
        lastStatus = message;
        Debug.LogError("[Phase157] " + message);
    }

    [Serializable]
    public sealed class ApplyCommandRequest
    {
        public float targetSpeed;
    }

    [Serializable]
    public sealed class ApplyCommandResponse
    {
        public bool accepted;
        public float appliedTargetSpeed;
    }
}
