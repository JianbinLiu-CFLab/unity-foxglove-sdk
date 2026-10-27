// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/ros2_bridge
// Purpose: Protocol logic tests for the Unity2Foxglove ROS 2 bridge sidecar.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

#include <gtest/gtest.h>

#include <algorithm>
#include <atomic>
#include <array>
#include <condition_variable>
#include <exception>
#include <fstream>
#include <limits>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

// Include the production translation unit directly to exercise internal parser helpers.
#define UNITY2FOXGLOVE_ROS2_BRIDGE_TESTING
#include "../src/unity2foxglove_ros2_bridge.cpp"

namespace
{
#ifndef U2R2_PROTOCOL_FIXTURE_PATH
#error "U2R2_PROTOCOL_FIXTURE_PATH must identify the shared v1 authority fixture"
#endif

#include "test_bridge_smoke/FixtureAndHelpers.inl"
#include "test_bridge_smoke/ProtocolContractTests.inl"
#include "test_bridge_smoke/LifecycleSetup.inl"
#include "test_bridge_smoke/SessionAndPublisherTests.inl"
#include "test_bridge_smoke/PublisherContractTests.inl"
#include "test_bridge_smoke/TransportIntegrationTests.inl"
