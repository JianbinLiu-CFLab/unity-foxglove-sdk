using System;
using Newtonsoft.Json.Linq;
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
            Console.WriteLine("PHASE189_AUTOMATIC_PASS");
        }
    }
}
