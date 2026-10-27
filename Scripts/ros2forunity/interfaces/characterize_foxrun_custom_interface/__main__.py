from __future__ import annotations

import sys

from . import main

if __name__ == "__main__":
    sys.argv[0] = "characterize_foxrun_custom_interface.py"
    raise SystemExit(main(sys.argv[1:]))
