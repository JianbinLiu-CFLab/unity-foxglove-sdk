// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 48 validation for camera CompressedImage protobuf parity.

using System;
using System.Linq;
using Foxglove.Schemas;
using Google.Protobuf;

public static class Phase48Validation
{
    public static void Validate()
    {
        Console.WriteLine("=== Phase 48: Camera CompressedImage Protobuf Parity ===");
        var passed = 0;
        var failed = 0;

        void Check(bool condition, string message)
        {
            if (condition) { Console.WriteLine($"[PASS] {message}"); passed++; }
            else { Console.WriteLine($"[FAIL] {message}"); failed++; }
        }

        var jpeg = new byte[] { 0xff, 0xd8, 0x11, 0x22, 0xff, 0xd9 };
        const ulong unixNs = 1_234_567_890UL;

        var message = CameraCompressedImageBuilder.Create(unixNs, "unity_camera", jpeg, "jpeg");
        Check(((IMessage)message).Descriptor.FullName == "foxglove.CompressedImage", "48A-1: builder returns official CompressedImage message");
        Check(message.Timestamp != null && message.Timestamp.Seconds == 1 && message.Timestamp.Nanos == 234_567_890, "48A-2: protobuf timestamp preserves unix ns");
        Check(message.FrameId == "unity_camera", "48A-3: frame_id is preserved");
        Check(message.Format == "jpeg", "48A-4: image format is preserved");
        Check(message.Data.ToByteArray().SequenceEqual(jpeg), "48A-5: protobuf data stores raw JPEG bytes");

        var payload = CameraCompressedImageBuilder.Serialize(unixNs, "unity_camera", jpeg, "jpeg");
        var parsed = Foxglove.CompressedImage.Parser.ParseFrom(payload);
        Check(parsed.Data.ToByteArray().SequenceEqual(jpeg), "48A-6: serialized protobuf payload roundtrips raw JPEG bytes");
        Check(payload.Length > 0, "48A-7: serialized protobuf payload is non-empty");

        Console.WriteLine($"\nPhase 48: {passed} passed, {failed} failed.");
        if (failed > 0)
            throw new Exception($"Phase 48: {failed} test(s) failed.");
    }
}
