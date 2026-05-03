using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Base class for all Foxglove publisher components.
    /// Handles manager resolution, FPS throttling, and frame ID sanitization.
    /// </summary>
    public abstract class FoxglovePublisherBase : MonoBehaviour
    {
        [SerializeField] protected FoxgloveManager _manager;
        [SerializeField] protected string _topic = "";
        [SerializeField] protected float _publishRateHz = 10f;
        [SerializeField] protected bool _publishOnEnable = true;
        [SerializeField] protected bool _warnIfManagerMissing = true;

        private float _lastPublishTime = float.NegativeInfinity;
        private bool _warnedManagerMissing;

        protected FoxgloveManager Manager => _manager;
        protected abstract string SchemaName { get; }
        protected ulong CurrentLogTimeNs => _manager?.NowNs ?? Schemas.FoxgloveTimeUtil.NowUnixTimeNs();

        protected virtual void OnEnable()
        {
            ResolveManager();
        }

        protected virtual void OnDisable() { }

        protected void ResolveManager()
        {
            if (_manager != null) return;

            _manager = FindFirstObjectByType<FoxgloveManager>();

            if (_manager == null && _warnIfManagerMissing && !_warnedManagerMissing)
            {
                Debug.LogWarning($"[Foxglove] {GetType().Name}: No FoxgloveManager found in scene.");
                _warnedManagerMissing = true;
            }
        }

        /// <summary>True if enough time has elapsed since last publish.</summary>
        protected bool ShouldPublishNow()
        {
            if (_publishRateHz <= 0) return true;

            var interval = 1f / _publishRateHz;
            var now = Time.unscaledTime;
            if (now - _lastPublishTime >= interval)
            {
                _lastPublishTime = now;
                return true;
            }
            return false;
        }

        /// <summary>Replace spaces with underscores. Use fallback if empty.</summary>
        protected static string SanitizeFrameId(string raw, string fallback)
        {
            var sanitized = string.IsNullOrEmpty(raw) ? fallback : raw;
            return sanitized.Replace(' ', '_');
        }

        /// <summary>Publish a message through the manager. Safe no-op if manager is null.</summary>
        protected void Publish(object message, ulong logTimeNs)
        {
            if (_manager == null) return;
            _manager.PublishJson(_topic, SchemaName, message, logTimeNs);
        }
    }
}
