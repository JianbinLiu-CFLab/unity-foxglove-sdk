from __future__ import annotations
from .arguments_and_collection import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase132_standard_messages_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def validate_camera_info(output: str) -> None:
    """Validate CameraInfo echo contains meaningful calibration data."""

    require_tokens("CameraInfo echo", output, ["frame_id: camera_optical_frame", "distortion_model:", "k:", "r:", "p:"])
    height = parse_positive_int("CameraInfo echo", output, "height")
    width = parse_positive_int("CameraInfo echo", output, "width")
    if height != 24 or width != 32:
        raise RuntimeError(f"CameraInfo expected 32x24, got {width}x{height}.\n{output}")
    if not re.search(r"(?m)^-\s*(?:[1-9][0-9]*(?:\.[0-9]+)?|0\.[0-9]*[1-9][0-9]*)\s*$", output):
        raise RuntimeError(f"CameraInfo echo did not include positive focal terms.\n{output}")
    if count_yaml_array_numbers_after(output, "k") < 9 or count_yaml_array_numbers_after(output, "r") < 9:
        raise RuntimeError(f"CameraInfo k/r arrays were not fully echoed.\n{output}")
    if count_yaml_array_numbers_after(output, "p") < 12:
        raise RuntimeError(f"CameraInfo p array was not fully echoed.\n{output}")


def validate_image(output: str) -> None:
    """Validate Image echo contains a full tiny rgb8 payload."""

    require_tokens("Image echo", output, ["frame_id: camera_optical_frame", "encoding: rgb8", "data:"])
    height = parse_positive_int("Image echo", output, "height")
    width = parse_positive_int("Image echo", output, "width")
    step = parse_positive_int("Image echo", output, "step")
    if width != 32 or height != 24 or step != width * 3:
        raise RuntimeError(f"Image expected width=32 height=24 step=96, got width={width} height={height} step={step}.\n{output}")
    if "'...'" in output or "- ..." in output:
        raise RuntimeError(f"Image echo appears truncated; Phase132 validation requires full data echo.\n{output}")
    expected = height * step
    observed = count_yaml_array_numbers_after(output, "data")
    if observed < expected:
        raise RuntimeError(f"Image data echo contained {observed} bytes, expected at least {expected}.\n{output}")


def validate_imu(output: str) -> None:
    """Validate IMU echo contains non-zero default measurements."""

    require_tokens(
        "IMU echo",
        output,
        [
            "frame_id: base_link",
            "orientation:",
            "orientation_covariance:",
            "angular_velocity:",
            "angular_velocity_covariance:",
            "linear_acceleration:",
            "linear_acceleration_covariance:",
        ],
    )
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"IMU orientation w is not non-zero.\n{output}")
    if not re.search(r"(?m)^\s*z:\s*9\.806", output):
        raise RuntimeError(f"IMU linear acceleration does not include gravity-like z value.\n{output}")


def validate_odometry(output: str) -> None:
    """Validate Odometry echo contains frame IDs, pose, twist, and orientation."""

    require_tokens("Odometry echo", output, ["frame_id: odom", "child_frame_id: base_link", "pose:", "twist:", "covariance:"])
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"Odometry pose orientation w is not non-zero.\n{output}")


def validate_pose(output: str) -> None:
    """Validate PoseStamped echo contains map frame and pose."""

    require_tokens("PoseStamped echo", output, ["frame_id: map", "pose:", "position:", "orientation:"])
    if "w: 1.0" not in output and "w: 1" not in output:
        raise RuntimeError(f"PoseStamped orientation w is not non-zero.\n{output}")


def validate_navsatfix(output: str) -> None:
    """Validate NavSatFix echo contains explicit non-zero WGS84 coordinates."""

    require_tokens("NavSatFix echo", output, ["frame_id: gps_link", "status:", "service:", "position_covariance_type:"])
    latitude = parse_float("NavSatFix echo", output, "latitude")
    longitude = parse_float("NavSatFix echo", output, "longitude")
    parse_float("NavSatFix echo", output, "altitude")
    if abs(latitude) <= 0.0001 and abs(longitude) <= 0.0001:
        raise RuntimeError(f"NavSatFix coordinates are both near zero; expected explicit synthetic WGS84 values.\n{output}")
    if latitude < -90.0 or latitude > 90.0 or longitude < -180.0 or longitude > 180.0:
        raise RuntimeError(f"NavSatFix coordinates are outside valid WGS84 ranges.\n{output}")


def require_summary_field(topic: str, summary: dict[str, object], field: str) -> object:
    """Return a collected summary field or raise with context."""

    if field not in summary:
        raise RuntimeError(f"{topic} summary missing field {field}: {summary}")
    return summary[field]


def validate_camera_info_summary(summary: dict[str, object]) -> None:
    """Validate collected CameraInfo fields."""

    if require_summary_field("/camera/camera_info", summary, "frame_id") != "camera_optical_frame":
        raise RuntimeError(f"CameraInfo frame_id mismatch: {summary}")
    if int(require_summary_field("/camera/camera_info", summary, "width")) != 32:
        raise RuntimeError(f"CameraInfo width mismatch: {summary}")
    if int(require_summary_field("/camera/camera_info", summary, "height")) != 24:
        raise RuntimeError(f"CameraInfo height mismatch: {summary}")
    for field, expected_len in (("k", 9), ("r", 9), ("p", 12)):
        values = require_summary_field("/camera/camera_info", summary, field)
        if not isinstance(values, list) or len(values) != expected_len:
            raise RuntimeError(f"CameraInfo {field} length mismatch: {summary}")
    k = require_summary_field("/camera/camera_info", summary, "k")
    if float(k[0]) <= 0.0 or float(k[4]) <= 0.0:
        raise RuntimeError(f"CameraInfo focal terms are not positive: {summary}")


