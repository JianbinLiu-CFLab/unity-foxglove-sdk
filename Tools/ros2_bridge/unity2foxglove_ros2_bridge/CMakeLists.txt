cmake_minimum_required(VERSION 3.8)
project(unity2foxglove_ros2_bridge)

if(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  add_compile_options(-Wall -Wextra -Wpedantic)
endif()

find_package(ament_cmake REQUIRED)
find_package(rclcpp REQUIRED)
find_package(nlohmann_json REQUIRED)

add_executable(unity2foxglove_ros2_bridge
  src/unity2foxglove_ros2_bridge.cpp
)

target_compile_features(unity2foxglove_ros2_bridge PUBLIC cxx_std_17)
target_link_libraries(unity2foxglove_ros2_bridge
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
  target_compile_features(test_bridge_smoke PUBLIC cxx_std_17)
  target_link_libraries(test_bridge_smoke
    rclcpp::rclcpp
    nlohmann_json::nlohmann_json
  )
  target_compile_definitions(test_bridge_smoke PRIVATE
    U2R2_PROTOCOL_FIXTURE_PATH="${CMAKE_CURRENT_SOURCE_DIR}/test/fixtures/u2r2_protocol_vectors.json"
  )
  if(WIN32)
    target_link_libraries(test_bridge_smoke ws2_32)
  endif()

  add_executable(phase186_origin_suppression_probe
    test/test_origin_suppression.cpp
  )
  target_compile_features(phase186_origin_suppression_probe PUBLIC cxx_std_17)
  target_link_libraries(phase186_origin_suppression_probe
    rclcpp::rclcpp
  )
endif()

ament_package()
