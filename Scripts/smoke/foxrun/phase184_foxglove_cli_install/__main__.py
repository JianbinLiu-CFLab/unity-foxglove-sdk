"""Executable entrypoint for the decomposed Phase184 CLI installer package."""

from __future__ import annotations

from . import *

if __name__ == "__main__":
    _phase192_entrypoint = globals().get("_entrypoint")
    if callable(_phase192_entrypoint):
        raise SystemExit(_phase192_entrypoint())
    _phase192_main = globals().get("main")
    if callable(_phase192_main):
        raise SystemExit(_phase192_main())