from __future__ import annotations
import shutil
import os
import time
from pathlib import Path

class DurableSnapshot:
    """All-or-nothing before-image for files and directories touched by sync."""
    def __init__(self, paths: list[Path], backup_root: Path):
        self.paths = [p.resolve() for p in paths]
        requested_root = backup_root.resolve()
        self.backup_root = requested_root
        if self.backup_root.exists():
            self.backup_root = requested_root.with_name(
                f"{requested_root.name}-{os.getpid()}-{time.time_ns()}"
            )
        self.records: list[tuple[Path, Path, bool]] = []
        self.backup_root.mkdir(parents=True, exist_ok=True)
        for index, path in enumerate(self.paths):
            backup = self.backup_root / str(index)
            existed = path.exists()
            if existed:
                if path.is_dir(): shutil.copytree(path, backup)
                else: backup.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(path, backup)
            self.records.append((path, backup, existed))
    def restore(self) -> None:
        for path, backup, existed in reversed(self.records):
            if path.exists():
                if path.is_dir(): shutil.rmtree(path)
                else: path.unlink()
            if existed:
                path.parent.mkdir(parents=True, exist_ok=True)
                if backup.is_dir(): shutil.copytree(backup, path)
                else: shutil.copy2(backup, path)
    def commit(self) -> None:
        shutil.rmtree(self.backup_root, ignore_errors=True)
