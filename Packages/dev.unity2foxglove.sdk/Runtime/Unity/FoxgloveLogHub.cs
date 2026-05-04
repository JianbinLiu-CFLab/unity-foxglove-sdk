using System.Collections.Generic;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public readonly struct FoxgloveLogTopicInfo
    {
        public readonly string Topic;
        public readonly float RateHz;
        public FoxgloveLogTopicInfo(string topic, float rateHz) { Topic = topic; RateHz = rateHz; }
    }

    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs);
    }

    [AddComponentMenu("")]
    public class FoxgloveLogHub : MonoBehaviour
    {
        private static FoxgloveLogHub _instance;
        private FoxgloveManager _mgr;
        private readonly Dictionary<IFoxgloveLogSource, float[]> _timers = new();
        private readonly List<IFoxgloveLogSource> _stale = new();
        private float _scanTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (_instance != null) return;
            var go = new GameObject("[FoxgloveLogHub]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<FoxgloveLogHub>();
        }

        private void Update()
        {
            if (_mgr == null)
                _mgr = FindFirstObjectByType<FoxgloveManager>();
            if (_mgr == null || !_mgr.IsRunning) return;

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = 2f;
                Scan();
            }

            var nowNs = _mgr.NowNs;
            var dt = Time.deltaTime;

            _stale.Clear();
            foreach (var kv in _timers)
            {
                if (kv.Key is MonoBehaviour mb && mb == null) { _stale.Add(kv.Key); continue; }
                if (kv.Key is MonoBehaviour mb2 && !mb2.isActiveAndEnabled) continue;
                var t = kv.Value;
                for (int i = 0; i < t.Length; i++)
                {
                    t[i] -= dt;
                    if (t[i] <= 0f)
                    {
                        var info = kv.Key.FoxgloveLog_GetTopic(i);
                        t[i] = info.RateHz > 0 ? 1f / info.RateHz : 1f;
                        kv.Key.FoxgloveLog_Publish(i, _mgr, nowNs);
                    }
                }
            }
            foreach (var s in _stale) _timers.Remove(s);
        }

        private void Scan()
        {
            var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var mb in all)
            {
                if (mb is IFoxgloveLogSource src && !_timers.ContainsKey(src))
                {
                    var count = src.FoxgloveLog_TopicCount;
                    if (count > 0)
                        _timers[src] = new float[count];
                }
            }
        }

        private void OnDestroy()
        {
            _timers.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
