from __future__ import annotations

from .foundation import *

def visible_windows_for_pid(pid: int) -> list[str]:
    """Return visible top-level Windows titles owned by a process id."""

    if _USER32 is None or _ENUM_WINDOWS_PROC_TYPE is None:
        return []

    titles: list[str] = []

    def callback(hwnd: wintypes.HWND, _lparam: wintypes.LPARAM) -> bool:
        """Collect visible window titles owned by the target process."""
        if not _USER32.IsWindowVisible(hwnd):
            return True

        process_id = wintypes.DWORD()
        _USER32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value != pid:
            return True

        length = _USER32.GetWindowTextLengthW(hwnd)
        buffer = ctypes.create_unicode_buffer(length + 1)
        _USER32.GetWindowTextW(hwnd, buffer, length + 1)
        titles.append(buffer.value or "<untitled>")
        return True

    _USER32.EnumWindows(_ENUM_WINDOWS_PROC_TYPE(callback), 0)
    return titles


def launch_rviz(
    ros2_root: pathlib.Path,
    config: pathlib.Path,
    env: dict[str, str],
    log_prefix: str,
    startup_check_seconds: float = 1.5,
    window_wait_seconds: float = 0.0,
    stdout_log_path: pathlib.Path | None = None,
) -> subprocess.Popen:
    """Launch RViz2 with the supplied config and optional visible-window timing."""

    launch_started = time.perf_counter()

    if not config.exists():
        raise FileNotFoundError(f"RViz2 config does not exist: {config}")

    rviz_exe = ros2_root / "bin" / "rviz2.exe"
    if not rviz_exe.exists():
        raise FileNotFoundError(f"rviz2.exe does not exist: {rviz_exe}")

    pixi = ros2_root / ".pixi" / "envs" / "default"
    qt_plugin_candidates = (
        pixi / "Library" / "lib" / "qt6" / "plugins",
        pixi / "Library" / "plugins",
        pixi / "Library" / "bin" / "plugins",
        pixi / "Lib" / "site-packages" / "PyQt5" / "Qt5" / "plugins",
    )
    rviz_path = [
        str(ros2_root / "bin"),
        *(str(path) for path in ros2_opt_bin_paths(ros2_root)),
        str(ros2_root / "Scripts"),
        str(pixi),
        str(pixi / "Library" / "bin"),
        str(pixi / "Scripts"),
        r"C:\Windows\system32",
        r"C:\Windows",
        r"C:\Windows\System32\Wbem",
        r"C:\Windows\System32\WindowsPowerShell\v1.0",
    ]
    rviz_env = env.copy()
    rviz_env["PATH"] = os.pathsep.join(rviz_path)
    qt_plugin_path = cached_qt_plugin_path(ros2_root, qt_plugin_candidates)
    if qt_plugin_path is not None:
        rviz_env["QT_PLUGIN_PATH"] = str(qt_plugin_path)
        rviz_env["QT_QPA_PLATFORM_PLUGIN_PATH"] = str(qt_plugin_path / "platforms")
        rviz_env["QT_QPA_PLATFORM"] = "windows:dpiawareness=0"
        rviz_env["QT_ENABLE_HIGHDPI_SCALING"] = "0"
        rviz_env["QT_AUTO_SCREEN_SCALE_FACTOR"] = "0"
        rviz_env["QT_SCALE_FACTOR"] = "1"
        rviz_env["QT_SCREEN_SCALE_FACTORS"] = "1"
        rviz_env["QT_OPENGL"] = "desktop"
    else:
        rviz_env["QT_OPENGL"] = "software"
        rviz_env["QT_QUICK_BACKEND"] = "software"
        rviz_env["LIBGL_ALWAYS_SOFTWARE"] = "1"

    log_event(log_prefix, f"RViz2 launch request exe={rviz_exe} config={config}")
    stdout_target = subprocess.DEVNULL
    log_file = None
    if stdout_log_path is not None:
        stdout_log_path.parent.mkdir(parents=True, exist_ok=True)
        log_file = stdout_log_path.open("w", encoding="utf-8")
        stdout_target = log_file
    popen_started = time.perf_counter()
    try:
        process = subprocess.Popen(
            [str(rviz_exe), "-d", str(config)],
            cwd=str(ros2_root),
            env=rviz_env,
            stdout=stdout_target,
            stderr=subprocess.STDOUT if log_file is not None else subprocess.DEVNULL,
        )
    finally:
        if log_file is not None:
            log_file.close()
    popen_elapsed = time.perf_counter() - popen_started
    total_elapsed = time.perf_counter() - launch_started
    log_event(
        log_prefix,
        f"RViz2 Popen returned pid={process.pid} popen_elapsed={popen_elapsed:.3f}s total_elapsed={total_elapsed:.3f}s",
    )

    if startup_check_seconds > 0.0:
        check_deadline = time.perf_counter() + startup_check_seconds
        exit_code = None
        while time.perf_counter() < check_deadline:
            exit_code = process.poll()
            if exit_code is not None:
                break
            time.sleep(0.1)
        if exit_code is not None:
            raise RuntimeError(
                f"RViz2 exited immediately with code {exit_code}; "
                f"check the RViz2 config and DLL search path. config={config}"
            )
        log_event(
            log_prefix,
            f"RViz2 process alive after startup_check={startup_check_seconds:.1f}s elapsed={time.perf_counter() - launch_started:.3f}s",
        )

    if window_wait_seconds > 0.0:
        log_event(log_prefix, f"Waiting up to {window_wait_seconds:.1f}s for visible RViz2 window.")
        window_deadline = time.perf_counter() + window_wait_seconds
        while time.perf_counter() < window_deadline:
            exit_code = process.poll()
            if exit_code is not None:
                raise RuntimeError(
                    f"RViz2 exited with code {exit_code} before a visible window appeared; "
                    f"elapsed={time.perf_counter() - launch_started:.3f}s config={config}"
                )

            titles = visible_windows_for_pid(process.pid)
            if titles:
                log_event(
                    log_prefix,
                    f"RViz2 visible window detected elapsed={time.perf_counter() - launch_started:.3f}s title={titles[0]}",
                )
                break

            time.sleep(0.25)
        else:
            log_event(
                log_prefix,
                f"RViz2 visible window not detected within {window_wait_seconds:.1f}s; "
                f"process_alive={process.poll() is None} elapsed={time.perf_counter() - launch_started:.3f}s",
            )

    log_event(log_prefix, f"Launched RViz2 pid={process.pid} config={config}")
    return process


def cached_qt_plugin_path(
    ros2_root: pathlib.Path,
    qt_plugin_candidates: tuple[pathlib.Path, ...],
) -> pathlib.Path | None:
    """Resolve the RViz Qt plugin path once per ROS2 root."""
    key = ros2_root.resolve()
    if key not in _QT_PLUGIN_PATH_CACHE:
        _QT_PLUGIN_PATH_CACHE[key] = next(
            (candidate for candidate in qt_plugin_candidates if (candidate / "platforms" / "qwindows.dll").exists()),
            None,
        )
    return _QT_PLUGIN_PATH_CACHE[key]


__all__ = ['visible_windows_for_pid', 'launch_rviz', 'cached_qt_plugin_path']
