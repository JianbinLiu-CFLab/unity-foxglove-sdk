"""Regression checks for Phase181 custom ROS2 typesupport add-on validation."""

from __future__ import annotations

from dataclasses import replace
import hashlib
import json
import unittest
from pathlib import Path
from unittest.mock import patch

from Scripts.test_support.phase181_scratch import temporary_directory

from Scripts.ros2forunity.interfaces import foxrun_custom_typesupport_common as common
from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
    AddonValidationError,
    AddonValidationRequest,
    compute_static_interface_digest,
    validate_addon,
    validate_addon_set,
)


def _sha256(path: Path) -> str:
    """Implement the internal sha256 step."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _normalized_json_sha256(payload: object) -> str:
    """Implement the internal normalized json sha256 step."""
    return hashlib.sha256(json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def _role_for(path: str) -> str:
    """Implement the internal role for step."""
    if path.endswith(".dll"):
        return "managed" if path.endswith("_assembly.dll") else "native"
    if path.endswith(".cs"):
        return "catalog"
    if path.endswith(".meta"):
        return "importer"
    if path.startswith("RuntimeSupport/"):
        return "metadata"
    return "notice"


def _classification_for(path: str) -> str:
    """Implement the internal classification for step."""
    return "direct" if path.endswith("custom.dll") else "metadata"


def _plugin_importer_meta() -> str:
    """Implement the internal plugin importer meta step."""
    return """fileFormatVersion: 2
PluginImporter:
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: x86_64
        OS: Windows
  - first:
      Standalone: Windows
    second:
      enabled: 1
      settings:
        CPU: x86_64
"""


def _unity6000_plugin_importer_meta() -> str:
    """Implement the internal unity6000 plugin importer meta step."""
    return """fileFormatVersion: 2
PluginImporter:
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 1
      settings:
        CPU: x86_64
        OS: Windows
    Standalone:
      enabled: 0
      settings:
        CPU: x86_64
    Win64:
      enabled: 1
      settings: {}
"""



__all__ = ['AddonValidationError', 'AddonValidationRequest', 'Path', 'common', 'compute_static_interface_digest', 'hashlib', 'json', 'patch', 'replace', 'temporary_directory', 'unittest', 'validate_addon', 'validate_addon_set', '_classification_for', '_normalized_json_sha256', '_plugin_importer_meta', '_role_for', '_sha256', '_unity6000_plugin_importer_meta']
