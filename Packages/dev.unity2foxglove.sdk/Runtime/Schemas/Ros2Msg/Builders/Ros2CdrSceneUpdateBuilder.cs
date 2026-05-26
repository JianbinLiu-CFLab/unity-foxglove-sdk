// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Builders
// Purpose: ROS 2 CDR smoke builder for foxglove_msgs/msg/SceneUpdate.

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>Builds minimal CDR payloads for foxglove_msgs/msg/SceneUpdate.</summary>
    public static class Ros2CdrSceneUpdateBuilder
    {
        public const string SchemaName = "foxglove_msgs/msg/SceneUpdate";

        /// <summary>Serialize supported SceneUpdate data to ROS 2 CDR.</summary>
        public static byte[] Serialize(SceneUpdateMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var writer = new Ros2CdrWriter();
            WriteDeletions(writer, message.Deletions);
            WriteEntities(writer, message.Entities);
            return writer.ToArray();
        }

        private static void WriteDeletions(Ros2CdrWriter writer, IList<SceneEntityDeletion> deletions)
        {
            var count = deletions?.Count ?? 0;
            writer.WriteSequenceLength(count);
            for (var i = 0; i < count; i++)
            {
                var deletion = deletions[i] ?? new SceneEntityDeletion();
                Ros2CdrGeometryWriter.WriteTime(writer, deletion.Timestamp);
                writer.WriteUInt8(checked((byte)deletion.Type));
                writer.WriteString(deletion.Id);
            }
        }

        private static void WriteEntities(Ros2CdrWriter writer, IList<SceneEntity> entities)
        {
            var count = entities?.Count ?? 0;
            writer.WriteSequenceLength(count);
            for (var i = 0; i < count; i++)
                WriteEntity(writer, entities[i] ?? new SceneEntity());
        }

        private static void WriteEntity(Ros2CdrWriter writer, SceneEntity entity)
        {
            Ros2CdrGeometryWriter.WriteTime(writer, entity.Timestamp);
            writer.WriteString(entity.FrameId);
            writer.WriteString(entity.Id);
            Ros2CdrGeometryWriter.WriteDuration(writer, entity.Lifetime);
            writer.WriteBool(entity.FrameLocked);
            WriteMetadata(writer, entity.Metadata);

            EnsureUnsupportedEmpty(entity.Arrows, "arrows");
            writer.WriteSequenceLength(0);
            WriteCubes(writer, entity.Cubes);
            EnsureUnsupportedEmpty(entity.Spheres, "spheres");
            writer.WriteSequenceLength(0);
            EnsureUnsupportedEmpty(entity.Cylinders, "cylinders");
            writer.WriteSequenceLength(0);
            EnsureUnsupportedEmpty(entity.Lines, "lines");
            writer.WriteSequenceLength(0);
            EnsureUnsupportedEmpty(entity.Triangles, "triangles");
            writer.WriteSequenceLength(0);
            EnsureUnsupportedEmpty(entity.Texts, "texts");
            writer.WriteSequenceLength(0);
            EnsureUnsupportedEmpty(entity.Models, "models");
            writer.WriteSequenceLength(0);
        }

        private static void WriteMetadata(Ros2CdrWriter writer, IList<FoxgloveKeyValuePair> metadata)
        {
            var count = metadata?.Count ?? 0;
            writer.WriteSequenceLength(count);
            for (var i = 0; i < count; i++)
            {
                var pair = metadata[i] ?? new FoxgloveKeyValuePair();
                writer.WriteString(pair.Key);
                writer.WriteString(pair.Value);
            }
        }

        private static void WriteCubes(Ros2CdrWriter writer, IList<CubePrimitive> cubes)
        {
            var count = cubes?.Count ?? 0;
            writer.WriteSequenceLength(count);
            for (var i = 0; i < count; i++)
            {
                var cube = cubes[i] ?? new CubePrimitive();
                Ros2CdrGeometryWriter.WritePose(writer, cube.Pose);
                Ros2CdrGeometryWriter.WriteVector3(writer, cube.Size);
                Ros2CdrGeometryWriter.WriteColor(writer, cube.Color);
            }
        }

        private static void EnsureUnsupportedEmpty<T>(ICollection<T> values, string fieldName)
        {
            if (values != null && values.Count != 0)
                throw new NotSupportedException($"SceneUpdate {fieldName} serialization is not supported by this CDR builder.");
        }
    }
}
