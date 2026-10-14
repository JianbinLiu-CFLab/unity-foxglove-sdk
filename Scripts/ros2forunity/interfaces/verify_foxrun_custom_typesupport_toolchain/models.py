#!/usr/bin/env python3
"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "verify_foxrun_custom_typesupport_toolchain.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Mapping, Sequence

SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
DEFAULT_GENERATOR = "Visual Studio 17 2022"
REQUIRED_ROSIDL_MODULES = (
    "rosidl_adapter",
    "rosidl_generator_c",
    "rosidl_generator_cpp",
    "rosidl_typesupport_c",
)
ERROR_CODE = "FOXRUN_TOOLCHAIN001"

@dataclass(frozen=True)
class ProcessResult:
    """Captured output from one explicit, shell-free process probe."""

    return_code: int
    stdout: str
    stderr: str


@dataclass(frozen=True)
class ToolchainPreflightRequest:
    """All roots are explicit so no user profile or global ROS setup is read."""

    distro: str
    ros2_root: Path
    ros2cs_source: Path
    r2fu_source: Path
    build_root: Path
    ros2cs_install: Path | None = None
    generator: str = DEFAULT_GENERATOR
    vswhere: Path | None = None
    dotnet: Path | None = None


@dataclass(frozen=True)
class ToolchainPreflightResult:
    """Represent ToolchainPreflightResult."""
    ready: bool
    distro: str
    generator: str
    requirements: tuple[dict[str, str], ...]
    input_identity: dict[str, object]


class ToolchainPreflightError(RuntimeError):
    """A bounded, operator-actionable preflight failure without local paths."""

    def __init__(self, remediation: str):
        """Initialize this object."""
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


ProbeRunner = Callable[[Sequence[str], Mapping[str, str]], ProcessResult]


__all__ = [name for name in globals() if not name.startswith("__")]
