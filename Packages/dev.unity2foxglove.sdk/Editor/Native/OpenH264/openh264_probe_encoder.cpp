// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/native/openh264_probe
// Purpose: OpenH264 helper process for source-built and official-binary flows.

#include "codec_api.h"

#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
#include <windows.h>
#endif

namespace
{
    struct Options
    {
        int width = 0;
        int height = 0;
        int fps = 30;
        int bitrateKbps = 4000;
        int keyint = 30;
        std::string openh264Dll;
    };

    using WelsCreateSVCEncoderFn = int (*)(ISVCEncoder**);
    using WelsDestroySVCEncoderFn = void (*)(ISVCEncoder*);

    struct OpenH264Api
    {
        WelsCreateSVCEncoderFn createEncoder = nullptr;
        WelsDestroySVCEncoderFn destroyEncoder = nullptr;
#ifdef _WIN32
        HMODULE module = nullptr;
#endif

        ~OpenH264Api()
        {
#ifdef _WIN32
            if (module != nullptr)
                FreeLibrary(module);
#endif
        }
    };

    bool ParseInt(const char* value, int& output)
    {
        if (value == nullptr || *value == '\0')
            return false;

        char* end = nullptr;
        const long parsed = std::strtol(value, &end, 10);
        if (end == value || *end != '\0')
            return false;

        output = static_cast<int>(parsed);
        return true;
    }

    bool ParseArgs(int argc, char** argv, Options& options)
    {
        for (int i = 1; i < argc; i += 2)
        {
            if (i + 1 >= argc)
                return false;

            const std::string key(argv[i]);
            if (key == "--openh264-dll")
            {
                options.openh264Dll = argv[i + 1] == nullptr ? "" : argv[i + 1];
                continue;
            }

            int value = 0;
            if (!ParseInt(argv[i + 1], value))
                return false;

            if (key == "--width")
                options.width = value;
            else if (key == "--height")
                options.height = value;
            else if (key == "--fps")
                options.fps = value;
            else if (key == "--bitrate-kbps")
                options.bitrateKbps = value;
            else if (key == "--keyint")
                options.keyint = value;
            else
                return false;
        }

        return options.width > 0
            && options.height > 0
            && options.fps > 0
            && options.bitrateKbps > 0
            && options.keyint > 0
#ifdef _WIN32
            && !options.openh264Dll.empty()
#endif
            && (options.width % 2) == 0
            && (options.height % 2) == 0;
    }

    void WriteUsage()
    {
        std::cerr
            << "Usage: openh264_probe_encoder "
            << "--openh264-dll <path> "
            << "--width <even_pixels> --height <even_pixels> "
            << "--fps <frames_per_second> --bitrate-kbps <kbps> --keyint <frames>"
            << std::endl;
    }

#ifdef _WIN32
    std::wstring Utf8ToWide(const std::string& value)
    {
        if (value.empty())
            return std::wstring();

        const int count = MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, nullptr, 0);
        if (count <= 0)
            return std::wstring();

        std::wstring wide(static_cast<size_t>(count - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, value.c_str(), -1, &wide[0], count);
        return wide;
    }
#endif

    bool LoadOpenH264(const Options& options, OpenH264Api& api)
    {
#ifdef _WIN32
        const auto dllPath = Utf8ToWide(options.openh264Dll);
        if (dllPath.empty())
        {
            std::cerr << "OpenH264 DLL path is empty or invalid UTF-8." << std::endl;
            return false;
        }

        api.module = LoadLibraryW(dllPath.c_str());
        if (api.module == nullptr)
        {
            std::cerr << "LoadLibraryW failed for OpenH264 DLL. win32_error=" << GetLastError() << std::endl;
            return false;
        }

        api.createEncoder = reinterpret_cast<WelsCreateSVCEncoderFn>(GetProcAddress(api.module, "WelsCreateSVCEncoder"));
        api.destroyEncoder = reinterpret_cast<WelsDestroySVCEncoderFn>(GetProcAddress(api.module, "WelsDestroySVCEncoder"));
        if (api.createEncoder == nullptr || api.destroyEncoder == nullptr)
        {
            std::cerr << "GetProcAddress failed for OpenH264 encoder entry points." << std::endl;
            return false;
        }

        return true;
#else
        (void)options;
        api.createEncoder = &WelsCreateSVCEncoder;
        api.destroyEncoder = &WelsDestroySVCEncoder;
        return true;
#endif
    }

    void WriteLittleEndianLength(uint32_t length)
    {
        const uint8_t bytes[4] =
        {
            static_cast<uint8_t>(length & 0xFF),
            static_cast<uint8_t>((length >> 8) & 0xFF),
            static_cast<uint8_t>((length >> 16) & 0xFF),
            static_cast<uint8_t>((length >> 24) & 0xFF)
        };
        std::cout.write(reinterpret_cast<const char*>(bytes), 4);
    }

    bool ReadFrame(std::vector<uint8_t>& frame)
    {
        std::cin.read(reinterpret_cast<char*>(frame.data()), static_cast<std::streamsize>(frame.size()));
        const std::streamsize read = std::cin.gcount();
        if (read == 0 && std::cin.eof())
            return false;

        if (read != static_cast<std::streamsize>(frame.size()))
        {
            std::cerr << "Partial I420 frame received before EOF. bytes=" << read << std::endl;
            std::exit(3);
        }

        return true;
    }

