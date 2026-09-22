#!/usr/bin/env python3
from __future__ import annotations
import argparse, filecmp, json, shutil, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
PACKAGE=ROOT/"Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo"
def version():
    """Read the SDK package version."""
    return json.loads((ROOT/"Packages/dev.unity2foxglove.sdk/package.json").read_text(encoding="utf-8"))["version"]
def imported():
    """Return the imported Unity sample path."""
    return ROOT/"Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK"/version()/"Virtual LiDAR Maze Demo"
def files(root):
    """List non-meta sample files below a root."""
    return {p.relative_to(root) for p in root.rglob("*") if p.is_file() and p.suffix != ".meta"}
def drift():
    """Return package/imported sample differences."""
    p,i=files(PACKAGE),files(imported()); out=[]
    for rel in sorted(p|i):
        if rel not in p: out.append(("extra imported",rel))
        elif rel not in i: out.append(("missing imported",rel))
        elif not filecmp.cmp(PACKAGE/rel, imported()/rel, shallow=False): out.append(("changed",rel))
    return out
def main():
    """Validate or synchronize the maze sample."""
    ap=argparse.ArgumentParser(); ap.add_argument("--apply",action="store_true"); ap.add_argument("--dry-run",action="store_true"); a=ap.parse_args()
    if not PACKAGE.is_dir(): print("PACKAGE_MISSING"); return 1
    if a.apply and not a.dry_run:
        if imported().exists(): shutil.rmtree(imported())
        imported().parent.mkdir(parents=True,exist_ok=True); shutil.copytree(PACKAGE,imported())
    d=drift()
    for kind,rel in d: print(f"{kind}: {rel}")
    print(f"MAZE_VERSION={version()} DRIFT={len(d)}")
    return 1 if d else 0
if __name__=="__main__": sys.exit(main())
