# ROS2 PointCloud2 Inbound

Add `Ros2ForUnityPointCloud2InboundBridge` and this sample component to the same GameObject. Configure the bridge topic and enable a Windows ROS2 For Unity runtime.

The sample receives frames on Unity's main thread, reports received frame and point counts, displays drop counts from the bounded latest-wins slot, and draws the latest finite `x`, `y`, and `z` bounds when the fields are present. ROS coordinates are kept unchanged; the sample does not apply a Unity coordinate conversion.

The bridge owns each delivered frame only until the event handler returns. Consumers that need to retain bytes must copy them before returning.
