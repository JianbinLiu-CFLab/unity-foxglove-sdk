using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour
{
    [FoxgloveLog("/debug/position")]
    private Vector3 _pos;

    [FoxgloveLog("/debug/health", RateHz = 5)]
    private float _health = 100f;

    void Update() { _pos = transform.position; }
}
