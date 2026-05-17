cmake_minimum_required(VERSION 3.8)
project(unity2foxglove_ros2_bridge)

if(CMAKE_COMPILER_IS_GNUCXX OR CMAKE_CXX_COMPILER_ID MATCHES "Clang")
  add_compile_options(-Wall -Wextra -Wpedantic)
endif()

find_package(ament_cmake REQUIRED)
find_package(rclcpp REQUIRED)
find_package(foxglove_msgs REQUIRED)
find_package(nlohmann_json REQUIRED)

add_executable(unity2foxglove_ros2_bridge
  src/unity2foxglove_ros2_bridge.cpp
)

target_compile_features(unity2foxglove_ros2_bridge PUBLIC cxx_std_17)
ament_target_dependencies(unity2foxglove_ros2_bridge
  rclcpp
  foxglove_msgs
)
target_link_libraries(unity2foxglove_ros2_bridge
  nlohmann_json::nlohmann_json
)

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

ament_package()
