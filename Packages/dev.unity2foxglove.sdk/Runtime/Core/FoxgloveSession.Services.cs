using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveSession
    {
        /// <summary>Drain completed service calls and send responses/failures.</summary>
        public void DrainServiceCalls()
        {
            _services.SweepTimeouts(FoxgloveServiceRegistry.DefaultTimeout);

            foreach (var call in _services.GetPendingCalls())
            {
                var handler = _services.GetHandler(call.ServiceId);
                if (handler == null) continue;
                try
                {
                    var payloadStr = Encoding.UTF8.GetString(call.Payload);
                    var input = JToken.Parse(payloadStr);
                    var result = handler(input);
                    var responseBytes = Encoding.UTF8.GetBytes(result.ToString(Formatting.None));
                    _services.CompleteResponse(call.ClientId, call.CallId, "json", responseBytes);
                }
                catch (Exception ex)
                {
                    _services.Fail(call.ClientId, call.CallId, $"Handler exception: {ex.Message}");
                }
            }

            foreach (var call in _services.DrainCompleted())
            {
                if (call.FailureMessage != null)
                {
                    var fail = new ServiceCallFailure
                    { ServiceId = call.ServiceId, CallId = call.CallId, Message = call.FailureMessage };
                    _transport.SendText(call.ClientId, JsonConvert.SerializeObject(fail));
                    _recorder?.WriteMetadata("foxglove.services",
                        JsonConvert.SerializeObject(new { serviceId = call.ServiceId, callId = call.CallId,
                            status = "failure", message = call.FailureMessage,
                            timestamp = _clock.NowNs }));
                }
                else
                {
                    var frame = BinaryEncoding.EncodeServerServiceCallResponse(
                        call.ServiceId, call.CallId, call.ResponseEncoding ?? "json", call.ResponsePayload);
                    _transport.SendBinary(call.ClientId, frame);
                    _recorder?.WriteMetadata("foxglove.services",
                        JsonConvert.SerializeObject(new { serviceId = call.ServiceId, callId = call.CallId,
                            status = "completed", payloadSize = call.ResponsePayload?.Length ?? 0,
                            timestamp = _clock.NowNs }));
                }
            }
        }

        private void HandleServiceCallRequest(uint clientId, byte[] data)
        {
            if (!BinaryEncoding.TryDecodeClientServiceCallRequest(data,
                    out var svcServiceId, out var svcCallId, out var svcEncoding, out var svcPayload))
                return;

            if (svcEncoding != "json")
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = svcServiceId, CallId = svcCallId, Message = $"Unsupported encoding: {svcEncoding}" }));
                return;
            }

            if (!_services.TryGet(svcServiceId, out _))
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = svcServiceId, CallId = svcCallId, Message = $"Unknown service: {svcServiceId}" }));
                return;
            }

            if (svcPayload.Length > FoxgloveServiceRegistry.MaxPayloadBytes)
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = svcServiceId, CallId = svcCallId, Message = $"Payload exceeds 1 MiB limit" }));
                return;
            }

            try { JToken.Parse(Encoding.UTF8.GetString(svcPayload)); }
            catch
            {
                _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
                { ServiceId = svcServiceId, CallId = svcCallId, Message = "Malformed JSON payload" }));
                return;
            }

            _services.Enqueue(svcServiceId, svcCallId, clientId, svcEncoding, svcPayload);
        }
    }
}
