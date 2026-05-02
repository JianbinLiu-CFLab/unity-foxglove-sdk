using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Registers demo Parameters and Services for Phase 6 manual verification.
/// Attach to the Foxglove GameObject (same one with FoxgloveManager).
/// </summary>
public class FoxgloveDemoSetup : MonoBehaviour
{
    [SerializeField] private FoxgloveManager _manager;
    [SerializeField] private MouseDragCube _cube;
    [SerializeField] private FoxgloveSceneCubePublisher _scenePublisher;

    private uint _resetSvcId;
    private float _lastAppliedScale = -1f;

    /// <summary>Called by MouseDragCube when scroll changes scale.</summary>
    public void SyncScaleToParameter(float s)
    {
        _manager?.Runtime?.Parameters.TrySetFromClient("/cube/scale", s);
        _lastAppliedScale = s;
    }

    private void Start()
    {
        if (_manager == null) _manager = GetComponent<FoxgloveManager>();
        if (_cube != null && _scenePublisher == null)
            _scenePublisher = _cube.GetComponent<FoxgloveSceneCubePublisher>();
        if (_manager?.Runtime == null) return;

        var rt = _manager.Runtime;

        rt.Parameters.Register("/cube/color", new JArray(0.0, 1.0, 0.0, 1.0), "number[]", true);
        rt.Parameters.Register("/cube/scale", 1.0, "number", true);

        _resetSvcId = rt.RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor
        {
            Name = "/cube/reset_pose",
            Type = "/cube/reset_pose",
            Request = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseRequest" },
            Response = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseResponse" }
        });

        Debug.Log("[FoxgloveDemo] Registered /cube/color, /cube/scale params and /cube/reset_pose service");
    }

    private void Update()
    {
        var session = _manager?.Runtime?.Session;
        if (session == null) return;

        // ── Sync /cube/color → cube state ──
        var colorParam = _manager.Runtime.Parameters.GetWireParameter("/cube/color");
        if (colorParam?.Value is JArray arr && arr.Count >= 3 && _cube != null)
        {
            try
            {
                var r = (float)arr[0].Value<double>();
                var g = (float)arr[1].Value<double>();
                var b = (float)arr[2].Value<double>();
                var a = arr.Count >= 4 ? (float)arr[3].Value<double>() : 1f;
                var c = new Color(r, g, b, a);

                var renderer = _cube.GetComponent<Renderer>();
                if (renderer != null) renderer.material.color = c;
                if (_scenePublisher != null) _scenePublisher.SceneCubeColor = c;
            }
            catch { /* malformed color — ignore */ }
        }

        // ── Sync /cube/scale → cube state (only when changed externally) ──
        var scaleParam = _manager.Runtime.Parameters.GetWireParameter("/cube/scale");
        if (scaleParam?.Value != null && _cube != null)
        {
            try
            {
                float s = (float)scaleParam.Value.Value<double>();
                if (Mathf.Abs(s - _lastAppliedScale) > 0.001f)
                {
                    _cube.transform.localScale = new Vector3(s, s, s);
                    _lastAppliedScale = s;
                }
            }
            catch { /* malformed value — ignore */ }
        }

        // ── Handle /cube/reset_pose service calls ──
        var pending = session.Services.GetPendingCalls();
        if (pending != null)
        {
            foreach (var call in pending.Where(c => c.ServiceId == _resetSvcId))
            {
                if (_cube != null)
                {
                    _cube.transform.position = Vector3.zero;
                    _cube.transform.rotation = Quaternion.identity;
                    _cube.transform.localScale = Vector3.one;
                    _lastAppliedScale = 1f;

                    _manager.Runtime.Parameters.TrySetFromClient("/cube/color",
                        new JArray(0.0, 1.0, 0.0, 1.0));
                    _manager.Runtime.Parameters.TrySetFromClient("/cube/scale", 1.0);
                }
                session.Services.CompleteResponse(call.ClientId, call.CallId, "json",
                    Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
                Debug.Log($"[FoxgloveDemo] Reset pose: callId={call.CallId}");
            }
        }
    }
}
