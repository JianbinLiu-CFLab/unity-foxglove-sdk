// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Experimental localhost TCP to ROS 2 GenericPublisher sidecar.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#include <errno.h>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/select.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

#include <algorithm>
#include <array>
#include <atomic>
#include <cctype>
#include <chrono>
#include <cstddef>
#include <cstdio>
#include <cstring>
#include <functional>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

#include <nlohmann/json.hpp>
#include <rclcpp/rclcpp.hpp>
#include <rclcpp/contexts/default_context.hpp>
#include <rclcpp/generic_subscription.hpp>
#include <rclcpp/serialized_message.hpp>
#include <rclcpp/typesupport_helpers.hpp>
#include <rclcpp/version.h>

#include "unity2foxglove_ros2_bridge/bridge_lifecycle.hpp"
#include "unity2foxglove_ros2_bridge/bridge_origin.hpp"
#include "unity2foxglove_ros2_bridge/bridge_process.hpp"
#include "unity2foxglove_ros2_bridge/bridge_session.hpp"

namespace
{
#include "unity2foxglove_ros2_bridge/SocketAndOptions.inl"
#include "unity2foxglove_ros2_bridge/ModelsAndBinding.inl"
#include "unity2foxglove_ros2_bridge/FramingAndParsing.inl"
#include "unity2foxglove_ros2_bridge/PublisherRuntime.inl"
#include "unity2foxglove_ros2_bridge/SessionWriter.inl"
#include "unity2foxglove_ros2_bridge/ProtocolDispatchAndMainSupport.inl"
}  // namespace

#ifndef UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
int main(int argc, char ** argv)
{
#ifdef _WIN32
  std::unique_ptr<WinsockRuntime> winsock;
  try {
    winsock = std::make_unique<WinsockRuntime>();
  } catch (const std::exception & ex) {
    std::fprintf(
      stderr,
      "[unity2foxglove_ros2_bridge] %s\n",
      ex.what());
    return 1;
  }
#endif

  auto non_ros_args = rclcpp::init_and_remove_ros_arguments(argc, argv);
  auto logger = rclcpp::get_logger(kSidecarName);

  try {
    const auto options = parse_args(non_ros_args);
    const auto limits = u2r2::ProtocolLimits::defaults();
    bridge_runtime::ProcessConnectionAuthority authority(limits);
    const auto context = rclcpp::contexts::get_global_default_context();
    bridge_runtime::ProcessRosOwner ros_owner(kSidecarName, context);
    ConnectionWorkerSet workers(limits.max_connections());
    const BridgeGenerationFactory generation_factory =
      [&ros_owner,
      payload_format = options.payload_format,
      maximum_publishers = limits.max_contracts()]() {
        return std::make_unique<BridgeNode>(
          ros_owner.require_node(),
          payload_format,
          maximum_publishers);
      };
    ScopedFd listen_fd(create_listen_socket(options.host, options.port, logger));

    RCLCPP_INFO(
      logger,
      "[unity2foxglove_ros2_bridge] listening on %s:%d",
      options.host.c_str(),
      options.port);

    while (rclcpp::ok()) {
      workers.reap_completed();
      auto accepted = accept_with_timeout(listen_fd.get());
      if (!accepted.valid()) {
        continue;
      }

      ScopedFd candidate(accepted.fd);
      const auto peer_length = accepted.peer_length > 0
        ? static_cast<size_t>(accepted.peer_length)
        : 0U;
      if (!bridge_runtime::is_ipv4_loopback_peer(
          reinterpret_cast<const sockaddr *>(&accepted.peer),
          peer_length))
      {
        RCLCPP_WARN(
          logger,
          "[unity2foxglove_ros2_bridge] rejected non-IPv4-loopback peer after accept");
        continue;
      }

      auto accepted_lease = authority.try_acquire_accepted();
      if (!accepted_lease) {
        RCLCPP_WARN(
          logger,
          "[unity2foxglove_ros2_bridge] busy: preclassification connection capacity exhausted");
        continue;
      }

      auto client =
        std::make_shared<ScopedFd>(candidate.release());
      RCLCPP_INFO(logger, "[unity2foxglove_ros2_bridge] client connected");
      workers.start(
        client,
        [client,
        accepted_lease = std::move(*accepted_lease),
        &authority,
        generation_factory,
        logger,
        context]() mutable {
          try {
            process_owned_client(
              client->get(),
              authority,
              generation_factory,
              logger,
              [context]() {return rclcpp::ok(context);});
          } catch (const ClientReadTimeoutException & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] %s",
              ex.what());
          } catch (const u2r2::ProtocolError & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] protocol %s: %s",
              ex.code().c_str(),
              ex.what());
          } catch (const std::exception & ex) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] client session escaped: %s",
              ex.what());
          } catch (...) {
            RCLCPP_WARN(
              logger,
              "[unity2foxglove_ros2_bridge] client session escaped with an unknown exception");
          }
          accepted_lease.release();
          RCLCPP_INFO(
            logger,
            "[unity2foxglove_ros2_bridge] client disconnected");
        });
    }
    workers.stop_and_join_all();
    ros_owner.stop();
  } catch (const std::exception & ex) {
    RCLCPP_ERROR(logger, "[unity2foxglove_ros2_bridge] %s", ex.what());
    rclcpp::shutdown();
    return 1;
  }

  rclcpp::shutdown();
  return 0;
}
#endif
