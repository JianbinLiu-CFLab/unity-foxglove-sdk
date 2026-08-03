// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Process-owned ROS node, executor, and spin thread.

#pragma once

#include <memory>
#include <string>

#include <rclcpp/context.hpp>
#include <rclcpp/node.hpp>

namespace unity2foxglove::ros2_bridge::runtime
{
class ProcessRosOwner final
{
public:
  ProcessRosOwner(
    std::string node_name,
    std::shared_ptr<rclcpp::Context> context);
  ~ProcessRosOwner();
  ProcessRosOwner(const ProcessRosOwner &) = delete;
  ProcessRosOwner & operator=(const ProcessRosOwner &) = delete;

  rclcpp::Node::SharedPtr require_node();
  rclcpp::Node::SharedPtr current_node() const;
  bool spin_thread_running() const;
  bool stop();

private:
  struct Impl;
  std::unique_ptr<Impl> impl_;
};
}  // namespace unity2foxglove::ros2_bridge::runtime
