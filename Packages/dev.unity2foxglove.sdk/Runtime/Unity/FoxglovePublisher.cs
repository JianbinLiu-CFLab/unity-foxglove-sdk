using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Generic publisher base class with automatic schema binding.
    /// Subclasses create message objects; the base class handles JSON serialization
    /// and FPS throttling via FoxglovePublisherBase.
    /// </summary>
    public abstract class FoxglovePublisher<TMessage> : FoxglovePublisherBase where TMessage : class, new()
    {
        protected override string SchemaName
        {
            get
            {
                var attr = typeof(TMessage).GetCustomAttributes(typeof(FoxgloveSchemaAttribute), false);
                return attr.Length > 0 ? ((FoxgloveSchemaAttribute)attr[0]).SchemaName : "";
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
        }

        /// <summary>Called at publish time. Subclass builds the message object.</summary>
        protected abstract TMessage CreateMessage();

        /// <summary>Publish the message as JSON with the current timestamp.</summary>
        protected void Publish(TMessage message)
        {
            if (_manager == null) return;
            if (string.IsNullOrEmpty(_topic)) return;

            var unixNs = FoxgloveTimeUtil.NowUnixTimeNs();
            _manager.PublishJson(_topic, SchemaName, message, unixNs);
        }
    }
}
