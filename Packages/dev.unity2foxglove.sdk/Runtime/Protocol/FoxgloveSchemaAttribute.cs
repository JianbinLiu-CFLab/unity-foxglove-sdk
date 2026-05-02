using System;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>
    /// Associates a DTO class with its foxglove schema name.
    /// Used by FoxglovePublisher<T> for automatic schema binding.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class FoxgloveSchemaAttribute : Attribute
    {
        public string SchemaName { get; }
        public FoxgloveSchemaAttribute(string schemaName) => SchemaName = schemaName;
    }
}
