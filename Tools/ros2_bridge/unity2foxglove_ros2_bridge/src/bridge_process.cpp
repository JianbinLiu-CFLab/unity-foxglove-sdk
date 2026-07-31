// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Process-owned ROS node, executor, and spin thread.

#include "unity2foxglove_ros2_bridge/bridge_process.hpp"

#include <atomic>
#include <chrono>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <utility>

#include <rclcpp/executor_options.hpp>
#include <rclcpp/executors/single_threaded_executor.hpp>
#include <rclcpp/node_options.hpp>

namespace unity2foxglove::ros2_bridge::runtime
{
struct ProcessRosOwner::Impl final
{
  Impl(std::string value, std::shared_ptr<rclcpp::Context> owner_context)
  : node_name(std::move(value)), context(std::move(owner_context))
  {
    if (node_name.empty()) {
      throw std::invalid_argument("process ROS node name is required");
    }
    if (!context) {
      throw std::invalid_argument("process ROS context is required");
    }
  }

  mutable std::mutex mutex;
  std::string node_name;
  std::shared_ptr<rclcpp::Context> context;
  rclcpp::Node::SharedPtr node;
  std::shared_ptr<rclcpp::executors::SingleThreadedExecutor> executor;
  std::thread spin_thread;
  std::atomic<bool> stop_requested{false};
  bool running{false};
  bool stopped{false};
};

ProcessRosOwner::ProcessRosOwner(
  std::string node_name,
  std::shared_ptr<rclcpp::Context> context)
: impl_(std::make_unique<Impl>(
      std::move(node_name),
      std::move(context)))
{
}

ProcessRosOwner::~ProcessRosOwner()
{
  try {
    stop();
  } catch (...) {
  }
}

rclcpp::Node::SharedPtr ProcessRosOwner::require_node()
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  if (impl_->stopped) {
    throw std::logic_error("process ROS owner is stopped");
  }
  if (impl_->node) {
    return impl_->node;
  }
  if (!rclcpp::ok(impl_->context)) {
    throw std::runtime_error("process ROS context is not active");
  }

  rclcpp::NodeOptions node_options;
  node_options.context(impl_->context);
  auto node = std::make_shared<rclcpp::Node>(
    impl_->node_name,
    node_options);
  rclcpp::ExecutorOptions executor_options;
  executor_options.context = impl_->context;
  auto executor =
    std::make_shared<rclcpp::executors::SingleThreadedExecutor>(
    executor_options);
  executor->add_node(node);
  const auto context = impl_->context;
  auto * stop_requested = &impl_->stop_requested;
  std::thread spin_thread;
  try {
    spin_thread = std::thread(
      [executor, context, stop_requested]() {
        while (!stop_requested->load() && rclcpp::ok(context)) {
          executor->spin_once(std::chrono::milliseconds(50));
        }
      });
  } catch (...) {
    executor->remove_node(node);
    throw;
  }
  impl_->node = node;
  impl_->executor = executor;
  impl_->spin_thread = std::move(spin_thread);
  impl_->running = true;
  return impl_->node;
}

rclcpp::Node::SharedPtr ProcessRosOwner::current_node() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->node;
}

bool ProcessRosOwner::spin_thread_running() const
{
  std::lock_guard<std::mutex> lock(impl_->mutex);
  return impl_->running;
}

bool ProcessRosOwner::stop()
{
  std::shared_ptr<rclcpp::executors::SingleThreadedExecutor> executor;
  rclcpp::Node::SharedPtr node;
  std::thread spin_thread;
  {
    std::lock_guard<std::mutex> lock(impl_->mutex);
    if (impl_->stopped) {
      return false;
    }
    impl_->stopped = true;
    impl_->running = false;
    impl_->stop_requested.store(true);
    executor = std::move(impl_->executor);
    node = std::move(impl_->node);
    spin_thread = std::move(impl_->spin_thread);
  }

  if (executor) {
    executor->cancel();
  }
  if (spin_thread.joinable()) {
    spin_thread.join();
  }
  if (executor && node) {
    executor->remove_node(node);
  }
  node.reset();
  executor.reset();
  return true;
}
}  // namespace unity2foxglove::ros2_bridge::runtime
