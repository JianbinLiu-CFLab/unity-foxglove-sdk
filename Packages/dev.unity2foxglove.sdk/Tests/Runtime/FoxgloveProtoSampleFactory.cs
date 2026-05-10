// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test-only sample-message factory for proving that every bundled
// official Foxglove protobuf schema can be constructed, serialized, published,
// and recorded to MCAP.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Foxglove;
using Foxglove.Schemas;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// A schema-name/sample-message pair used by Phase 44 coverage tests.
    /// </summary>
    public sealed class FoxgloveProtoSample
    {
        public FoxgloveProtoSample(FoxgloveProtoSchemaCatalogEntry catalogEntry, IMessage message)
        {
            CatalogEntry = catalogEntry ?? throw new ArgumentNullException(nameof(catalogEntry));
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public FoxgloveProtoSchemaCatalogEntry CatalogEntry { get; }
        public string SchemaName => CatalogEntry.SchemaName;
        public IMessage Message { get; }
    }

    /// <summary>
    /// Builds deterministic minimal samples for the current protobuf catalog.
    /// These samples prove construct/serialize/publish/MCAP viability, not
    /// complete Foxglove panel semantics for every schema.
    /// </summary>
    public static class FoxgloveProtoSampleFactory
    {
        private static readonly byte[] SampleBytes = { 0x01, 0x02, 0x03, 0x04 };
        private static readonly byte[] SampleImageBytes =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
            0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
            0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82
        };

        public static IReadOnlyList<FoxgloveProtoSample> CreateAll()
        {
            return FoxgloveProtoSchemaCatalog.Entries
                .Select(entry => new FoxgloveProtoSample(entry, Create(entry)))
                .ToArray();
        }

        public static IMessage Create(FoxgloveProtoSchemaCatalogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (!typeof(IMessage).IsAssignableFrom(entry.ClrType))
                throw new InvalidOperationException($"{entry.SchemaName} type does not implement IMessage.");

            var message = (IMessage)Activator.CreateInstance(entry.ClrType);
            PopulateMessage(message, depth: 0);
            ApplySemanticOverrides(message);
            return message;
        }

        public static IMessage Parse(FoxgloveProtoSchemaCatalogEntry entry, byte[] data)
        {
            var parser = (MessageParser)entry.ClrType.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            if (parser == null)
                throw new InvalidOperationException($"{entry.SchemaName} does not expose a static Parser property.");
            return parser.ParseFrom(data);
        }

        private static void PopulateMessage(IMessage message, int depth)
        {
            if (message == null || depth > 5) return;

            foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
            {
                if (field.IsMap) continue;

                if (field.IsRepeated)
                {
                    var collection = field.Accessor.GetValue(message);
                    var value = CreateFieldValue(field, depth + 1);
                    if (collection != null && value != null)
                        AddRepeatedValue(collection, value);
                    continue;
                }

                var fieldValue = CreateFieldValue(field, depth + 1);
                if (fieldValue != null)
                    field.Accessor.SetValue(message, fieldValue);
            }
        }

        private static object CreateFieldValue(FieldDescriptor field, int depth)
        {
            switch (field.FieldType)
            {
                case FieldType.Bool:
                    return true;
                case FieldType.Bytes:
                    return ByteString.CopyFrom(SampleBytes);
                case FieldType.Double:
                    return 1.25d;
                case FieldType.Float:
                    return 1.25f;
                case FieldType.Int32:
                case FieldType.SInt32:
                case FieldType.SFixed32:
                    return 1;
                case FieldType.Int64:
                case FieldType.SInt64:
                case FieldType.SFixed64:
                    return 1L;
                case FieldType.UInt32:
                case FieldType.Fixed32:
                    return (uint)(field.Name.Contains("stride") ? 4 : 1);
                case FieldType.UInt64:
                case FieldType.Fixed64:
                    return 1UL;
                case FieldType.String:
                    return CreateStringValue(field);
                case FieldType.Enum:
                    return CreateEnumValue(field);
                case FieldType.Message:
                    return CreateMessageValue(field, depth);
                case FieldType.Group:
                default:
                    return null;
            }
        }

        private static string CreateStringValue(FieldDescriptor field)
        {
            var name = field.Name ?? "";
            if (name.Contains("frame_id")) return "unity_world";
            if (name == "format") return "png";
            if (name == "encoding") return "rgba8";
            if (name == "media_type") return "model/gltf-binary";
            if (name == "geojson") return "{\"type\":\"FeatureCollection\",\"features\":[]}";
            if (name == "url") return "package://unity2foxglove/sample.glb";
            if (name == "key") return "phase44_key";
            if (name == "value") return "phase44_value";
            if (name == "id") return "phase44_entity";
            if (name == "text") return "phase44";
            if (name == "message") return "phase44 sample";
            if (name == "name") return "phase44";
            if (name == "file") return "Phase44Validation.cs";
            return "phase44";
        }

        private static object CreateEnumValue(FieldDescriptor field)
        {
            var propertyType = field.ContainingType.ClrType.GetProperty(field.PropertyName)?.PropertyType;
            if (propertyType == null || !propertyType.IsEnum)
                return 0;

            var values = System.Enum.GetValues(propertyType);
            foreach (var value in values)
            {
                if (Convert.ToInt32(value) != 0)
                    return value;
            }
            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(propertyType);
        }

        private static object CreateMessageValue(FieldDescriptor field, int depth)
        {
            if (field.MessageType.FullName == Timestamp.Descriptor.FullName)
                return new Timestamp { Seconds = 1, Nanos = 123456789 };
            if (field.MessageType.FullName == Duration.Descriptor.FullName)
                return new Duration { Seconds = 1, Nanos = 0 };

            var message = (IMessage)Activator.CreateInstance(field.MessageType.ClrType);
            PopulateMessage(message, depth);
            ApplySemanticOverrides(message);
            return message;
        }

        private static void AddRepeatedValue(object collection, object value)
        {
            if (collection is IList list)
            {
                list.Add(value);
                return;
            }

            var add = collection.GetType().GetMethods()
                .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
            if (add == null)
                throw new InvalidOperationException($"Repeated field collection {collection.GetType().Name} has no Add method.");
            add.Invoke(collection, new[] { value });
        }

        private static void ApplySemanticOverrides(IMessage message)
        {
            switch (message)
            {
                case Color c:
                    c.R = 0.1;
                    c.G = 0.8;
                    c.B = 0.2;
                    c.A = 1.0;
                    break;
                case Quaternion q:
                    q.W = q.W == 0 ? 1 : q.W;
                    break;
                case Pose p:
                    p.Position ??= new Vector3 { X = 1, Y = 2, Z = 3 };
                    p.Orientation ??= new Quaternion { W = 1 };
                    break;
                case PackedElementField f:
                    f.Name = string.IsNullOrEmpty(f.Name) ? "x" : f.Name;
                    f.Type = PackedElementField.Types.NumericType.Float32;
                    f.Offset = 0;
                    break;
                case CompressedImage image:
                    image.Format = "png";
                    image.Data = ByteString.CopyFrom(SampleImageBytes);
                    image.FrameId = "camera";
                    break;
                case RawImage raw:
                    raw.Width = 2;
                    raw.Height = 2;
                    raw.Encoding = "rgba8";
                    raw.Step = 8;
                    raw.FrameId = "camera";
                    raw.Data = ByteString.CopyFrom(new byte[]
                    {
                        255, 0, 0, 255, 0, 255, 0, 255,
                        0, 0, 255, 255, 255, 255, 255, 255
                    });
                    break;
                case RawAudio audio:
                    audio.Format = "pcm-s16";
                    audio.SampleRate = 48000;
                    audio.NumberOfChannels = 1;
                    audio.Data = ByteString.CopyFrom(new byte[] { 0, 0, 1, 0 });
                    break;
                case PointCloud pc:
                    pc.PointStride = 12;
                    pc.Fields.Clear();
                    pc.Fields.Add(new PackedElementField { Name = "x", Offset = 0, Type = PackedElementField.Types.NumericType.Float32 });
                    pc.Fields.Add(new PackedElementField { Name = "y", Offset = 4, Type = PackedElementField.Types.NumericType.Float32 });
                    pc.Fields.Add(new PackedElementField { Name = "z", Offset = 8, Type = PackedElementField.Types.NumericType.Float32 });
                    pc.Data = ByteString.CopyFrom(new byte[12]);
                    break;
                case Grid grid:
                    grid.ColumnCount = 1;
                    grid.CellSize = new Vector2 { X = 1, Y = 1 };
                    grid.RowStride = 4;
                    grid.CellStride = 4;
                    grid.Fields.Clear();
                    grid.Fields.Add(new PackedElementField { Name = "value", Offset = 0, Type = PackedElementField.Types.NumericType.Float32 });
                    grid.Data = ByteString.CopyFrom(new byte[4]);
                    break;
                case VoxelGrid voxel:
                    voxel.RowCount = 1;
                    voxel.ColumnCount = 1;
                    voxel.CellSize = new Vector3 { X = 1, Y = 1, Z = 1 };
                    voxel.SliceStride = 4;
                    voxel.RowStride = 4;
                    voxel.CellStride = 4;
                    voxel.Fields.Clear();
                    voxel.Fields.Add(new PackedElementField { Name = "value", Offset = 0, Type = PackedElementField.Types.NumericType.Float32 });
                    voxel.Data = ByteString.CopyFrom(new byte[4]);
                    break;
                case LaserScan scan:
                    scan.StartAngle = -0.5;
                    scan.EndAngle = 0.5;
                    scan.Ranges.Clear();
                    scan.Ranges.Add(1.0);
                    scan.Intensities.Clear();
                    scan.Intensities.Add(0.5);
                    break;
                case CameraCalibration calibration:
                    calibration.Width = 2;
                    calibration.Height = 2;
                    calibration.DistortionModel = "plumb_bob";
                    FillRepeated(calibration.K, 9, 0);
                    FillRepeated(calibration.R, 9, 0);
                    FillRepeated(calibration.P, 12, 0);
                    break;
                case GeoJSON geo:
                    geo.Geojson = "{\"type\":\"FeatureCollection\",\"features\":[]}";
                    break;
                case Log log:
                    log.Level = Log.Types.Level.Info;
                    break;
            }
        }

        private static void FillRepeated(Google.Protobuf.Collections.RepeatedField<double> values, int count, double fill)
        {
            values.Clear();
            for (var i = 0; i < count; i++)
                values.Add(fill);
        }
    }
}
