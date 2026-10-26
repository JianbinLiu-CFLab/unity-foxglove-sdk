from __future__ import annotations
from .core import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate the optional Unity package metadata."""
    add(results, "optional package folder exists", PACKAGE.is_dir(), rel(PACKAGE))
    package_json = PACKAGE / "package.json"
    data = load_json(package_json, results, "package.json parses") if package_json.exists() else {}
    if not data:
        return

    expected = {
        "name": "dev.unity2foxglove.ros2forunity",
        "version": "0.1.0-preview.1",
        "displayName": "Unity2Foxglove ROS2 For Unity",
        "license": "Apache-2.0",
        "unity": "6000.0",
    }
    for key, value in expected.items():
        add(results, f"package {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    dependencies = data.get("dependencies")
    add(
        results,
        "package depends only on core SDK package",
        dependencies == {"dev.unity2foxglove.sdk": "1.9.6"},
        f"dependencies={dependencies!r}",
    )

    samples = data.get("samples")
    add(
        results,
        "package has External Adapter, RViz2 v1, and standard message sample set",
        isinstance(samples, list) and len(samples) >= 6,
        f"samples={samples!r}",
    )
    if isinstance(samples, list) and samples:
        samples_by_name = {
            str(sample.get("displayName", "")): sample
            for sample in samples
            if isinstance(sample, dict)
        }
        sample = samples_by_name.get("ROS2 For Unity External Adapter", {})
        add(
            results,
            "sample displayName",
            sample.get("displayName") == "ROS2 For Unity External Adapter",
            f"displayName={sample.get('displayName')!r}",
        )
        add(
            results,
            "sample path",
            sample.get("path") == "Samples~/ROS2 For Unity External Adapter",
            f"path={sample.get('path')!r}",
        )
        description = str(sample.get("description", ""))
        add(
            results,
            "sample description names runtime package or external R2FU",
            "ROS2 For Unity runtime package or external runtime" in description,
            description,
        )
        rviz_sample = samples_by_name.get("RViz2 Standard Visualization Acceptance", {})
        add(
            results,
            "RViz2 sample displayName",
            rviz_sample.get("displayName") == "RViz2 Standard Visualization Acceptance",
            f"displayName={rviz_sample.get('displayName')!r}",
        )
        add(
            results,
            "RViz2 sample path",
            rviz_sample.get("path") == "Samples~/RViz2 Standard Visualization Acceptance",
            f"path={rviz_sample.get('path')!r}",
        )
        rviz_description = str(rviz_sample.get("description", ""))
        add(
            results,
            "RViz2 sample description names TF and scan",
            "/tf" in rviz_description and "/scan" in rviz_description,
            rviz_description,
        )
        pointcloud_sample = samples_by_name.get("RViz2 PointCloud2 Acceptance", {})
        add(
            results,
            "PointCloud2 sample displayName",
            pointcloud_sample.get("displayName") == "RViz2 PointCloud2 Acceptance",
            f"displayName={pointcloud_sample.get('displayName')!r}",
        )
        add(
            results,
            "PointCloud2 sample path",
            pointcloud_sample.get("path") == "Samples~/RViz2 PointCloud2 Acceptance",
            f"path={pointcloud_sample.get('path')!r}",
        )
        pointcloud_description = str(pointcloud_sample.get("description", ""))
        add(
            results,
            "PointCloud2 sample description names standard type and /points",
            "sensor_msgs/msg/PointCloud2" in pointcloud_description and "/points" in pointcloud_description,
            pointcloud_description,
        )
        markerarray_sample = samples_by_name.get("RViz2 MarkerArray Acceptance", {})
        add(
            results,
            "MarkerArray sample displayName",
            markerarray_sample.get("displayName") == "RViz2 MarkerArray Acceptance",
            f"displayName={markerarray_sample.get('displayName')!r}",
        )
        add(
            results,
            "MarkerArray sample path",
            markerarray_sample.get("path") == "Samples~/RViz2 MarkerArray Acceptance",
            f"path={markerarray_sample.get('path')!r}",
        )
        markerarray_description = str(markerarray_sample.get("description", ""))
        add(
            results,
            "MarkerArray sample description names standard type, /markers, and /tf",
            "visualization_msgs/msg/MarkerArray" in markerarray_description
            and "/markers" in markerarray_description
            and "/tf" in markerarray_description,
            markerarray_description,
        )
        v1_sample = samples_by_name.get("RViz2 Standard Visualization v1", {})
        add(
            results,
            "RViz2 v1 sample displayName",
            v1_sample.get("displayName") == "RViz2 Standard Visualization v1",
            f"displayName={v1_sample.get('displayName')!r}",
        )
        add(
            results,
            "RViz2 v1 sample path",
            v1_sample.get("path") == "Samples~/RViz2 Standard Visualization v1",
            f"path={v1_sample.get('path')!r}",
        )
        v1_description = str(v1_sample.get("description", ""))
        add(
            results,
            "RViz2 v1 sample description names all v1 topics",
            all(topic in v1_description for topic in ("/tf", "/scan", "/points", "/markers")),
            v1_description,
        )
        standard_messages_sample = samples_by_name.get("ROS2 Standard Message Expansion", {})
        add(
            results,
            "Standard Messages sample displayName",
            standard_messages_sample.get("displayName") == "ROS2 Standard Message Expansion",
            f"displayName={standard_messages_sample.get('displayName')!r}",
        )
        add(
            results,
            "Standard Messages sample path",
            standard_messages_sample.get("path") == "Samples~/ROS2 Standard Message Expansion",
            f"path={standard_messages_sample.get('path')!r}",
        )
        standard_messages_description = str(standard_messages_sample.get("description", ""))
        add(
            results,
            "Standard Messages sample description names supported types",
            all(token in standard_messages_description for token in ("CameraInfo", "raw Image", "IMU", "Odometry", "PoseStamped", "NavSatFix")),
            standard_messages_description,
        )


def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required for the Phase107 boundary."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "THIRD_PARTY_NOTICES.md",
        PACKAGE / "Upstream" / "LICENSE.AL2",
        MANIFEST,
        RUNTIME_INVENTORY,
        RUNTIME_NOTICES,
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))



__all__ = [name for name in globals() if not name.startswith("__")]
