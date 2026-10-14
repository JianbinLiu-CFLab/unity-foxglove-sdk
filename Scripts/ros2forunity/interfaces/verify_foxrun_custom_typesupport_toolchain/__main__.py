from __future__ import annotations

import sys

from . import cli_entry

if __name__ == "__main__":
    raise SystemExit(cli_entry(sys.argv[1:]))
