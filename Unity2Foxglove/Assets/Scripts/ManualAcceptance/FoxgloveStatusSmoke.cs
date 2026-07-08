// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/ManualStatusSmoke
// Purpose: Provides a small manual smoke test for Foxglove status and
// removeStatus WebSocket messages in the Problems panel.

using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
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
    /// Handles manual keyboard shortcuts using the active Unity input backend.
    /// F7 publishes a warning status; F8 removes the same status immediately.
    /// </summary>
    private void Update()
    {
        if (manager == null || !manager.IsRunning)
            return;

        if (WasKeyPressed(KeyCode.F7))
            PublishAndAutoClear();

        if (WasKeyPressed(KeyCode.F8))
            ClearStatus();
    }

    private void OnDisable()
    {
        StopAutoClearRoutine();
    }

    private static bool WasKeyPressed(KeyCode legacyKey)
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        return legacyKey switch
        {
            KeyCode.F7 => keyboard.f7Key.wasPressedThisFrame,
            KeyCode.F8 => keyboard.f8Key.wasPressedThisFrame,
            _ => false
        };
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(legacyKey);
#else
        return false;
#endif
    }

    /// <summary>
    /// Publishes the smoke warning and schedules automatic removal.
    /// </summary>
    private void PublishAndAutoClear()
    {
        manager.PublishWarningStatus(StatusMessage, StatusId);
        Debug.Log("[FoxgloveStatusSmoke] Published status. It will auto-clear soon.");

        StopAutoClearRoutine();
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

    private void StopAutoClearRoutine()
    {
        if (autoClearRoutine == null)
            return;

        StopCoroutine(autoClearRoutine);
        autoClearRoutine = null;
    }
}
