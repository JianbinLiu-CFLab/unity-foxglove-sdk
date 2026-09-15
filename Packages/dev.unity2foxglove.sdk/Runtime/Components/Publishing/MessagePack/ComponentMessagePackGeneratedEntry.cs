using System;

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    public sealed class ComponentMessagePackGeneratedEntry
    {
        public ComponentMessagePackGeneratedEntry(Type clrType, string logicalSchemaName, string shapeIdentity, bool isAvailable, bool claimsLogicalSchemaKey, string diagnostic, ComponentMessagePackSerializeDelegate serializer = null)
        {
            ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            ShapeIdentity = shapeIdentity ?? string.Empty;
            IsAvailable = isAvailable;
            ClaimsLogicalSchemaKey = claimsLogicalSchemaKey;
            Diagnostic = diagnostic ?? string.Empty;
            Serializer = serializer;
        }
        public Type ClrType { get; }
        public string LogicalSchemaName { get; }
        public string ShapeIdentity { get; }
        public bool IsAvailable { get; }
        public bool ClaimsLogicalSchemaKey { get; }
        public string Diagnostic { get; }
        public ComponentMessagePackSerializeDelegate Serializer { get; }
        public byte[] Serialize(object value)
        {
            if (!IsAvailable || Serializer == null) throw new InvalidOperationException(Diagnostic.Length == 0 ? "MessagePack codec is unavailable." : Diagnostic);
            return Serializer(value);
        }
    }
}
