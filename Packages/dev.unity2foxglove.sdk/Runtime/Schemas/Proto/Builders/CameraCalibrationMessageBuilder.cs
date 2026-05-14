// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Unity-free builders for foxglove.CameraCalibration JSON and protobuf payloads.

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using Unity.FoxgloveSDK.Schemas;

namespace Foxglove.Schemas
{
    /// <summary>Builds <c>foxglove.CameraCalibration</c> JSON/protobuf payloads.</summary>
    public static class CameraCalibrationMessageBuilder
    {
        /// <summary>Create a JSON CameraCalibration DTO.</summary>
        public static CameraCalibrationMessage CreateJson(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            string distortionModel,
            IEnumerable<double> d,
            IEnumerable<double> k,
            IEnumerable<double> r,
            IEnumerable<double> p)
        {
            var dList = ToListOrEmpty(d);
            var kList = ToListOrEmpty(k);
            var rList = ToListOrEmpty(r);
            var pList = ToListOrEmpty(p);
            ValidateMatrices(kList, rList, pList);

            return new CameraCalibrationMessage
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToJsonTime(unixNs),
                FrameId = frameId ?? "",
                Width = width,
                Height = height,
                DistortionModel = distortionModel ?? "",
                D = dList,
                K = kList,
                R = rList,
                P = pList
            };
        }

        /// <summary>Create an official protobuf CameraCalibration message.</summary>
        public static Foxglove.CameraCalibration CreateProtobuf(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            string distortionModel,
            IEnumerable<double> d,
            IEnumerable<double> k,
            IEnumerable<double> r,
            IEnumerable<double> p)
        {
            var dList = ToListOrEmpty(d);
            var kList = ToListOrEmpty(k);
            var rList = ToListOrEmpty(r);
            var pList = ToListOrEmpty(p);
            ValidateMatrices(kList, rList, pList);

            var message = new Foxglove.CameraCalibration
            {
                Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs),
                FrameId = frameId ?? "",
                Width = width,
                Height = height,
                DistortionModel = distortionModel ?? ""
            };
            message.D.AddRange(dList);
            message.K.AddRange(kList);
            message.R.AddRange(rList);
            message.P.AddRange(pList);
            return message;
        }

        /// <summary>Create and serialize an official protobuf CameraCalibration payload.</summary>
        public static byte[] SerializeProtobuf(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            string distortionModel,
            IEnumerable<double> d,
            IEnumerable<double> k,
            IEnumerable<double> r,
            IEnumerable<double> p)
        {
            return CreateProtobuf(unixNs, frameId, width, height, distortionModel, d, k, r, p).ToByteArray();
        }

        /// <summary>Create pinhole intrinsics from image dimensions and vertical field of view.</summary>
        public static CameraCalibrationMessage CreateAutoIntrinsics(
            ulong unixNs,
            string frameId,
            uint width,
            uint height,
            double verticalFovDegrees)
        {
            var fovRad = Math.Max(0.001, verticalFovDegrees) * Math.PI / 180.0;
            var fy = height / (2.0 * Math.Tan(fovRad / 2.0));
            var fx = fy;
            var cx = width / 2.0;
            var cy = height / 2.0;

            return CreateJson(
                unixNs,
                frameId,
                width,
                height,
                "plumb_bob",
                Array.Empty<double>(),
                new[] { fx, 0, cx, 0, fy, cy, 0, 0, 1 },
                new[] { 1.0, 0, 0, 0, 1, 0, 0, 0, 1 },
                new[] { fx, 0, cx, 0, 0, fy, cy, 0, 0, 0, 1, 0 });
        }

        private static List<double> ToListOrEmpty(IEnumerable<double> values)
        {
            return values == null ? new List<double>() : values.ToList();
        }

        private static void ValidateMatrices(ICollection<double> k, ICollection<double> r, ICollection<double> p)
        {
            if (k.Count != 9)
                throw new ArgumentException("CameraCalibration K must contain exactly 9 values.", nameof(k));
            if (r.Count != 9)
                throw new ArgumentException("CameraCalibration R must contain exactly 9 values.", nameof(r));
            if (p.Count != 12)
                throw new ArgumentException("CameraCalibration P must contain exactly 12 values.", nameof(p));
        }
    }
}