def validate_image_summary(summary: dict[str, object]) -> None:
    """Validate collected Image fields."""

    if require_summary_field("/camera/image_raw", summary, "frame_id") != "camera_optical_frame":
        raise RuntimeError(f"Image frame_id mismatch: {summary}")
    width = int(require_summary_field("/camera/image_raw", summary, "width"))
    height = int(require_summary_field("/camera/image_raw", summary, "height"))
    step = int(require_summary_field("/camera/image_raw", summary, "step"))
    if width != 32 or height != 24 or step != 96:
        raise RuntimeError(f"Image dimensions mismatch: {summary}")
    if require_summary_field("/camera/image_raw", summary, "encoding") != "rgb8":
        raise RuntimeError(f"Image encoding mismatch: {summary}")
    expected = height * step
    if int(require_summary_field("/camera/image_raw", summary, "data_length")) < expected:
        raise RuntimeError(f"Image data length is shorter than expected {expected}: {summary}")
    if int(require_summary_field("/camera/image_raw", summary, "data_sum")) <= 0:
        raise RuntimeError(f"Image data appears empty: {summary}")


def validate_imu_summary(summary: dict[str, object]) -> None:
    """Validate collected IMU fields."""

    if require_summary_field("/imu/data", summary, "frame_id") != "base_link":
        raise RuntimeError(f"IMU frame_id mismatch: {summary}")
    if float(require_summary_field("/imu/data", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"IMU orientation is not non-zero: {summary}")
    if float(require_summary_field("/imu/data", summary, "linear_acceleration_z")) < 9.0:
        raise RuntimeError(f"IMU acceleration z is not gravity-like: {summary}")
    for field in ("orientation_covariance", "angular_velocity_covariance", "linear_acceleration_covariance"):
        values = require_summary_field("/imu/data", summary, field)
        if not isinstance(values, list) or len(values) != 9:
            raise RuntimeError(f"IMU {field} length mismatch: {summary}")


def validate_odometry_summary(summary: dict[str, object]) -> None:
    """Validate collected Odometry fields."""

    if require_summary_field("/odom", summary, "frame_id") != "odom":
        raise RuntimeError(f"Odometry frame_id mismatch: {summary}")
    if require_summary_field("/odom", summary, "child_frame_id") != "base_link":
        raise RuntimeError(f"Odometry child_frame_id mismatch: {summary}")
    if float(require_summary_field("/odom", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"Odometry orientation is not non-zero: {summary}")
    for field in ("pose_covariance", "twist_covariance"):
        values = require_summary_field("/odom", summary, field)
        if not isinstance(values, list) or len(values) != 36:
            raise RuntimeError(f"Odometry {field} length mismatch: {summary}")


def validate_pose_summary(summary: dict[str, object]) -> None:
    """Validate collected PoseStamped fields."""

    if require_summary_field("/pose", summary, "frame_id") != "map":
        raise RuntimeError(f"PoseStamped frame_id mismatch: {summary}")
    if float(require_summary_field("/pose", summary, "orientation_w")) <= 0.0:
        raise RuntimeError(f"PoseStamped orientation is not non-zero: {summary}")


def validate_navsatfix_summary(summary: dict[str, object]) -> None:
    """Validate collected NavSatFix fields."""

    if require_summary_field("/fix", summary, "frame_id") != "gps_link":
        raise RuntimeError(f"NavSatFix frame_id mismatch: {summary}")
    latitude = float(require_summary_field("/fix", summary, "latitude"))
    longitude = float(require_summary_field("/fix", summary, "longitude"))
    if abs(latitude) <= 0.0001 and abs(longitude) <= 0.0001:
        raise RuntimeError(f"NavSatFix coordinates are both near zero: {summary}")
    if latitude < -90.0 or latitude > 90.0 or longitude < -180.0 or longitude > 180.0:
        raise RuntimeError(f"NavSatFix coordinates are outside WGS84 ranges: {summary}")
    values = require_summary_field("/fix", summary, "position_covariance")
    if not isinstance(values, list) or len(values) != 9:
        raise RuntimeError(f"NavSatFix covariance length mismatch: {summary}")
    if int(require_summary_field("/fix", summary, "position_covariance_type")) <= 0:
        raise RuntimeError(f"NavSatFix covariance type is not known: {summary}")


VALIDATORS = {
    "/camera/camera_info": validate_camera_info,
    "/camera/image_raw": validate_image,
    "/imu/data": validate_imu,
    "/odom": validate_odometry,
    "/pose": validate_pose,
    "/fix": validate_navsatfix,
}
SUMMARY_VALIDATORS = {
    "/camera/camera_info": validate_camera_info_summary,
    "/camera/image_raw": validate_image_summary,
    "/imu/data": validate_imu_summary,
    "/odom": validate_odometry_summary,
    "/pose": validate_pose_summary,
    "/fix": validate_navsatfix_summary,
}




__all__ = [name for name in globals() if not name.startswith("__")]
