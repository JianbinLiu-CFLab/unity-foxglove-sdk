#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Decomposed ROS 2 CDR generator section."""

from __future__ import annotations

from ._shared import *

def meta_guid(relative_path: str) -> str:
    """Generate a deterministic Unity .meta GUID for a generated asset."""

    return hashlib.md5(("unity2foxglove-phase93:" + relative_path.replace("\\", "/")).encode("utf-8")).hexdigest()


def existing_guid(path: Path) -> str | None:
    """Read an existing Unity .meta GUID when regenerating in place."""

    if not path.is_file():
        return None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("guid:"):
            return line.split(":", 1)[1].strip()
    return None


def write_text(path: Path, text: str) -> None:
    """Write UTF-8 text with LF line endings, creating parent directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file() and path.read_text(encoding="utf-8") == text:
        return
    path.write_text(text, encoding="utf-8", newline="\n")


def write_meta(path: Path, is_folder: bool) -> None:
    """Write a deterministic Unity .meta file for a generated file or folder."""

    rel = str(path.relative_to(REPO_ROOT))
    meta = Path(str(path) + ".meta")
    guid = existing_guid(meta) or meta_guid(rel)
    if is_folder:
        text = f"""fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    else:
        text = f"""fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""
    write_text(meta, text)


def generate(input_dir: Path, output_dir: Path) -> str:
    """Generate serializers, deserializers, registries, samples, and Unity .meta files."""

    schemas = load_schemas(input_dir)
    output_dir.mkdir(parents=True, exist_ok=True)
    write_meta(output_dir, is_folder=True)

    outputs = {
        "Ros2CdrGeneratedSerializers.g.cs": generate_serializers(schemas),
        "Ros2CdrGeneratedDeserializers.g.cs": generate_deserializers(schemas),
        "Ros2CdrSerializerRegistry.g.cs": generate_registry(schemas),
        "Ros2CdrDeserializerRegistry.g.cs": generate_deserializer_registry(schemas),
        "Ros2CdrSampleFactory.g.cs": generate_samples(schemas),
    }
    for name, text in outputs.items():
        path = output_dir / name
        write_text(path, text)
        write_meta(path, is_folder=False)

    for name in outputs:
        if not Path(str(output_dir / name) + ".meta").is_file():
            raise RuntimeError(f"Missing generated Unity .meta file: {name}.meta")
    if not Path(str(output_dir) + ".meta").is_file():
        raise RuntimeError(f"Missing generated Unity .meta file: {output_dir}.meta")

    return f"[ros2-cdr-gen] generated {len(schemas)} serializers/deserializers in {output_dir}"


