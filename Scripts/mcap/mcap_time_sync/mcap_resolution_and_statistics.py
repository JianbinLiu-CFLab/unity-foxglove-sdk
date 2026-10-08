from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "mcap_time_sync.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def latest_mcap_from_default_dir() -> Path:
    """Pick the newest readable MCAP under the default recording directory."""

    matches = list(DEFAULT_RECORDING_DIR.rglob("*.mcap"))
    if not matches:
        raise FileNotFoundError(f"No .mcap files found under {DEFAULT_RECORDING_DIR}")

    latest = max(matches, key=lambda p: p.stat().st_mtime)
    if latest.stat().st_size <= 0:
        raise FileNotFoundError(f"The selected MCAP file is empty: {latest}")
    return latest.resolve()


def resolve_mcap_path(path_or_glob: str | None) -> Path:
    """Resolve explicit path or choose latest MCAP by default."""

    if path_or_glob:
        candidate = Path(path_or_glob)
        if not candidate.is_absolute():
            candidate = REPO_ROOT / candidate
        candidate = candidate.resolve()

        if any(ch in path_or_glob for ch in ("*", "?", "[")):
            pattern = str(candidate)
            parent = candidate.parent
            matches = list(parent.glob(candidate.name))
            if not matches:
                raise FileNotFoundError(f"No MCAP files match: {pattern}")
            return max(matches, key=lambda p: p.stat().st_mtime)

        if not candidate.exists() or candidate.stat().st_size <= 0:
            raise FileNotFoundError(f"MCAP not found or empty: {candidate}")
        return candidate

    return latest_mcap_from_default_dir()


def percentile(values: list[float], ratio: float) -> float:
    """Get a deterministic percentile value for a sorted float list."""

    if not values:
        return math.nan
    idx = int((ratio / 100.0) * (len(values) - 1))
    return values[idx]


def describe_deltas(values: list[float]) -> dict[str, float]:
    """Build a concise statistics dictionary from absolute millisecond deltas."""

    if not values:
        return {
            "count": 0,
            "mean_ms": math.nan,
            "min_ms": math.nan,
            "median_ms": math.nan,
            "p95_ms": math.nan,
            "p99_ms": math.nan,
            "max_ms": math.nan,
            "pct_le": {str(t): math.nan for t in TIMESTAMP_THRESHOLDS_MS},
        }

    sorted_values = sorted(values)
    return {
        "count": len(sorted_values),
        "mean_ms": sum(sorted_values) / len(sorted_values),
        "min_ms": sorted_values[0],
        "median_ms": median(sorted_values),
        "p95_ms": percentile(sorted_values, 95),
        "p99_ms": percentile(sorted_values, 99),
        "max_ms": sorted_values[-1],
        "pct_le": {str(t): percent_if(sorted_values, t) for t in TIMESTAMP_THRESHOLDS_MS},
    }


def nearest_neighbor_deltas_ms(a_ns: list[int], b_ns: list[int]) -> list[float]:
    """Distance from each sample in ``a_ns`` to nearest sample in sorted ``b_ns``."""

    if not b_ns:
        return []

    b_sorted = sorted(b_ns)
    deltas_ms: list[float] = []

    for target in a_ns:
        idx = bisect.bisect_left(b_sorted, target)
        candidates = []
        if idx < len(b_sorted):
            candidates.append(abs(b_sorted[idx] - target))
        if idx > 0:
            candidates.append(abs(b_sorted[idx - 1] - target))

        if not candidates:
            continue

        deltas_ms.append(min(candidates) / 1_000_000.0)

    return deltas_ms


__all__ = [name for name in globals() if not name.startswith("__")]
