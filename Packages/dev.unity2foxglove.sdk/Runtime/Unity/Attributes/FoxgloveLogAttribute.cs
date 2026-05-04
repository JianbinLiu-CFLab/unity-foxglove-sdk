using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Mark a field or property to be auto-published as a Foxglove topic.
    /// The annotated class must be declared as partial for the source generator to extend it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class FoxgloveLogAttribute : Attribute
    {
        /// <summary>Foxglove topic name (e.g. "/debug/pose").</summary>
        public string Topic { get; }

        /// <summary>Publish rate in Hz (default 10).</summary>
        public float RateHz { get; set; } = 10f;

        /// <summary>Optional Foxglove schema name. If empty, publishes schemaless JSON.</summary>
        public string SchemaName { get; set; }

        public FoxgloveLogAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
