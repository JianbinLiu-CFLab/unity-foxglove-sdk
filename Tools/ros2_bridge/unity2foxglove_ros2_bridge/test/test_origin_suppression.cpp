// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Windows-native cross-RMW probe for a portable ROS publisher-origin primitive.

#include <rclcpp/rclcpp.hpp>

#include <rmw/rmw.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

namespace
{

using namespace std::chrono_literals;

struct Arguments
{
  std::string role;
  std::string topic;
  std::string type;
  std::vector<std::uint8_t> payload;
  std::chrono::milliseconds timeout{15000};
};

std::vector<std::uint8_t> parse_hex(const std::string & text)
{
  if (text.empty() || (text.size() % 2U) != 0U) {
    throw std::invalid_argument("--payload-hex must contain complete non-empty bytes");
  }
  std::vector<std::uint8_t> bytes;
  bytes.reserve(text.size() / 2U);
  for (std::size_t i = 0; i < text.size(); i += 2U) {
    const auto digit = [](char value) -> unsigned int {
        if (value >= '0' && value <= '9') {
          return static_cast<unsigned int>(value - '0');
        }
        if (value >= 'a' && value <= 'f') {
          return static_cast<unsigned int>(value - 'a' + 10);
        }
        if (value >= 'A' && value <= 'F') {
          return static_cast<unsigned int>(value - 'A' + 10);
        }
        throw std::invalid_argument("--payload-hex contains a non-hexadecimal character");
      };
    bytes.push_back(static_cast<std::uint8_t>((digit(text[i]) << 4U) | digit(text[i + 1U])));
  }
  return bytes;
}

Arguments parse_arguments(int argc, char ** argv)
{
  Arguments result;
  for (int i = 1; i < argc; ++i) {
    const std::string option(argv[i]);
    const auto next = [&]() -> std::string {
        if (++i >= argc) {
          throw std::invalid_argument("missing value after " + option);
        }
        return std::string(argv[i]);
      };
    if (option == "--role") {
      result.role = next();
    } else if (option == "--topic") {
      result.topic = next();
    } else if (option == "--type") {
      result.type = next();
    } else if (option == "--payload-hex") {
      result.payload = parse_hex(next());
    } else if (option == "--timeout-ms") {
      const auto parsed = std::stoll(next());
      if (parsed <= 0 || parsed > 120000) {
        throw std::invalid_argument("--timeout-ms must be in [1, 120000]");
      }
      result.timeout = std::chrono::milliseconds(parsed);
    } else {
      throw std::invalid_argument("unknown argument: " + option);
    }
  }
  if (result.role != "subscriber" && result.role != "publisher") {
    throw std::invalid_argument("--role must be subscriber or publisher");
  }
  if (result.topic.empty() || result.topic.front() != '/') {
    throw std::invalid_argument("--topic must be an absolute ROS topic");
  }
  if (result.type.empty()) {
    throw std::invalid_argument("--type is required");
  }
  if (result.payload.empty()) {
    throw std::invalid_argument("--payload-hex is required");
  }
  return result;
}

rclcpp::SerializedMessage serialized(const std::vector<std::uint8_t> & payload)
{
  rclcpp::SerializedMessage result(payload.size());
  auto & raw = result.get_rcl_serialized_message();
  if (raw.buffer_capacity < payload.size()) {
    throw std::runtime_error("serialized-message capacity is smaller than the payload");
  }
  std::memcpy(raw.buffer, payload.data(), payload.size());
  raw.buffer_length = payload.size();
  return result;
}

bool same_gid(const rmw_gid_t & left, const rmw_gid_t & right)
{
  return std::memcmp(left.data, right.data, RMW_GID_STORAGE_SIZE) == 0;
}

template<typename Predicate>
bool wait_until(
  std::chrono::steady_clock::time_point deadline,
  Predicate predicate)
{
  while (rclcpp::ok() && std::chrono::steady_clock::now() < deadline) {
    if (predicate()) {
      return true;
    }
    std::this_thread::sleep_for(10ms);
  }
  return false;
}

int run_publisher(const Arguments & arguments)
{
  auto node = std::make_shared<rclcpp::Node>("phase186_external_origin_publisher");
  auto publisher = node->create_generic_publisher(
    arguments.topic,
    arguments.type,
    rclcpp::QoS(10).reliable());
  const auto deadline = std::chrono::steady_clock::now() + arguments.timeout;
  if (!wait_until(deadline, [&]() {return publisher->get_subscription_count() >= 2U;})) {
    std::cerr << "external publisher did not discover both probe subscriptions" << std::endl;
    return 2;
  }
  auto message = serialized(arguments.payload);
  for (int i = 0; i < 20; ++i) {
    publisher->publish(message);
    std::this_thread::sleep_for(25ms);
  }
  std::cout
    << "{\"role\":\"publisher\",\"published\":20,\"observedRmw\":\""
    << rmw_get_implementation_identifier()
    << "\"}" << std::endl;
  return 0;
}

int run_subscriber(const Arguments & arguments)
{
  auto node = std::make_shared<rclcpp::Node>("phase186_origin_subscriber");
  auto local_publisher = node->create_generic_publisher(
    arguments.topic,
    arguments.type,
    rclcpp::QoS(10).reliable());
  const auto noop = [](std::shared_ptr<rclcpp::SerializedMessage>) {};
  auto all_subscription = node->create_generic_subscription(
    arguments.topic,
    arguments.type,
    rclcpp::QoS(10).reliable(),
    noop);
  rclcpp::SubscriptionOptions ignore_options;
  ignore_options.ignore_local_publications = true;
  auto external_only_subscription = node->create_generic_subscription(
    arguments.topic,
    arguments.type,
    rclcpp::QoS(10).reliable(),
    noop,
    ignore_options);

  const auto deadline = std::chrono::steady_clock::now() + arguments.timeout;
  if (!wait_until(
      deadline,
      [&]() {
        return local_publisher->get_subscription_count() >= 2U &&
               all_subscription->get_publisher_count() >= 1U &&
               external_only_subscription->get_publisher_count() >= 1U;
      }))
  {
    std::cerr << "local endpoints did not discover each other" << std::endl;
    return 3;
  }

  auto local_message = serialized(arguments.payload);
  bool local_seen = false;
  bool local_gid_matched = false;
  bool ignore_local_saw_local = false;
  const auto local_deadline = std::min(deadline, std::chrono::steady_clock::now() + 3000ms);
  for (int attempt = 0; attempt < 20 && !local_seen; ++attempt) {
    local_publisher->publish(local_message);
    wait_until(
      std::min(local_deadline, std::chrono::steady_clock::now() + 100ms),
      [&]() {
        rclcpp::SerializedMessage taken;
        rclcpp::MessageInfo info;
        if (!all_subscription->take_serialized(taken, info)) {
          return false;
        }
        local_seen = true;
        local_gid_matched = same_gid(
          local_publisher->get_gid(),
          info.get_rmw_message_info().publisher_gid);
        return true;
      });
  }
  const auto quiet_deadline = std::min(deadline, std::chrono::steady_clock::now() + 500ms);
  while (std::chrono::steady_clock::now() < quiet_deadline) {
    rclcpp::SerializedMessage ignored;
    rclcpp::MessageInfo ignored_info;
    if (external_only_subscription->take_serialized(ignored, ignored_info)) {
      ignore_local_saw_local = true;
      break;
    }
    std::this_thread::sleep_for(10ms);
  }
  if (!local_seen || !local_gid_matched || ignore_local_saw_local) {
    std::cerr << "local publisher origin was not classified consistently" << std::endl;
    return 4;
  }

  std::cout << "PHASE186_ORIGIN_PROBE_READY" << std::endl;

  bool external_seen = false;
  bool external_gid_matched = true;
  bool ignore_local_saw_external = false;
  while (rclcpp::ok() && std::chrono::steady_clock::now() < deadline) {
    rclcpp::SerializedMessage taken;
    rclcpp::MessageInfo info;
    if (all_subscription->take_serialized(taken, info)) {
      const bool matches_local = same_gid(
        local_publisher->get_gid(),
        info.get_rmw_message_info().publisher_gid);
      if (!matches_local) {
        external_seen = true;
        external_gid_matched = false;
      }
    }
    rclcpp::SerializedMessage external_only;
    rclcpp::MessageInfo external_info;
    if (external_only_subscription->take_serialized(external_only, external_info)) {
      if (!same_gid(
          local_publisher->get_gid(),
          external_info.get_rmw_message_info().publisher_gid))
      {
        ignore_local_saw_external = true;
      }
    }
    if (external_seen && ignore_local_saw_external) {
      break;
    }
    std::this_thread::sleep_for(10ms);
  }

  std::cout
    << "{\"role\":\"subscriber\","
    << "\"mechanism\":\"publisher_gid_take_serialized\","
    << "\"localSeen\":" << (local_seen ? "true" : "false") << ","
    << "\"localGidMatched\":" << (local_gid_matched ? "true" : "false") << ","
    << "\"ignoreLocalSawLocal\":" << (ignore_local_saw_local ? "true" : "false") << ","
    << "\"externalSeen\":" << (external_seen ? "true" : "false") << ","
    << "\"externalGidMatched\":" << (external_gid_matched ? "true" : "false") << ","
    << "\"ignoreLocalSawExternal\":" << (ignore_local_saw_external ? "true" : "false") << ","
    << "\"observedRmw\":\"" << rmw_get_implementation_identifier() << "\","
    << "\"topic\":\"" << arguments.topic << "\","
    << "\"type\":\"" << arguments.type << "\""
    << "}" << std::endl;
  return external_seen && !external_gid_matched && ignore_local_saw_external ? 0 : 5;
}

}  // namespace

int main(int argc, char ** argv)
{
  try {
    const auto arguments = parse_arguments(argc, argv);
    rclcpp::init(argc, argv);
    int result = 1;
    try {
      result = arguments.role == "subscriber" ?
        run_subscriber(arguments) :
        run_publisher(arguments);
    } catch (...) {
      rclcpp::shutdown();
      throw;
    }
    rclcpp::shutdown();
    return result;
  } catch (const std::exception & exception) {
    std::cerr << "phase186 origin probe failed: " << exception.what() << std::endl;
    return 1;
  }
}
