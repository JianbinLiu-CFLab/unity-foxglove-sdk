cmake_minimum_required(VERSION 3.8)
project(unity2foxglove_foxrun_interfaces_v1)

find_package(ament_cmake REQUIRED)
find_package(rosidl_default_generators REQUIRED)
find_package(builtin_interfaces REQUIRED)

rosidl_generate_interfaces(${PROJECT_NAME}
  "msg/Phase181NestedState_3281D0E21244.msg"
  "msg/Phase181State_79A0742B2553.msg"
  "msg/Phase181State_79A0742B2553Envelope.msg"
  DEPENDENCIES builtin_interfaces
)

ament_export_dependencies(rosidl_default_runtime)
ament_package()
