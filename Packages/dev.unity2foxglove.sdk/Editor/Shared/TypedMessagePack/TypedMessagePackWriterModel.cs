// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

namespace Unity.FoxgloveSDK.Editor
{
    internal sealed class TypedMessagePackWriterModel
    {
        internal TypedMessagePackWriterModel(FoxRunTypeShape shape, string access, string writer, string pad)
        {
            Shape = shape;
            Access = access;
            Writer = writer;
            Pad = pad;
        }

        internal FoxRunTypeShape Shape { get; }
        internal string Access { get; }
        internal string Writer { get; }
        internal string Pad { get; }
    }
}
