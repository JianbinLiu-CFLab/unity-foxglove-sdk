using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Registers demo Parameters and Services for Phase 7 manual verification.
/// Attach to the Foxglove GameObject (same one with FoxgloveManager).
/// </summary>
public class FoxgloveDemoSetup : MonoBehaviour
{
    [SerializeField] private FoxgloveManager _manager;

    private uint _resetSvcId;
    private float _lastAppliedScale = -1f;
    private Color _lastAppliedColor = Color.clear;
    private MaterialPropertyBlock _propBlock;

    private void Start()
    {
        if (_manager == null) _manager = GetComponent<FoxgloveManager>();
        if (_manager?.Runtime == null) return;

        var rt = _manager.Runtime;

        rt.RegisterParameter("/cube/color", new JArray(0.0, 1.0, 0.0, 1.0), "number[]", true);
        rt.RegisterParameter("/cube/scale", 1.0, "number", true);

        _resetSvcId = rt.RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor
        {
            Name = "/cube/reset_pose",
            Type = "/cube/reset_pose",
            Request = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseRequest" },
            Response = new Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor { SchemaName = "/cube/ResetPoseResponse" }
        }, req =>
        {
            var cube = FindCube();
            if (cube != null)
            {
                cube.transform.position = Vector3.zero;
                cube.transform.rotation = Quaternion.identity;
                cube.transform.localScale = Vector3.one;
                _lastAppliedScale = 1f;
                _lastAppliedColor = Color.green;

                _manager.Runtime?.Parameters.TrySetFromClient("/cube/color",
                    new JArray(0.0, 1.0, 0.0, 1.0));
                _manager.Runtime?.Parameters.TrySetFromClient("/cube/scale", 1.0);
            }
            return JToken.Parse("{\"status\":\"ok\"}");
        });

        // Phase 8: log client-published messages to Unity Console
        rt.Session.OnClientMessage += (cid, chId, topic, payload) =>
            Debug.Log($"[ClientMsg] client={cid} topic={topic} payload={System.Text.Encoding.UTF8.GetString(payload)}");
    }

    private void Update()
    {
        if (_manager?.Runtime?.Session == null) return;

        // Sync /cube/color from parameter → cube (only when changed externally)
        var colorParam = _manager.Runtime.Parameters.GetWireParameter("/cube/color");
        if (colorParam?.Value is JArray arr && arr.Count >= 3)
        {
            try
            {
                var r = (float)arr[0].Value<double>();
                var g = (float)arr[1].Value<double>();
                var b = (float)arr[2].Value<double>();
                var a = arr.Count >= 4 ? (float)arr[3].Value<double>() : 1f;
                var c = new Color(r, g, b, a);

                if (c != _lastAppliedColor)
                {
                    _lastAppliedColor = c;
                    var cube = FindCube();
                    if (cube != null)
                    {
                        var renderer = cube.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
                            renderer.GetPropertyBlock(_propBlock);
                            _propBlock.SetColor("_Color", c);
                            renderer.SetPropertyBlock(_propBlock);
                        }

                        var scenePub = cube.GetComponent<FoxgloveSceneCubePublisher>();
                        if (scenePub != null) scenePub.SceneCubeColor = c;
                    }
                }
            }
            catch { }
        }

        // Sync /cube/scale from parameter → cube (only when changed externally)
        var scaleParam = _manager.Runtime.Parameters.GetWireParameter("/cube/scale");
        if (scaleParam?.Value != null)
        {
            try
            {
                float s = (float)scaleParam.Value.Value<double>();
                if (Mathf.Abs(s - _lastAppliedScale) > 0.001f)
                {
                    _lastAppliedScale = s;
                    var cube = FindCube();
                    if (cube != null)
                        cube.transform.localScale = new Vector3(s, s, s);
                }
            }
            catch { }
        }
    }

    private GameObject FindCube()
    {
        var cube = GameObject.Find("Cube");
        if (cube == null) cube = GameObject.FindGameObjectWithTag("Player");
        return cube;
    }

    // Called by MouseDragCube when scroll changes scale
    public void SyncScaleToParameter(float s)
    {
        _manager?.Runtime?.Parameters.TrySetFromClient("/cube/scale", s);
        _lastAppliedScale = s;
    }
}
