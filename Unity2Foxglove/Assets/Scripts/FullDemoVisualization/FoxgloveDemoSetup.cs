// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FullDemoVisualization
// Purpose: Registers demo Parameters, Services, and wires Foxglove client message logging for manual verification.

using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;

/// <summary>
/// Registers demo Parameters and Services for Phase 7 manual verification.
/// Attach to the Foxglove GameObject (same one with FoxgloveManager).
/// </summary>
public class FoxgloveDemoSetup : MonoBehaviour
{
    internal const float ScaleMinimum = 0.2f;
    internal const float ScaleMaximum = 5f;
    private const int ClientPayloadPreviewBytes = 160;
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    [SerializeField] private FoxgloveManager _manager;
    [SerializeField] private GameObject _cube;

    private uint _resetSvcId;
    private float _lastAppliedScale = -1f;
    private Color _lastAppliedColor = Color.clear;
    private bool _syncingColor;
    private bool _initialized;
    private bool _warnedWaitingForManager;
    private bool _warnedInvalidScale;
    private SynchronizationContext _unityContext;
    private FoxgloveSceneCubePublisher _scenePublisher;
    private GameObject _cachedCube;

    /// <summary>
    /// Initializes parameters <c>/cube/color</c> and <c>/cube/scale</c>,
    /// registers the <c>/cube/reset_pose</c> service, hooks up
    /// client-message logging, and wires parameter-change callbacks.
    /// </summary>
    private void Start()
    {
        _unityContext = SynchronizationContext.Current;
        TryInitializeDemo();
    }

    private bool TryInitializeDemo()
    {
        if (_manager == null)
            _manager = GetComponent<FoxgloveManager>();

        var runtime = _manager?.Runtime;
        if (runtime?.Session == null)
        {
            if (_initialized)
                ClearRuntimeWiring();
            if (!_warnedWaitingForManager)
            {
                _warnedWaitingForManager = true;
                Debug.LogWarning("[FoxgloveDemo] Waiting for FoxgloveManager before registering demo parameters and services.");
            }

            return false;
        }

        if (_initialized)
            return true;

        var rt = runtime;

        rt.RegisterParameter("/cube/color", new JArray(0.0, 1.0, 0.0, 1.0), "number[]", true);
        rt.RegisterParameter("/cube/scale", 1.0, "number", true);

        _resetSvcId = _manager.RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor
        {
            Name = "/cube/reset_pose",
            Type = "/cube/reset_pose",
            Request = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseRequest" },
            Response = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseResponse" }
        }, req =>
        {
            var cube = FindCube();
            if (cube == null)
                return JToken.Parse("{\"status\":\"error\",\"reason\":\"cube not found\"}");

            if (cube != null)
            {
                cube.transform.position = Vector3.zero;
                cube.transform.rotation = Quaternion.identity;
                cube.transform.localScale = Vector3.one;
                _lastAppliedScale = 1f;

                _manager.Runtime?.TrySetParameter("/cube/color", new JArray(0.0, 1.0, 0.0, 1.0));
                _manager.Runtime?.TrySetParameter("/cube/scale", JToken.FromObject(1.0));
            }
            return JToken.Parse("{\"status\":\"ok\"}");
        });

        // Phase 8: log client-published messages to Unity Console.
        _manager.OnClientMessage += OnClientMessageReceived;

        // Advertise /unity/client_log so Foxglove sees foxglove.Log in the schema picker.
        _manager.GetOrRegisterSchemaChannel("/unity/client_log", FoxgloveSchemaDefinitions.LogSchemaName);

        rt.Parameters.OnParameterChanged += OnParameterChanged;

        var cubeObject = FindCube();
        if (cubeObject != null)
        {
            _scenePublisher = cubeObject.GetComponent<FoxgloveSceneCubePublisher>();
            if (_scenePublisher != null)
                _scenePublisher.OnSceneCubeColorChanged += OnSceneCubeColorChanged;
        }

        var initialColor = rt.Parameters.GetWireParameter("/cube/color")?.Value;
        if (TryReadColor(initialColor, out var color))
            ApplySceneColorFromParameter(color);

        var initialScale = rt.Parameters.GetWireParameter("/cube/scale")?.Value;
        ApplyScaleFromParameter(initialScale);

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Unsubscribes parameter-change and color-change callbacks to
    /// prevent leaks after destruction.
    /// </summary>
    private void OnDestroy()
    {
        ClearRuntimeWiring();
    }

    private void ClearRuntimeWiring()
    {
        var runtime = _manager?.Runtime;
        if (runtime != null)
        {
            runtime.Parameters.OnParameterChanged -= OnParameterChanged;
            if (_resetSvcId != 0)
            {
                runtime.UnregisterService(_resetSvcId);
                _resetSvcId = 0;
            }
        }

        if (_manager != null)
            _manager.OnClientMessage -= OnClientMessageReceived;

        if (_scenePublisher != null)
        {
            _scenePublisher.OnSceneCubeColorChanged -= OnSceneCubeColorChanged;
            _scenePublisher = null;
        }

        _initialized = false;
    }

    /// <summary>Keeps runtime wiring alive while parameter changes drive cube state.</summary>
    private void Update()
    {
        if (!TryInitializeDemo())
            return;

        var runtime = _manager?.Runtime;
        if (runtime?.Session == null)
        {
            if (_initialized)
                ClearRuntimeWiring();
            return;
        }
    }

