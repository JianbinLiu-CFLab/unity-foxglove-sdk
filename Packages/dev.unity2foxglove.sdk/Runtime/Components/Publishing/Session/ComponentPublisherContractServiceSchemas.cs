namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    internal static class ComponentPublisherContractServiceSchemas
    {
        internal const string Request = "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\"},\"includeTypeShape\":{\"type\":\"boolean\"}}}";
        internal const string Response = "{\"type\":\"object\",\"properties\":{\"contractVersion\":{\"type\":\"integer\"},\"sessionReady\":{\"type\":\"boolean\"},\"generation\":{\"type\":\"integer\"},\"sourceKind\":{\"type\":\"string\"},\"responseTruncated\":{\"type\":\"boolean\"},\"results\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}}}";
    }
}
