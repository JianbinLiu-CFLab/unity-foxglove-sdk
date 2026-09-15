using System;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Components.Publishing.Session;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Deterministic runtime acceptance gate for the Component MessagePack surfaces.</summary>
    internal static class ComponentMessagePackAcceptanceValidation
    {
        public static void Validate()
        {
            var response = ComponentPublisherContractCatalog.BuildResponse(null);
            if (response["sessionReady"]?.Value<bool>() != false)
                throw new InvalidOperationException("Component contract service did not fail closed before capture.");
            if (response["results"]?[0]?["code"]?.Value<string>() != "session_not_ready")
                throw new InvalidOperationException("Component contract service missing session_not_ready result.");
            var codec = new ComponentMessagePackGeneratedEntry(
                typeof(ComponentMessagePackAcceptanceValidation),
                "unity2foxglove.component.Acceptance",
                "acceptance-shape-v1",
                true,
                true,
                string.Empty,
                _ => new byte[] { 0x81, 0xA1, 0x76, 0x01 });
            var snapshot = new ComponentPublisherSessionBuilder().Build(
                7,
                new[]
                {
                    new ComponentPublisherContractDraft(
                        new object(), "acceptance", typeof(ComponentMessagePackAcceptanceValidation), "scalar",
                        "/phase189/component/scalar", "unity2foxglove.component.Acceptance",
                        PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.MsgPack, codec)
                });
            var active = ComponentPublisherContractCatalog.BuildResponse(snapshot, includeTypeShape: true);
            var row = active["results"]?[0];
            if (row?["effectiveEncoding"]?.Value<string>() != "msgpack"
                || row?["wireSchema"]?.Value<string>() != "schemaless"
                || row?["available"]?.Value<bool>() != true
                || row?["shapeIdentity"]?.Value<string>() != "acceptance-shape-v1")
                throw new InvalidOperationException("Active Component MessagePack projection lost encoding, schema, availability, or shape identity.");
            var bytes = codec.Serialize(null);
            if (bytes.Length != 4 || bytes[0] != 0x81 || bytes[3] != 0x01)
                throw new InvalidOperationException("Generated codec acceptance vector did not preserve deterministic bytes.");
            ComponentMessagePackMcapInspectorValidation.ValidateIndependentFourTopicInspection();
            Console.WriteLine("PHASE189_AUTOMATIC_PASS");
        }
    }
}
