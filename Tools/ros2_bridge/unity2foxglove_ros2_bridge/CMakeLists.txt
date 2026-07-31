cmake_minimum_required(VERSION 3.12)
project(unity2foxglove_ros2_bridge)

if(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  add_compile_options(-Wall -Wextra -Wpedantic)
endif()

find_package(ament_cmake REQUIRED)
find_package(rclcpp REQUIRED)
find_package(nlohmann_json REQUIRED)

add_library(u2r2_protocol STATIC
  src/u2r2_protocol.cpp
  src/u2r2_protocol_authority.cpp
)
target_compile_features(u2r2_protocol PUBLIC cxx_std_20)
target_include_directories(u2r2_protocol PUBLIC include)
target_link_libraries(u2r2_protocol PUBLIC
  nlohmann_json::nlohmann_json
)

add_library(bridge_runtime STATIC
  src/bridge_lifecycle.cpp
  src/bridge_process.cpp
  src/bridge_session.cpp
)
target_compile_features(bridge_runtime PUBLIC cxx_std_20)
target_include_directories(bridge_runtime PUBLIC include)
target_link_libraries(bridge_runtime PUBLIC
  u2r2_protocol
  rclcpp::rclcpp
)
if(WIN32)
  target_link_libraries(bridge_runtime PUBLIC ws2_32)
endif()

add_executable(unity2foxglove_ros2_bridge
  src/unity2foxglove_ros2_bridge.cpp
)

target_compile_features(unity2foxglove_ros2_bridge PUBLIC cxx_std_20)
target_link_libraries(unity2foxglove_ros2_bridge
  bridge_runtime
  rclcpp::rclcpp
  nlohmann_json::nlohmann_json
)
if(WIN32)
  target_link_libraries(unity2foxglove_ros2_bridge ws2_32)
endif()

install(TARGETS unity2foxglove_ros2_bridge
  DESTINATION lib/${PROJECT_NAME}
)

install(DIRECTORY launch
  DESTINATION share/${PROJECT_NAME}
)

install(DIRECTORY scripts
  DESTINATION share/${PROJECT_NAME}
  USE_SOURCE_PERMISSIONS
)

if(BUILD_TESTING)
  find_package(ament_cmake_gtest REQUIRED)
  ament_add_gtest(test_bridge_smoke
    test/test_bridge_smoke.cpp
  )
  target_compile_features(test_bridge_smoke PUBLIC cxx_std_20)
  target_link_libraries(test_bridge_smoke
    bridge_runtime
    rclcpp::rclcpp
    nlohmann_json::nlohmann_json
  )
  target_compile_definitions(test_bridge_smoke PRIVATE
    U2R2_PROTOCOL_FIXTURE_PATH="${CMAKE_CURRENT_SOURCE_DIR}/test/fixtures/u2r2_protocol_vectors.json"
  )
  if(WIN32)
    target_link_libraries(test_bridge_smoke ws2_32)
  endif()

  ament_add_gtest(test_u2r2_protocol
    test/test_u2r2_protocol.cpp
  )
  target_compile_features(test_u2r2_protocol PUBLIC cxx_std_20)
  target_include_directories(test_u2r2_protocol PRIVATE include)
  target_link_libraries(test_u2r2_protocol
    u2r2_protocol
  )
  target_compile_definitions(test_u2r2_protocol PRIVATE
    U2R2_PROTOCOL_FIXTURE_PATH="${CMAKE_CURRENT_SOURCE_DIR}/test/fixtures/u2r2_protocol_vectors.json"
  )

  ament_add_gtest(test_u2r2_protocol_authority
    test/test_u2r2_protocol_authority.cpp
  )
  target_compile_features(test_u2r2_protocol_authority PUBLIC cxx_std_20)
  target_include_directories(test_u2r2_protocol_authority PRIVATE include)
  target_link_libraries(test_u2r2_protocol_authority
    u2r2_protocol
  )

  ament_add_gtest(test_bridge_lifecycle
    test/test_bridge_lifecycle.cpp
  )
  target_compile_features(test_bridge_lifecycle PUBLIC cxx_std_20)
  target_link_libraries(test_bridge_lifecycle
    bridge_runtime
  )
  if(WIN32)
    target_link_libraries(test_bridge_lifecycle ws2_32)
  endif()

  ament_add_gtest(test_bridge_session
    test/test_bridge_session.cpp
  )
  target_compile_features(test_bridge_session PUBLIC cxx_std_20)
  target_link_libraries(test_bridge_session
    bridge_runtime
  )
  target_compile_definitions(test_u2r2_protocol_authority PRIVATE
    U2R2_PROTOCOL_FIXTURE_PATH="${CMAKE_CURRENT_SOURCE_DIR}/test/fixtures/u2r2_protocol_vectors.json"
  )

  add_executable(phase186_origin_suppression_probe
    test/test_origin_suppression.cpp
  )
  target_compile_features(phase186_origin_suppression_probe PUBLIC cxx_std_20)
  target_link_libraries(phase186_origin_suppression_probe
    rclcpp::rclcpp
  )
endif()

ament_package()