    /// <summary>
    /// Locates the cube GameObject by explicit binding or demo object name.
    /// </summary>
    private GameObject FindCube()
    {
        if (_cube != null)
            return _cube;
        if (_cachedCube != null)
            return _cachedCube;
        _cachedCube = GameObject.Find("Cube");
        return _cachedCube;
    }

    // Called by MouseDragCube when scroll changes scale.
    /// <summary>
    /// Called by <c>MouseDragCube</c> when scroll changes scale; pushes
    /// the new scale value to the Foxglove parameter store.
    /// </summary>
    public void SyncScaleToParameter(float s)
    {
        if (float.IsNaN(s) || float.IsInfinity(s))
            return;

        var clamped = Mathf.Clamp(s, ScaleMinimum, ScaleMaximum);
        _manager?.Runtime?.TrySetParameter("/cube/scale", JToken.FromObject(clamped));
        _lastAppliedScale = clamped;
    }

    private void OnClientMessageReceived(uint cid, uint chId, string topic, byte[] payload)
    {
        payload ??= System.Array.Empty<byte>();
        Debug.Log($"[ClientMsg] client={cid} channel={chId} topic={topic} bytes={payload.Length} preview={FormatPayloadPreview(payload)}");
    }

    /// <summary>
    /// Handles Foxglove parameter changes by delegating Unity-object updates to
    /// the main thread via <c>SynchronizationContext</c>.
    /// </summary>
    private void OnParameterChanged(string name, JToken value, string type)
    {
        if (name == "/cube/color" && TryReadColor(value, out var color))
        {
            if (_unityContext != null && SynchronizationContext.Current != _unityContext)
                _unityContext.Post(_ => ApplySceneColorFromParameter(color), null);
            else
                ApplySceneColorFromParameter(color);
            return;
        }

        if (name == "/cube/scale")
        {
            var scaleValue = value?.DeepClone();
            if (_unityContext != null && SynchronizationContext.Current != _unityContext)
                _unityContext.Post(_ => ApplyScaleFromParameter(scaleValue), null);
            else
                ApplyScaleFromParameter(scaleValue);
        }
    }

    /// <summary>
    /// When the scene cube color changes locally, syncs it back to the
    /// Foxglove <c>/cube/color</c> parameter (re-entrancy guarded).
    /// </summary>
    private void OnSceneCubeColorChanged(Color color)
    {
        if (_syncingColor)
            return;

        _lastAppliedColor = color;
        _manager?.Runtime?.TrySetParameter("/cube/color", new JArray(color.r, color.g, color.b, color.a));
    }

    /// <summary>
    /// Applies a color to the scene cube publisher, guarding against
    /// duplicate application and re-entrancy.
    /// </summary>
    private void ApplySceneColorFromParameter(Color color)
    {
        if (color == _lastAppliedColor)
            return;

        _lastAppliedColor = color;
        if (_scenePublisher == null)
        {
            var cube = FindCube();
            if (cube != null)
                _scenePublisher = cube.GetComponent<FoxgloveSceneCubePublisher>();
        }
        if (_scenePublisher == null)
            return;

        _syncingColor = true;
        try { _scenePublisher.SceneCubeColor = color; }
        finally { _syncingColor = false; }
    }

    private void ApplyScaleFromParameter(JToken value)
    {
        if (!TryReadScale(value, out var clamped, out var reason))
        {
            WarnInvalidScaleOnce(reason);
            return;
        }

        if (Mathf.Abs(clamped - _lastAppliedScale) <= 0.001f)
            return;

        _lastAppliedScale = clamped;
        var cube = FindCube();
        if (cube != null)
            cube.transform.localScale = new Vector3(clamped, clamped, clamped);
    }

    /// <summary>
    /// Attempts to parse a Foxglove parameter value as a Unity Color
    /// from a JArray with 3 or 4 components.
    /// </summary>
    private static bool TryReadColor(JToken value, out Color color)
    {
        color = Color.clear;
        if (value is not JArray arr || arr.Count < 3)
            return false;
        try
        {
            color = new Color(
                (float)arr[0].Value<double>(),
                (float)arr[1].Value<double>(),
                (float)arr[2].Value<double>(),
                arr.Count >= 4 ? (float)arr[3].Value<double>() : 1f);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static bool TryReadScale(JToken value, out float clamped, out string reason)
    {
        clamped = 1f;
        reason = null;
        if (value == null)
        {
            reason = "missing scale value";
            return false;
        }

        try
        {
            var scale = (float)value.Value<double>();
            if (float.IsNaN(scale) || float.IsInfinity(scale))
            {
                reason = "non-finite scale value";
                return false;
            }

            clamped = Mathf.Clamp(scale, ScaleMinimum, ScaleMaximum);
            return true;
        }
        catch (System.Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private void WarnInvalidScaleOnce(string reason)
    {
        if (_warnedInvalidScale)
            return;

        _warnedInvalidScale = true;
        Debug.LogWarning("[FoxgloveDemo] Ignoring invalid /cube/scale parameter: " + reason);
    }

    private static string FormatPayloadPreview(byte[] payload)
    {
        var count = Mathf.Min(payload.Length, ClientPayloadPreviewBytes);
        try
        {
            var text = StrictUtf8.GetString(payload, 0, count);
            return payload.Length > count ? $"utf8:{text}..." : $"utf8:{text}";
        }
        catch (System.Exception)
        {
            var builder = new StringBuilder(count * 3);
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(' ');
                builder.Append(payload[i].ToString("X2"));
            }
            if (payload.Length > count)
                builder.Append(" ...");
            return "hex:" + builder;
        }
    }
}
