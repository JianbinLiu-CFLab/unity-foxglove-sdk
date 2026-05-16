# OpenH264 Probe Helper

This folder contains source for a Phase 80 spike helper executable. It is not a production SDK binary and it must be built locally.

## Local Source Boundary

Clone OpenH264 source into the ignored third-party workspace:

```powershell
git clone https://github.com/cisco/openh264.git third-party\openh264
```

The repository already ignores `third-party/`, so OpenH264 source and build outputs remain local. In documentation this workspace is referred to as `third-party/openh264`.

## Build Notes

OpenH264's documented Windows source build path may require MSVC Build Tools or Visual Studio C++ workload, Cygwin `make`, and NASM. If those tools are missing, record Phase 80 as `BLOCKED` with the exact missing tool.

After OpenH264 is built from source, compile `openh264_probe_encoder.cpp` against the local headers and library. The exact library output path can vary by OpenH264 build route, so adjust include and library paths to the local build.

Example shape:

```powershell
cl /EHsc /std:c++17 `
  /I third-party\openh264\codec\api\svc `
  Scripts\native\openh264_probe\openh264_probe_encoder.cpp `
  /link /LIBPATH:third-party\openh264 openh264.lib `
  /OUT:third-party\openh264_probe_encoder.exe
```

## Protocol

Input is raw I420 frames on stdin. Each frame is `width * height * 3 / 2` bytes.

Output is binary stdout:

- 4-byte little-endian unsigned payload length.
- `length` bytes of H.264 Annex B access-unit data.

Diagnostics are written to stderr only.

## Unity Probe

The Unity demo probe captures RGB24 frames, vertically flips the Unity readback rows during I420/YUV420p conversion, sends them to the helper, and publishes returned access units as `foxglove.CompressedVideo` with `format = h264`.

The probe topic is isolated at `/unity/camera/openh264_probe` so it does not collide with production JPEG, H.264 FFmpeg, or H.265 FFmpeg camera modes.

## Manual Smoke

1. Open the `Unity2Foxglove` SampleScene.
2. Add `Foxglove > Experimental > OpenH264 Source Probe Publisher`.
3. Set the helper executable path to a locally built helper under an ignored workspace such as `third-party/`.
4. Enter Play Mode and connect Foxglove.
5. Open `/unity/camera/openh264_probe` in an Image panel.
6. Record 10-20 seconds of MCAP and reopen it directly in Foxglove.
7. Confirm existing JPEG and FFmpeg camera modes are unchanged.

## Result States

- GREEN: source-built helper starts, live Foxglove playback works, MCAP playback works, and the SDK remains OpenH264-binary-free.
- YELLOW: helper emits H.264, but timing, orientation, SPS/PPS cadence, live playback, or MCAP playback needs follow-up.
- RED: helper builds/runs but cannot produce Foxglove-usable H.264 access units.
- BLOCKED: local OpenH264 source build or helper build cannot be completed with the available toolchain.

## Licensing Boundary

Phase 80 uses an OpenH264 source-only posture. Do not commit OpenH264 source, compiled OpenH264 libraries, or this helper executable. Cisco's prebuilt OpenH264 binaries have separate AVC/H.264 patent-license conditions, so this spike intentionally does not download, bundle, or install Cisco binaries.
