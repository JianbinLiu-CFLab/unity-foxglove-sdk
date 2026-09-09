// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/native/draco_probe
// Purpose: Draco POINT_CLOUD probe encoder helper.

#include <cstdint>
#include <cmath>
#include <cstring>
#include <iostream>
#include <limits>
#include <vector>

#include "draco/attributes/geometry_attribute.h"
#include "draco/compression/encode.h"
#include "draco/core/encoder_buffer.h"
#include "draco/point_cloud/point_cloud.h"

namespace {

constexpr int kPositionQuantizationBits = 11;
constexpr int kCompressionLevelSevenSpeed = 3;
constexpr uint32_t kMaxPointCount = 4u * 1000u * 1000u;

enum class ReadStatus {
  kOk,
  kCleanEof,
  kTruncated,
};

ReadStatus ReadExact(char* dst, size_t size) {
  std::cin.read(dst, static_cast<std::streamsize>(size));
  if (std::cin.gcount() == static_cast<std::streamsize>(size)) {
    return ReadStatus::kOk;
  }
  return std::cin.gcount() == 0 && std::cin.eof()
             ? ReadStatus::kCleanEof
             : ReadStatus::kTruncated;
}

ReadStatus ReadUint32(uint32_t* value) {
  uint8_t bytes[4] = {};
  const ReadStatus status =
      ReadExact(reinterpret_cast<char*>(bytes), sizeof(bytes));
  if (status != ReadStatus::kOk) {
    return status;
  }

  *value = static_cast<uint32_t>(bytes[0]) |
           (static_cast<uint32_t>(bytes[1]) << 8) |
           (static_cast<uint32_t>(bytes[2]) << 16) |
           (static_cast<uint32_t>(bytes[3]) << 24);
  return ReadStatus::kOk;
}

void WriteUint32(uint32_t value) {
  uint8_t bytes[4] = {
      static_cast<uint8_t>(value & 0xff),
      static_cast<uint8_t>((value >> 8) & 0xff),
      static_cast<uint8_t>((value >> 16) & 0xff),
      static_cast<uint8_t>((value >> 24) & 0xff),
  };
  std::cout.write(reinterpret_cast<const char*>(bytes), sizeof(bytes));
}

bool EncodePointCloud(const std::vector<float>& xyz, uint32_t point_count,
                      draco::EncoderBuffer* buffer) {
  for (size_t i = 0; i < xyz.size(); ++i) {
    if (!std::isfinite(xyz[i])) {
      std::cerr << "non-finite XYZ value at float index " << i << std::endl;
      return false;
    }
  }

  draco::PointCloud cloud;
  cloud.set_num_points(point_count);

  draco::GeometryAttribute position_attribute;
  position_attribute.Init(draco::GeometryAttribute::POSITION, nullptr, 3,
                          draco::DT_FLOAT32, false, sizeof(float) * 3, 0);

  const int position_id =
      cloud.AddAttribute(position_attribute, true, point_count);
  if (position_id < 0) {
    std::cerr << "failed to add POSITION attribute" << std::endl;
    return false;
  }

  draco::PointAttribute* position = cloud.attribute(position_id);
  for (uint32_t i = 0; i < point_count; ++i) {
    position->SetAttributeValue(draco::AttributeValueIndex(i),
                                &xyz[static_cast<size_t>(i) * 3]);
  }

  draco::Encoder encoder;
  encoder.SetAttributeQuantization(draco::GeometryAttribute::POSITION,
                                   kPositionQuantizationBits);
  encoder.SetSpeedOptions(kCompressionLevelSevenSpeed,
                          kCompressionLevelSevenSpeed);

  const draco::Status status = encoder.EncodePointCloudToBuffer(cloud, buffer);
  if (!status.ok()) {
    std::cerr << "Draco POINT_CLOUD encode failed: " << status.error_msg()
              << std::endl;
    return false;
  }

  return true;
}

ReadStatus ProcessOneFrame(std::vector<float>* xyz) {
  uint32_t point_count = 0;
  const ReadStatus header_status = ReadUint32(&point_count);
  if (header_status != ReadStatus::kOk) {
    return header_status;
  }

  if (point_count > kMaxPointCount) {
    std::cerr << "invalid point_count: " << point_count << std::endl;
    return ReadStatus::kTruncated;
  }

  if (point_count == 0) {
    WriteUint32(0);
    std::cout.flush();
    return ReadStatus::kOk;
  }

  const size_t float_count = static_cast<size_t>(point_count) * 3;
  xyz->resize(float_count);
  if (ReadExact(reinterpret_cast<char*>(xyz->data()),
                float_count * sizeof(float)) != ReadStatus::kOk) {
    std::cerr << "stdin ended mid XYZ payload" << std::endl;
    return ReadStatus::kTruncated;
  }

  draco::EncoderBuffer buffer;
  if (!EncodePointCloud(*xyz, point_count, &buffer)) {
    return ReadStatus::kTruncated;
  }

  if (buffer.size() >
      static_cast<size_t>(std::numeric_limits<uint32_t>::max())) {
    std::cerr << "payload_length exceeds uint32" << std::endl;
    return ReadStatus::kTruncated;
  }

  const uint32_t payload_length = static_cast<uint32_t>(buffer.size());
  WriteUint32(payload_length);
  std::cout.write(buffer.data(), static_cast<std::streamsize>(buffer.size()));
  std::cout.flush();
  return ReadStatus::kOk;
}

}  // namespace

int main() {
  std::ios::sync_with_stdio(false);

  std::vector<float> xyz;
  while (true) {
    const ReadStatus status = ProcessOneFrame(&xyz);
    if (status == ReadStatus::kCleanEof) {
      return 0;
    }
    if (status != ReadStatus::kOk) {
      return 2;
    }
  }

  return 0;
}
