// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Scripts/native/draco_probe
// Purpose: Draco POINT_CLOUD probe encoder helper.

#include <cstdint>
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

bool ReadExact(char* dst, size_t size) {
  std::cin.read(dst, static_cast<std::streamsize>(size));
  return std::cin.gcount() == static_cast<std::streamsize>(size);
}

bool ReadUint32(uint32_t* value) {
  uint8_t bytes[4] = {};
  if (!ReadExact(reinterpret_cast<char*>(bytes), sizeof(bytes))) {
    return false;
  }

  *value = static_cast<uint32_t>(bytes[0]) |
           (static_cast<uint32_t>(bytes[1]) << 8) |
           (static_cast<uint32_t>(bytes[2]) << 16) |
           (static_cast<uint32_t>(bytes[3]) << 24);
  return true;
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

bool ProcessOneFrame() {
  uint32_t point_count = 0;
  if (!ReadUint32(&point_count)) {
    return false;
  }

  if (point_count > kMaxPointCount) {
    std::cerr << "invalid point_count: " << point_count << std::endl;
    return false;
  }

  if (point_count == 0) {
    std::cerr << "warning: zero-point frame; writing empty payload" << std::endl;
    WriteUint32(0);
    std::cout.flush();
    return true;
  }

  const size_t float_count = static_cast<size_t>(point_count) * 3;
  std::vector<float> xyz(float_count);
  if (!ReadExact(reinterpret_cast<char*>(xyz.data()),
                 float_count * sizeof(float))) {
    std::cerr << "stdin ended mid XYZ payload" << std::endl;
    return false;
  }

  draco::EncoderBuffer buffer;
  if (!EncodePointCloud(xyz, point_count, &buffer)) {
    return false;
  }

  if (buffer.size() >
      static_cast<size_t>(std::numeric_limits<uint32_t>::max())) {
    std::cerr << "payload_length exceeds uint32" << std::endl;
    return false;
  }

  const uint32_t payload_length = static_cast<uint32_t>(buffer.size());
  WriteUint32(payload_length);
  std::cout.write(buffer.data(), static_cast<std::streamsize>(buffer.size()));
  std::cout.flush();
  return true;
}

}  // namespace

int main() {
  std::ios::sync_with_stdio(false);

  while (std::cin.good()) {
    if (!ProcessOneFrame()) {
      return std::cin.eof() ? 0 : 1;
    }
  }

  return 0;
}
