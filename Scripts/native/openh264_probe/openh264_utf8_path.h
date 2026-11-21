// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/native/openh264_probe
// Purpose: UTF-8 to UTF-16 conversion for Windows OpenH264 paths.

#pragma once

#ifdef _WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>

#include <string>

namespace unity2foxglove_openh264
{
    inline std::wstring Utf8ToWide(const std::string& value)
    {
        if (value.empty())
            return std::wstring();

        const int count = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
        if (count <= 0)
            return std::wstring();

        std::wstring wide(static_cast<size_t>(count), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, &wide[0], count);
        wide.resize(static_cast<size_t>(count - 1));
        return wide;
    }
}

#endif