    void AppendLayerNalUnits(const SLayerBSInfo& layer, std::vector<uint8_t>& accessUnit)
    {
        int offset = 0;
        for (int nal = 0; nal < layer.iNalCount; ++nal)
        {
            const int length = layer.pNalLengthInByte[nal];
            if (length <= 0)
                continue;

            accessUnit.insert(
                accessUnit.end(),
                layer.pBsBuf + offset,
                layer.pBsBuf + offset + length);
            offset += length;
        }
    }

    void WriteAccessUnit(const SFrameBSInfo& info)
    {
        if (info.eFrameType == videoFrameTypeSkip)
        {
            std::cerr << "OpenH264 skipped frame." << std::endl;
            return;
        }

        if (info.eFrameType == videoFrameTypeInvalid)
            return;

        std::vector<uint8_t> accessUnit;
        for (int layer = 0; layer < info.iLayerNum; ++layer)
            AppendLayerNalUnits(info.sLayerInfo[layer], accessUnit);

        if (accessUnit.empty())
            return;

        WriteLittleEndianLength(static_cast<uint32_t>(accessUnit.size()));
        std::cout.write(reinterpret_cast<const char*>(accessUnit.data()), static_cast<std::streamsize>(accessUnit.size()));
        std::cout.flush();
    }
}

int main(int argc, char** argv)
{
#ifdef _WIN32
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
#endif

    Options options;
    if (!ParseArgs(argc, argv, options))
    {
        WriteUsage();
        return 2;
    }

    OpenH264Api api;
    if (!LoadOpenH264(options, api))
        return 4;

    ISVCEncoder* encoder = nullptr;
    if (api.createEncoder(&encoder) != 0 || encoder == nullptr)
    {
        std::cerr << "WelsCreateSVCEncoder failed." << std::endl;
        return 4;
    }

    SEncParamExt params;
    std::memset(&params, 0, sizeof(params));
    encoder->GetDefaultParams(&params);
    params.iUsageType = CAMERA_VIDEO_REAL_TIME;
    params.iPicWidth = options.width;
    params.iPicHeight = options.height;
    params.fMaxFrameRate = static_cast<float>(options.fps);
    params.iTargetBitrate = options.bitrateKbps * 1000;
    params.iRCMode = RC_BITRATE_MODE;
    params.iTemporalLayerNum = 1;
    params.iSpatialLayerNum = 1;
    params.bEnableFrameSkip = false;
    params.uiIntraPeriod = static_cast<unsigned int>(options.keyint);

    params.sSpatialLayers[0].iVideoWidth = options.width;
    params.sSpatialLayers[0].iVideoHeight = options.height;
    params.sSpatialLayers[0].fFrameRate = static_cast<float>(options.fps);
    params.sSpatialLayers[0].iSpatialBitrate = options.bitrateKbps * 1000;
    params.sSpatialLayers[0].iMaxSpatialBitrate = options.bitrateKbps * 1000;
    params.sSpatialLayers[0].sSliceArgument.uiSliceMode = SM_SINGLE_SLICE;
    params.sSpatialLayers[0].sSliceArgument.uiSliceNum = 1;

    const int init = encoder->InitializeExt(&params);
    if (init != cmResultSuccess)
    {
        std::cerr << "OpenH264 InitializeExt failed. code=" << init << std::endl;
        api.destroyEncoder(encoder);
        return 5;
    }

    int videoFormat = videoFormatI420;
    encoder->SetOption(ENCODER_OPTION_DATAFORMAT, &videoFormat);

    const size_t frameBytes = static_cast<size_t>(options.width) * static_cast<size_t>(options.height) * 3 / 2;
    std::vector<uint8_t> frame(frameBytes);
    uint64_t framesEncoded = 0;

    while (ReadFrame(frame))
    {
        SSourcePicture picture;
        std::memset(&picture, 0, sizeof(picture));
        picture.iPicWidth = options.width;
        picture.iPicHeight = options.height;
        picture.iColorFormat = videoFormatI420;
        picture.iStride[0] = options.width;
        picture.iStride[1] = options.width / 2;
        picture.iStride[2] = options.width / 2;
        picture.pData[0] = frame.data();
        picture.pData[1] = frame.data() + static_cast<size_t>(options.width) * static_cast<size_t>(options.height);
        picture.pData[2] = picture.pData[1] + static_cast<size_t>(options.width) * static_cast<size_t>(options.height) / 4;

        SFrameBSInfo info;
        std::memset(&info, 0, sizeof(info));
        const int encoded = encoder->EncodeFrame(&picture, &info);
        if (encoded != cmResultSuccess)
        {
            std::cerr << "OpenH264 EncodeFrame failed. code=" << encoded << std::endl;
            encoder->Uninitialize();
            api.destroyEncoder(encoder);
            return 6;
        }

        WriteAccessUnit(info);
        ++framesEncoded;
    }

    std::cerr << "OpenH264 probe helper finished. frames=" << framesEncoded << std::endl;
    encoder->Uninitialize();
    api.destroyEncoder(encoder);
    return 0;
}
