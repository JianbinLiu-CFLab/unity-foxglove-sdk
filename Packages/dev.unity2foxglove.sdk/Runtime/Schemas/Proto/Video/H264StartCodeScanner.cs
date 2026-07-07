// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Shared H.264 Annex B start-code scanner.

using System;
using System.Collections.Generic;

namespace Foxglove.Schemas.Video
{
    internal static class H264StartCodeScanner
    {
        public static bool Find(byte[] data, int startIndex, out int index, out int length)
        {
            index = -1;
            length = 0;
            if (data == null)
                return false;

            for (var i = Math.Max(0, startIndex); i <= data.Length - 3; i++)
            {
                if (i <= data.Length - 4
                    && data[i] == 0
                    && data[i + 1] == 0
                    && data[i + 2] == 0
                    && data[i + 3] == 1)
                {
                    index = i;
                    length = 4;
                    return true;
                }

                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    index = i;
                    length = 3;
                    return true;
                }
            }

            return false;
        }

        public static bool Find(List<byte> data, int startIndex, out int index, out int length)
        {
            index = -1;
            length = 0;
            if (data == null)
                return false;

            for (var i = Math.Max(0, startIndex); i <= data.Count - 3; i++)
            {
                if (i <= data.Count - 4
                    && data[i] == 0
                    && data[i + 1] == 0
                    && data[i + 2] == 0
                    && data[i + 3] == 1)
                {
                    index = i;
                    length = 4;
                    return true;
                }

                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    index = i;
                    length = 3;
                    return true;
                }
            }

            return false;
        }
    }
}
