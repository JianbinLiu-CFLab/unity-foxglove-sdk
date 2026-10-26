from __future__ import annotations
from pathlib import Path as _Phase192Path
from .runtime_checks import *
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path

def check_sample_source_boundary(results: list[CheckResult]) -> None:
    """Validate package samples are source-only and name their acceptance boundaries."""
    required = [
        ADAPTER_SAMPLE / "README.md",
        ADAPTER_SAMPLE / "Phase110Ros2ForUnityContextFactory.cs",
        ADAPTER_SAMPLE / "Phase110Ros2ForUnityContext.cs",
        ADAPTER_SAMPLE / "Phase110Ros2ForUnityStringSmoke.cs",
        RVIZ_SAMPLE / "README.md",
        RVIZ_SAMPLE / "Phase128Rviz2TfLaserScanSmoke.cs",
        RVIZ_SAMPLE / "rviz2_phase128_tf_laserscan.rviz",
        RVIZ_SAMPLE / "phase128_rviz2_evidence_template.md",
        RVIZ_POINTCLOUD2_SAMPLE / "README.md",
        RVIZ_POINTCLOUD2_SAMPLE / "Phase129Rviz2PointCloud2Smoke.cs",
        RVIZ_POINTCLOUD2_SAMPLE / "Phase129PointCloud2MessageBuilder.cs",
        RVIZ_POINTCLOUD2_SAMPLE / "rviz2_phase129_pointcloud2.rviz",
        RVIZ_POINTCLOUD2_SAMPLE / "phase129_pointcloud2_evidence_template.md",
        RVIZ_MARKERARRAY_SAMPLE / "README.md",
        RVIZ_MARKERARRAY_SAMPLE / "Phase130Rviz2MarkerArraySmoke.cs",
        RVIZ_MARKERARRAY_SAMPLE / "Phase130MarkerArrayMessageBuilder.cs",
        RVIZ_MARKERARRAY_SAMPLE / "rviz2_phase130_markerarray.rviz",
        RVIZ_MARKERARRAY_SAMPLE / "phase130_markerarray_evidence_template.md",
        RVIZ_V1_SAMPLE / "README.md",
        RVIZ_V1_SAMPLE / "rviz2_phase131_standard_visualization.rviz",
        RVIZ_V1_SAMPLE / "phase131_standard_visualization_evidence_template.md",
        STANDARD_MESSAGES_SAMPLE / "README.md",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardMessagesSmoke.cs",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardCameraSource.cs",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardImuSource.cs",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardOdometrySource.cs",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardPoseSource.cs",
        STANDARD_MESSAGES_SAMPLE / "Phase132StandardNavSatFixSource.cs",
        STANDARD_MESSAGES_SAMPLE / "rviz2_phase132_standard_messages.rviz",
        STANDARD_MESSAGES_SAMPLE / "phase132_standard_messages_evidence_template.md",
    ]
    for path in required:
        add(results, f"sample file: {path.name}", path.exists(), rel(path))

    sample_roots = [
        ADAPTER_SAMPLE,
        RVIZ_SAMPLE,
        RVIZ_POINTCLOUD2_SAMPLE,
        RVIZ_MARKERARRAY_SAMPLE,
        RVIZ_V1_SAMPLE,
        STANDARD_MESSAGES_SAMPLE,
    ]
    invalid_files = [
        rel(path)
        for sample_root in sample_roots
        for path in iter_files(sample_root)
        if path.suffix.lower() not in ALLOWED_SAMPLE_SUFFIXES
    ]
    add(
        results,
        "samples contain only source/docs/rviz/meta files",
        not invalid_files,
        "; ".join(invalid_files[:MAX_REPORTED_OFFENDERS]) if invalid_files else "source-only sample",
    )

    readme = (ADAPTER_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace") if (ADAPTER_SAMPLE / "README.md").exists() else ""
    add(
        results,
        "sample README documents runtime package activation",
        "dev.unity2foxglove.ros2forunity.runtime.humble.win64" in readme
        and "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in readme,
        rel(ADAPTER_SAMPLE / "README.md"),
    )
    add(
        results,
        "sample README documents live topics",
        "/unity2foxglove/ros2forunity/string/out" in readme
        and "/unity2foxglove/ros2forunity/string/in" in readme,
        rel(ADAPTER_SAMPLE / "README.md"),
    )
    rviz_readme = (RVIZ_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace") if (RVIZ_SAMPLE / "README.md").exists() else ""
    rviz_script = (
        (RVIZ_SAMPLE / "Phase128Rviz2TfLaserScanSmoke.cs").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_SAMPLE / "Phase128Rviz2TfLaserScanSmoke.cs").exists()
        else ""
    )
    rviz_config = (
        (RVIZ_SAMPLE / "rviz2_phase128_tf_laserscan.rviz").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_SAMPLE / "rviz2_phase128_tf_laserscan.rviz").exists()
        else ""
    )
    add(
        results,
        "RViz2 sample README documents helper and compile symbol",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in rviz_readme
        and "phase128_rviz2_acceptance.py" in rviz_readme
        and "/tf" in rviz_readme
        and "/scan" in rviz_readme,
        rel(RVIZ_SAMPLE / "README.md"),
    )
    add(
        results,
        "RViz2 sample script is guarded and source-only",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in rviz_script
        and "CreatePublisher<tf2_msgs.msg.TFMessage>" in rviz_script
        and "CreatePublisher<sensor_msgs.msg.LaserScan>" in rviz_script,
        rel(RVIZ_SAMPLE / "Phase128Rviz2TfLaserScanSmoke.cs"),
    )
    add(
        results,
        "RViz2 config uses map fixed frame and scan topic",
        "Fixed Frame: map" in rviz_config
        and "/tf" in rviz_config
        and "/scan" in rviz_config
        and "rviz_default_plugins/LaserScan" in rviz_config,
        rel(RVIZ_SAMPLE / "rviz2_phase128_tf_laserscan.rviz"),
    )

    pointcloud_readme = (
        (RVIZ_POINTCLOUD2_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_POINTCLOUD2_SAMPLE / "README.md").exists()
        else ""
    )
    pointcloud_smoke = (
        (RVIZ_POINTCLOUD2_SAMPLE / "Phase129Rviz2PointCloud2Smoke.cs").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_POINTCLOUD2_SAMPLE / "Phase129Rviz2PointCloud2Smoke.cs").exists()
        else ""
    )
    pointcloud_builder = (
        (RVIZ_POINTCLOUD2_SAMPLE / "Phase129PointCloud2MessageBuilder.cs").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_POINTCLOUD2_SAMPLE / "Phase129PointCloud2MessageBuilder.cs").exists()
        else ""
    )
    pointcloud_config = (
        (RVIZ_POINTCLOUD2_SAMPLE / "rviz2_phase129_pointcloud2.rviz").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_POINTCLOUD2_SAMPLE / "rviz2_phase129_pointcloud2.rviz").exists()
        else ""
    )
    add(
        results,
        "PointCloud2 sample README documents helper, TF, and standard type",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in pointcloud_readme
        and "phase129_pointcloud2_acceptance.py" in pointcloud_readme
        and "/tf" in pointcloud_readme
        and "/points" in pointcloud_readme
        and "sensor_msgs/msg/PointCloud2" in pointcloud_readme,
        rel(RVIZ_POINTCLOUD2_SAMPLE / "README.md"),
    )
    add(
        results,
        "PointCloud2 sample smoke is guarded and source-only",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in pointcloud_smoke
        and "CreatePublisher<tf2_msgs.msg.TFMessage>" in pointcloud_smoke
        and "CreatePublisher<sensor_msgs.msg.PointCloud2>" in pointcloud_smoke,
        rel(RVIZ_POINTCLOUD2_SAMPLE / "Phase129Rviz2PointCloud2Smoke.cs"),
    )
    add(
        results,
        "PointCloud2 sample builder maps packed SDK layout explicitly",
        "PointCloudPackedDataBuilder.Build(frame)" in pointcloud_builder
        and "PointFieldFloat32 = 7" in pointcloud_builder
        and "(byte)field.Type" not in pointcloud_builder,
        rel(RVIZ_POINTCLOUD2_SAMPLE / "Phase129PointCloud2MessageBuilder.cs"),
    )
    add(
        results,
        "PointCloud2 config uses map fixed frame and points topic",
        "Fixed Frame: map" in pointcloud_config
        and "/tf" in pointcloud_config
        and "/points" in pointcloud_config
        and "rviz_default_plugins/PointCloud2" in pointcloud_config,
        rel(RVIZ_POINTCLOUD2_SAMPLE / "rviz2_phase129_pointcloud2.rviz"),
    )

    markerarray_readme = (
        (RVIZ_MARKERARRAY_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_MARKERARRAY_SAMPLE / "README.md").exists()
        else ""
    )
    markerarray_smoke = (
        (RVIZ_MARKERARRAY_SAMPLE / "Phase130Rviz2MarkerArraySmoke.cs").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_MARKERARRAY_SAMPLE / "Phase130Rviz2MarkerArraySmoke.cs").exists()
        else ""
    )
    markerarray_builder = (
        (RVIZ_MARKERARRAY_SAMPLE / "Phase130MarkerArrayMessageBuilder.cs").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_MARKERARRAY_SAMPLE / "Phase130MarkerArrayMessageBuilder.cs").exists()
        else ""
    )
    markerarray_config = (
        (RVIZ_MARKERARRAY_SAMPLE / "rviz2_phase130_markerarray.rviz").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_MARKERARRAY_SAMPLE / "rviz2_phase130_markerarray.rviz").exists()
        else ""
    )
    add(
        results,
        "MarkerArray sample README documents helper, fixed frame, and standard type",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in markerarray_readme
        and "phase130_markerarray_acceptance.py" in markerarray_readme
        and "/tf" in markerarray_readme
        and "/markers" in markerarray_readme
        and "visualization_msgs/msg/MarkerArray" in markerarray_readme
        and "frame_id = map" in markerarray_readme,
        rel(RVIZ_MARKERARRAY_SAMPLE / "README.md"),
    )
    add(
        results,
        "MarkerArray sample smoke is guarded and source-only",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in markerarray_smoke
        and "CreatePublisher<visualization_msgs.msg.MarkerArray>" in markerarray_smoke
        and "CreatePublisher<tf2_msgs.msg.TFMessage>" in markerarray_smoke
        and "FrameMarkerOrigin = \"phase130_marker_origin\"" in markerarray_smoke
        and "CreatePublisher<sensor_msgs.msg" not in markerarray_smoke,
        rel(RVIZ_MARKERARRAY_SAMPLE / "Phase130Rviz2MarkerArraySmoke.cs"),
    )
    add(
        results,
        "MarkerArray sample builder uses deterministic ids and cleanup actions",
        "FnvOffsetBasis" in markerarray_builder
        and "0x7fffffff" in markerarray_builder
        and "Marker.DELETE" in markerarray_builder
        and "Marker.DELETEALL" in markerarray_builder
        and "Lifetime" in markerarray_builder,
        rel(RVIZ_MARKERARRAY_SAMPLE / "Phase130MarkerArrayMessageBuilder.cs"),
    )
    add(
        results,
        "MarkerArray config uses map fixed frame and markers topic",
        "Fixed Frame: map" in markerarray_config
        and "/markers" in markerarray_config
        and "rviz_default_plugins/MarkerArray" in markerarray_config
        and "rviz_default_plugins/TF" not in markerarray_config
        and "rviz_default_plugins/PointCloud2" not in markerarray_config,
        rel(RVIZ_MARKERARRAY_SAMPLE / "rviz2_phase130_markerarray.rviz"),
    )

    v1_readme = (
        (RVIZ_V1_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_V1_SAMPLE / "README.md").exists()
        else ""
    )
    v1_config = (
        (RVIZ_V1_SAMPLE / "rviz2_phase131_standard_visualization.rviz").read_text(encoding="utf-8", errors="replace")
        if (RVIZ_V1_SAMPLE / "rviz2_phase131_standard_visualization.rviz").exists()
        else ""
    )
    add(
        results,
        "RViz2 v1 README documents docs-only kit and publisher sample imports",
        "not a publisher sample by itself" in v1_readme
        and "RViz2 Standard Visualization Acceptance" in v1_readme
        and "RViz2 PointCloud2 Acceptance" in v1_readme
        and "RViz2 MarkerArray Acceptance" in v1_readme,
        rel(RVIZ_V1_SAMPLE / "README.md"),
    )
    add(
        results,
        "RViz2 v1 README documents TF owner rule",
        "single owner" in v1_readme
        and "map -> base_link" in v1_readme
        and "Publish Shared Base Tf" in v1_readme,
        rel(RVIZ_V1_SAMPLE / "README.md"),
    )
    add(
        results,
        "RViz2 v1 config uses only supported standard visualization topics",
        "Fixed Frame: map" in v1_config
        and "/tf" in v1_config
        and "/scan" in v1_config
        and "/points" in v1_config
        and "/markers" in v1_config
        and "rviz_default_plugins/TF" in v1_config
        and "rviz_default_plugins/LaserScan" in v1_config
        and "rviz_default_plugins/PointCloud2" in v1_config
        and "rviz_default_plugins/MarkerArray" in v1_config,
        rel(RVIZ_V1_SAMPLE / "rviz2_phase131_standard_visualization.rviz"),
    )

    standard_readme = (
        (STANDARD_MESSAGES_SAMPLE / "README.md").read_text(encoding="utf-8", errors="replace")
        if (STANDARD_MESSAGES_SAMPLE / "README.md").exists()
        else ""
    )
    standard_smoke = (
        (STANDARD_MESSAGES_SAMPLE / "Phase132StandardMessagesSmoke.cs").read_text(encoding="utf-8", errors="replace")
        if (STANDARD_MESSAGES_SAMPLE / "Phase132StandardMessagesSmoke.cs").exists()
        else ""
    )
    standard_sources = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in [
            STANDARD_MESSAGES_SAMPLE / "Phase132StandardCameraSource.cs",
            STANDARD_MESSAGES_SAMPLE / "Phase132StandardImuSource.cs",
            STANDARD_MESSAGES_SAMPLE / "Phase132StandardOdometrySource.cs",
            STANDARD_MESSAGES_SAMPLE / "Phase132StandardPoseSource.cs",
            STANDARD_MESSAGES_SAMPLE / "Phase132StandardNavSatFixSource.cs",
        ]
        if path.exists()
    )
    standard_helper = (ROOT / "Scripts" / "smoke" / "ros2" / "phase132_standard_messages_acceptance.py")
    standard_helper_text = (
        "\n".join(
            [standard_helper.read_text(encoding="utf-8", errors="replace")]
            + [
                path.read_text(encoding="utf-8", errors="replace")
                for path in sorted(standard_helper.with_suffix("").glob("*.py"))
            ]
        )
        if standard_helper.exists()
        else ""
    )
    standard_launcher = (ROOT / "Scripts" / "smoke" / "ros2" / "launch_phase132_rviz2.py")
    standard_launcher_text = standard_launcher.read_text(encoding="utf-8", errors="replace") if standard_launcher.exists() else ""
    standard_rviz_config = (
        (STANDARD_MESSAGES_SAMPLE / "rviz2_phase132_standard_messages.rviz").read_text(encoding="utf-8", errors="replace")
        if (STANDARD_MESSAGES_SAMPLE / "rviz2_phase132_standard_messages.rviz").exists()
        else ""
    )
    add(
        results,
        "Standard Messages README documents topics, helper, QoS, TF, and namespace caveats",
        all(
            token in standard_readme
            for token in (
                "/camera/camera_info",
                "/camera/image_raw",
                "/imu/data",
                "/odom",
                "/pose",
                "/fix",
                "phase132_standard_messages_acceptance.py",
                "launches RViz2 by default",
                "--no-launch-rviz",
                "R2FU default QoS",
                "does not publish `/tf`",
                "Production projects should namespace",
                "synthetic constant WGS84",
            )
        ),
        rel(STANDARD_MESSAGES_SAMPLE / "README.md"),
    )
    add(
        results,
        "Standard Messages smoke is guarded and publishes only the six standard topics",
        "UNITY2FOXGLOVE_ROS2_FOR_UNITY" in standard_smoke
        and "CreatePublisher<sensor_msgs.msg.CameraInfo>" in standard_smoke
        and "CreatePublisher<sensor_msgs.msg.Image>" in standard_smoke
        and "CreatePublisher<sensor_msgs.msg.Imu>" in standard_smoke
        and "CreatePublisher<nav_msgs.msg.Odometry>" in standard_smoke
        and "CreatePublisher<geometry_msgs.msg.PoseStamped>" in standard_smoke
        and "CreatePublisher<sensor_msgs.msg.NavSatFix>" in standard_smoke
        and "CreatePublisher<tf2_msgs.msg.TFMessage>" not in standard_smoke
        and "CreatePublisher<rosgraph_msgs.msg.Clock>" not in standard_smoke,
        rel(STANDARD_MESSAGES_SAMPLE / "Phase132StandardMessagesSmoke.cs"),
    )
    add(
        results,
        "Standard Messages sources validate fixed arrays and tiny rgb8 image",
        "CameraInfo.k[9]" in standard_sources
        and "CameraInfo.r[9]" in standard_sources
        and "CameraInfo.p[12]" in standard_sources
        and "double[9]" in standard_sources
        and "double[36]" in standard_sources
        and "rgb8" in standard_sources
        and "step = checked((uint)(width * 3))" in standard_sources
        and "expectedLength = checked(height * (int)step)" in standard_sources
        and "new byte[expectedLength]" in standard_sources,
        rel(STANDARD_MESSAGES_SAMPLE),
    )
    add(
        results,
        "Standard Messages helper uses ros2-script.py and validates non-zero defaults",
        "import _ros2_windows_env" in standard_helper_text
        and "validate_ros2_root" in standard_helper_text
        and "[str(pixi_python), str(ros2_script)" not in standard_helper_text
        and "--domain-id" in standard_helper_text
        and "parser.set_defaults(launch_rviz=True)" in standard_helper_text
        and "rviz2_phase132_standard_messages.rviz" in standard_helper_text
        and "--no-launch-rviz" in standard_helper_text
        and "validate_camera_info" in standard_helper_text
        and "validate_image" in standard_helper_text
        and "validate_imu" in standard_helper_text
        and "validate_navsatfix" in standard_helper_text
        and "expected at least" in standard_helper_text
        and "GREEN" in standard_helper_text,
        rel(standard_helper),
    )
    add(
        results,
        "Standard Messages RViz2 helper config and launcher are wired",
        "rviz_default_plugins/Pose" in standard_rviz_config
        and "rviz_default_plugins/Image" in standard_rviz_config
        and "/pose" in standard_rviz_config
        and "/camera/image_raw" in standard_rviz_config
        and "ros2env.launch_rviz" in standard_launcher_text
        and "rviz2_phase132_standard_messages.rviz" in standard_launcher_text,
        rel(STANDARD_MESSAGES_SAMPLE / "rviz2_phase132_standard_messages.rviz"),
    )




__all__ = [name for name in globals() if not name.startswith("__")]
