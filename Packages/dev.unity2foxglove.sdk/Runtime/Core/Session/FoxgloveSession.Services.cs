// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: FoxgloveSession partial — Service call dispatch, handler execution,
// response encoding, and timeout sweep. Service handlers run on the calling
// thread, so DrainServiceCalls must occur on the Unity main thread.

using System;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveSession
    {
        /// <summary>
        /// Sweep timed-out calls, execute pending handler invocations, and
        /// send completed responses/failures. Must run on the Unity main
        /// thread if handlers touch Unity objects.
        /// </summary>
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
                var recorder = Volatile.Read(ref _recorder);
                if (call.FailureMessage != null)
                {
                    var fail = new ServiceCallFailure
                    { ServiceId = call.ServiceId, CallId = call.CallId, Message = call.FailureMessage };
                    _transport.SendText(call.ClientId, JsonConvert.SerializeObject(fail));
                    recorder?.WriteMetadata("foxglove.services",
                        JsonConvert.SerializeObject(new { serviceId = call.ServiceId, callId = call.CallId,
                            status = "failure", message = call.FailureMessage,
                            timestamp = _clock.NowNs }));
                }
                else
                {
                    var frame = BinaryEncoding.EncodeServerServiceCallResponse(
                        call.ServiceId, call.CallId, call.ResponseEncoding ?? "json", call.ResponsePayload);
                    _transport.SendBinary(call.ClientId, frame);
                    recorder?.WriteMetadata("foxglove.services",
                        JsonConvert.SerializeObject(new { serviceId = call.ServiceId, callId = call.CallId,
                            status = "completed", payloadSize = call.ResponsePayload?.Length ?? 0,
                            timestamp = _clock.NowNs }));
                }
            }
        }

        /// <summary>
        /// Decode and validate a binary service call request — checks encoding,
        /// service existence, and payload size &lt;= 1 MiB. Enqueues valid requests
        /// for processing in <see cref="DrainServiceCalls"/>.
        /// </summary>
        private void HandleServiceCallRequest(uint clientId, byte[] data)
        {
            if (!BinaryEncoding.TryDecodeClientServiceCallRequest(data,
                    out var serviceId, out var callId, out var encoding, out var payload))
                return;

            if (encoding != "json")
            {
                SendServiceCallFailure(clientId, serviceId, callId, $"Unsupported encoding: {encoding}");
                return;
            }

            if (!_services.TryGet(serviceId, out _))
            {
                SendServiceCallFailure(clientId, serviceId, callId, $"Unknown service: {serviceId}");
                return;
            }

            if (payload.Length > FoxgloveServiceRegistry.MaxPayloadBytes)
            {
                SendServiceCallFailure(clientId, serviceId, callId, "Payload exceeds 1 MiB limit");
                return;
            }

            try { JToken.Parse(Encoding.UTF8.GetString(payload)); }
            catch
            {
                SendServiceCallFailure(clientId, serviceId, callId, "Malformed JSON payload");
                return;
            }

            if (!_services.TryEnqueue(serviceId, callId, clientId, encoding, payload, out _, out var error))
                SendServiceCallFailure(clientId, serviceId, callId, error);
        }

        private void SendServiceCallFailure(uint clientId, uint serviceId, uint callId, string message)
        {
            _transport.SendText(clientId, JsonConvert.SerializeObject(new ServiceCallFailure
            { ServiceId = serviceId, CallId = callId, Message = message }));
        }
    }
}
