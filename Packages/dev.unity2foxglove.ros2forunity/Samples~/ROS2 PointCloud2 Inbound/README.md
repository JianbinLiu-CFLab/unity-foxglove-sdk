# ROS2 PointCloud2 Inbound

Add `Ros2ForUnityPointCloud2InboundBridge` and this sample component to the same GameObject. Configure the bridge topic and enable a Windows ROS2 For Unity runtime.

The sample receives frames on Unity's main thread, reports received frame and point counts, displays drop counts from the bounded latest-wins slot, and draws sampled finite `x`, `y`, and `z` bounds when the fields are present. Bounds work is capped by `_maxBoundsPoints` per frame and accepts Float32 or Float64 fields. ROS coordinates are kept unchanged; the sample does not apply a Unity coordinate conversion.

The bridge owns each delivered frame until all event handlers return, then disposes it. Consumers that need to retain bytes must copy them before returning.
