// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/ManualStatusSmoke
// Purpose: Provides a small manual smoke test for Foxglove status and
// removeStatus WebSocket messages in the Problems panel.

using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Publishes a temporary warning status to Foxglove and clears it again.
/// Attach this component to any scene object and assign the active
/// <see cref="FoxgloveManager"/>, or leave the field empty for auto-discovery.
/// </summary>
public class FoxgloveStatusSmoke : MonoBehaviour
{
    /// <summary>
    /// Stable Foxglove status id used by the manual smoke entry.
    /// </summary>
    private const string StatusId = "manual/status-smoke";

    /// <summary>
    /// Human-readable message shown in Foxglove's Problems panel.
    /// </summary>
    private const string StatusMessage = "Manual status smoke test";

    /// <summary>
    /// Active manager that owns the WebSocket server connection.
    /// </summary>
    [SerializeField] private FoxgloveManager manager;

    /// <summary>
    /// Delay before the smoke status is removed after pressing F7.
    /// </summary>
    [SerializeField] private float autoClearSeconds = 3f;

    /// <summary>
    /// Tracks the pending auto-clear coroutine so repeated F7 presses restart
    /// the timer instead of scheduling duplicate removals.
    /// </summary>
    private Coroutine autoClearRoutine;

    /// <summary>
    /// Finds the scene's Foxglove manager when the field was not assigned in
    /// the Inspector.
    /// </summary>
    private void Awake()
    {
        if (manager == null)
            manager = FindFirstObjectByType<FoxgloveManager>();
    }

    /// <summary>
    /// Handles manual keyboard shortcuts using Unity's Input System package.
    /// F7 publishes a warning status; F8 removes the same status immediately.
    /// </summary>
    private void Update()
    {
        if (Keyboard.current == null || manager == null || !manager.IsRunning)
            return;

        if (Keyboard.current.f7Key.wasPressedThisFrame)
            PublishAndAutoClear();

        if (Keyboard.current.f8Key.wasPressedThisFrame)
            ClearStatus();
    }

    /// <summary>
    /// Publishes the smoke warning and schedules automatic removal.
    /// </summary>
    private void PublishAndAutoClear()
    {
        manager.PublishWarningStatus(StatusMessage, StatusId);
        Debug.Log("[FoxgloveStatusSmoke] Published status. It will auto-clear soon.");

        if (autoClearRoutine != null)
            StopCoroutine(autoClearRoutine);
        autoClearRoutine = StartCoroutine(AutoClearStatus());
    }

    /// <summary>
    /// Waits for the configured delay and then sends removeStatus.
    /// </summary>
    private IEnumerator AutoClearStatus()
    {
        yield return new WaitForSeconds(autoClearSeconds);
        ClearStatus();
        autoClearRoutine = null;
    }

    /// <summary>
    /// Removes the smoke status from Foxglove's Problems panel.
    /// </summary>
    private void ClearStatus()
    {
        manager.RemoveStatus(StatusId);
        Debug.Log("[FoxgloveStatusSmoke] Requested status removal.");
    }
}

