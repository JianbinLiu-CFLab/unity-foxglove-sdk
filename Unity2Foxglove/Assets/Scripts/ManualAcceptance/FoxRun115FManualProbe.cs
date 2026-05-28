// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/FoxRun115F
// Purpose: Temporary Unity-side probe for FoxRun generation-model hardening.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

/// <summary>
/// Manual probe for verifying that FoxRun source generation handles emission
/// type names that differ from raw host-observed type names.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to an enabled GameObject in a scene that also has a
///    running <see cref="FoxgloveManager"/> and FoxRun logging enabled.
/// 2. Enter Play Mode and connect Foxglove to <c>ws://127.0.0.1:8765</c>.
/// 3. Confirm that the <c>/debug/115f/...</c> topics appear and publish JSON
///    values for scalar, string, array, list, nullable, and Unity vector types.
///    The nested-object member is kept below as a commented negative diagnostic
///    probe; uncomment its FoxRun attribute only when verifying FOXRUN006.
/// 4. Toggle <c>Publish Null Optional</c> during Play Mode and confirm the
///    nullable topic alternates between a number and null without compile or
///    runtime errors.
/// 5. Regenerate schema evidence. The probe should exercise source generation
///    and descriptor evidence, but it should not require any manual codegen
///    path outside the normal FoxRun workflow.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/FoxRun 115F Manual Probe")]
public partial class FoxRun115FManualProbe : MonoBehaviour
{
    [Serializable]
    public sealed class NestedPayload
    {
        public int count;
        public string label = "initialized";
        public Vector3 offset;
    }

    [Header("Probe Controls")]
    [Tooltip("When enabled, Update mutates every published value once per frame.")]
    [SerializeField] private bool _mutateValues = true;
    [Tooltip("When enabled, the nullable value publishes null instead of the current frame count.")]
    [SerializeField] private bool _publishNullOptional;
    [Tooltip("Last local probe status, visible in the Inspector during Play Mode.")]
    [SerializeField] private string _lastStatus = "Not started.";

    [Header("FoxRun Values")]
    [FoxRun("/debug/115f/scalar", RateHz = 2f)]
    public float scalarValue;

    [FoxRun("/debug/115f/string", RateHz = 2f)]
    public string textValue = "hello FoxRun user";

    [FoxRun("/debug/115f/vector", RateHz = 2f)]
    public Vector3 vectorValue;

    [FoxRun("/debug/115f/array", RateHz = 2f)]
    public float[] sampleArray = new float[] { 0f, 1f, 2f };

    [FoxRun("/debug/115f/list", RateHz = 2f)]
    public List<float> sampleList = new List<float> { 0f, 1f, 2f };

    [FoxRun("/debug/115f/nullable", RateHz = 2f)]
    public int? optionalCount = 0;

    // Test from Phase136, test for FOXRUN006 message
    // [FoxRun("/test/native", RateHz = 10f)]
    // public NativeArray<float> _testNative;

    // Negative diagnostic probe: uncomment this attribute to verify that
    // FOXRUN006 rejects non-canonical custom object payloads as an error.
    // [FoxRun("/debug/115f/nested", PublishMode = FoxRunPublishMode.OnTrigger)]
    public NestedPayload nestedPayload = new NestedPayload();

    private int _frameCount;

    /// <summary>
    /// Initializes probe values when the component is first added or reset.
    /// </summary>
    private void Reset()
    {
        EnsureCollections();
        RandomizeProbeValues();
    }

    /// <summary>
    /// Ensures list, array, and nested payload references exist before Play Mode.
    /// </summary>
    private void Awake()
    {
        EnsureCollections();
    }

    /// <summary>
    /// Keeps the probe values moving so the generated FoxRun publishers have
    /// fresh payloads to publish while the scene is running.
    /// </summary>
    private void Update()
    {
        if (!_mutateValues)
            return;

        _frameCount++;
        var t = Time.time;
        scalarValue = Mathf.Sin(t);
        textValue = "frame " + _frameCount;
        vectorValue = transform.position + new Vector3(Mathf.Sin(t), Mathf.Cos(t), t % 5f);

        sampleArray[0] = scalarValue;
        sampleArray[1] = vectorValue.x;
        sampleArray[2] = vectorValue.z;

        sampleList[0] = vectorValue.y;
        sampleList[1] = scalarValue * 10f;
        sampleList[2] = _frameCount;

        optionalCount = _publishNullOptional ? (int?)null : _frameCount;
        nestedPayload.count = _frameCount;
        nestedPayload.label = _publishNullOptional ? "nullable:null" : "nullable:value";
        nestedPayload.offset = vectorValue;

        _lastStatus = "Updated frame " + _frameCount;
    }

    /// <summary>
    /// Randomizes all probe values once from the Inspector context menu.
    /// </summary>
    [ContextMenu("Randomize Probe Values")]
    public void RandomizeProbeValues()
    {
        EnsureCollections();

        scalarValue = UnityEngine.Random.Range(-1f, 1f);
        textValue = "random " + UnityEngine.Random.Range(0, 1000);
        vectorValue = new Vector3(
            UnityEngine.Random.Range(-2f, 2f),
            UnityEngine.Random.Range(-2f, 2f),
            UnityEngine.Random.Range(-2f, 2f));

        for (var i = 0; i < sampleArray.Length; i++)
            sampleArray[i] = UnityEngine.Random.Range(-10f, 10f);

        for (var i = 0; i < sampleList.Count; i++)
            sampleList[i] = UnityEngine.Random.Range(-10f, 10f);

        optionalCount = _publishNullOptional ? (int?)null : UnityEngine.Random.Range(0, 1000);
        nestedPayload.count = optionalCount ?? -1;
        nestedPayload.label = "randomized";
        nestedPayload.offset = vectorValue;

        _lastStatus = "Randomized probe values.";
    }

    /// <summary>
    /// Restores deterministic values from the Inspector context menu.
    /// </summary>
    [ContextMenu("Reset Probe Values")]
    public void ResetProbeValues()
    {
        EnsureCollections();

        _frameCount = 0;
        scalarValue = 0f;
        textValue = "hello 115F";
        vectorValue = Vector3.zero;
        sampleArray[0] = 0f;
        sampleArray[1] = 1f;
        sampleArray[2] = 2f;
        sampleList[0] = 0f;
        sampleList[1] = 1f;
        sampleList[2] = 2f;
        optionalCount = 0;
        nestedPayload.count = 0;
        nestedPayload.label = "reset";
        nestedPayload.offset = Vector3.zero;

        _lastStatus = "Reset probe values.";
    }

    /// <summary>
    /// Allocates missing mutable containers and normalizes them to length three.
    /// </summary>
    private void EnsureCollections()
    {
        if (sampleArray == null || sampleArray.Length != 3)
            sampleArray = new float[] { 0f, 1f, 2f };

        if (sampleList == null)
            sampleList = new List<float>();

        while (sampleList.Count < 3)
            sampleList.Add(sampleList.Count);

        while (sampleList.Count > 3)
            sampleList.RemoveAt(sampleList.Count - 1);

        if (nestedPayload == null)
            nestedPayload = new NestedPayload();
    }
}
