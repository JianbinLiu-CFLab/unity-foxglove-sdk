#!/usr/bin/env python3
from __future__ import annotations
from .foundation import *
class _LogTailState:
    """Bounded partial-record and file identity state for one build invocation."""

    def __init__(self) -> None:
        """Start without a file or a partially consumed record."""
        self.identity: Optional[Tuple[int, int]] = None
        self.size = 0
        self.pending = b""
        self.discarding = False
        self.warning: Optional[str] = None

    def report(self, message: str) -> None:
        """Report an unusable/truncated log without flooding each polling pass."""
        if self.warning != message:
            print(f"[build_unity_il2cpp] {message}", file=sys.stderr, flush=True)
            self.warning = message

    def seed(self, log_path: Path) -> int:
        """Remember prelaunch identity and skip bytes from previous invocations."""
        try:
            info = log_path.stat()
        except OSError:
            return INITIAL_LOG_OFFSET
        if not stat.S_ISREG(info.st_mode):
            self.report(f"Log is not a regular file: {log_path}")
            return INITIAL_LOG_OFFSET
        self.identity = (info.st_dev, info.st_ino)
        self.size = info.st_size
        return self.size


def read_new_important_lines(
    log_path: Path, offset: int, *, state: Optional[_LogTailState] = None, final: bool = False
) -> Tuple[int, List[str]]:
    """Consume at most one chunk; explicit state preserves identity and split records."""
    persistent = state is not None
    state = state if state is not None else _LogTailState()
    try:
        if not stat.S_ISREG(log_path.stat().st_mode):
            state.report(f"Log is not a regular file: {log_path}")
            return offset, []
        # Nonblocking open prevents a POSIX file-to-FIFO race from waiting for a
        # writer. Validate the opened object as well as the pre-open path.
        flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NONBLOCK", 0)
        descriptor = os.open(log_path, flags)
        with os.fdopen(descriptor, "rb") as handle:
            info = os.fstat(handle.fileno())
            if not stat.S_ISREG(info.st_mode):
                state.report(f"Log is not a regular file: {log_path}")
                return offset, []
            identity = (info.st_dev, info.st_ino)
            if (state.identity is not None and
                    (identity != state.identity or info.st_size < state.size)) or offset > info.st_size:
                offset, state.pending, state.discarding = 0, b"", False
            state.identity, state.size = identity, info.st_size
            handle.seek(offset)
            contents = handle.read(MAX_LOG_READ_BYTES)
            new_offset = handle.tell()
    except FileNotFoundError:
        return offset, []  # Unity may not have created its log yet.
    except OSError as exc:
        state.report(f"Log read failed for {log_path}: {exc}")
        return offset, []

    important = []
    for chunk in contents.splitlines(keepends=True):
        complete = chunk.endswith((b"\n", b"\r"))
        if state.discarding:
            if complete:
                state.discarding = False
            continue
        record = state.pending + chunk
        state.pending = b""
        if len(record) > MAX_LOG_READ_BYTES or (not complete and len(record) == MAX_LOG_READ_BYTES):
            state.report(f"Log record exceeded {MAX_LOG_READ_BYTES} bytes; progress record truncated. Full log: {log_path}")
            record = record[:MAX_LOG_READ_BYTES]
            line = record.decode("utf-8", errors="replace").strip()
            if is_important_log_line(line):
                important.append(line + " [truncated]")
            state.discarding = not complete
        elif complete:
            line = record.decode("utf-8", errors="replace").strip()
            if is_important_log_line(line):
                important.append(line)
        else:
            state.pending = record
    if final and new_offset >= state.size:
        line = state.pending.decode("utf-8", errors="replace").strip()
        if is_important_log_line(line):
            important.append(line)
        state.pending = b""
    elif not persistent:
        # The two-argument convenience form carries partial records by offset.
        new_offset -= len(state.pending)
    return new_offset, important


def finish_log_tail(log_path: Path, offset: int, state: _LogTailState) -> None:
    """Drain a bounded final diagnostic backlog only after process cleanup."""
    for _ in range(MAX_FINAL_LOG_CHUNKS):
        previous = offset
        offset, lines = read_new_important_lines(log_path, offset, state=state, final=True)
        for line in lines:
            print(f"[unity-log] {line}", flush=True)
        if offset == previous or offset >= state.size:
            return
    state.report(f"Final progress log budget exhausted; remaining diagnostics are in {log_path}")

__all__ = ['_LogTailState', 'read_new_important_lines', 'finish_log_tail']
