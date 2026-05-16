// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/native/draco_native
// Purpose: Windows native plugin entry points for Draco point-cloud encoding.

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <exception>
#include <limits>

#include "draco/attributes/geometry_attribute.h"
#include "draco/compression/encode.h"
#include "draco/core/encoder_buffer.h"
#include "draco/point_cloud/point_cloud.h"

namespace {

constexpr int kPositionQuantizationBits = 11;
constexpr int kCompressionLevelSevenSpeed = 3;
constexpr int kMaxPointCount = 4 * 1000 * 1000;
constexpr char kVersion[] =
    "Unity2FoxgloveDracoNative;draco=1.5.7-40-ga94bf7d;commit=a94bf7dc1f7abc6ea212f8c4979e9bb4a61c4281";

enum ResultCode {
  kOk = 0,
  kInvalidArgument = -1,
  kPointCountTooLarge = -2,
  kEncodeFailed = -3,
  kOutputTooSmall = -4,
  kUnhandledException = -5,
};

bool EncodePointCloud(const float* xyz, int point_count,
                      draco::EncoderBuffer* buffer) {
  draco::PointCloud cloud;
  cloud.set_num_points(point_count);

  draco::GeometryAttribute position_attribute;
  position_attribute.Init(draco::GeometryAttribute::POSITION, nullptr, 3,
                          draco::DT_FLOAT32, false, sizeof(float) * 3, 0);

  const int position_id =
      cloud.AddAttribute(position_attribute, true, point_count);
  if (position_id < 0) {
    return false;
  }

  draco::PointAttribute* position = cloud.attribute(position_id);
  for (int i = 0; i < point_count; ++i) {
    position->SetAttributeValue(draco::AttributeValueIndex(i), &xyz[i * 3]);
  }

  draco::Encoder encoder;
  encoder.SetAttributeQuantization(draco::GeometryAttribute::POSITION,
                                   kPositionQuantizationBits);
  encoder.SetSpeedOptions(kCompressionLevelSevenSpeed,
                          kCompressionLevelSevenSpeed);

  const draco::Status status = encoder.EncodePointCloudToBuffer(cloud, buffer);
  return status.ok();
}

}  // namespace

extern "C" __declspec(dllexport) int U2FDracoGetVersion(char* buffer,
                                                        int capacity) {
  if (buffer == nullptr || capacity <= 0) {
    return kInvalidArgument;
  }

  const int version_length = static_cast<int>(sizeof(kVersion) - 1);
  const int copy_length = std::min(version_length, capacity - 1);
  std::memcpy(buffer, kVersion, static_cast<size_t>(copy_length));
  buffer[copy_length] = '\0';
  return version_length;
}

extern "C" __declspec(dllexport) int U2FDracoEncodePointCloud(
    const float* xyz, int point_count, std::uint8_t* output,
    int output_capacity, int* bytes_written) {
  if (bytes_written != nullptr) {
    *bytes_written = 0;
  }

  if (xyz == nullptr || output == nullptr || bytes_written == nullptr ||
      point_count <= 0 || output_capacity <= 0) {
    return kInvalidArgument;
  }

  if (point_count > kMaxPointCount) {
    return kPointCountTooLarge;
  }

  try {
    draco::EncoderBuffer buffer;
    if (!EncodePointCloud(xyz, point_count, &buffer)) {
      return kEncodeFailed;
    }

    if (buffer.size() >
        static_cast<size_t>(std::numeric_limits<int>::max())) {
      return kEncodeFailed;
    }

    const int payload_size = static_cast<int>(buffer.size());
    *bytes_written = payload_size;
    if (payload_size > output_capacity) {
      return kOutputTooSmall;
    }

    std::memcpy(output, buffer.data(), static_cast<size_t>(payload_size));
    return kOk;
  } catch (const std::exception&) {
    return kUnhandledException;
  } catch (...) {
    return kUnhandledException;
  }
}
