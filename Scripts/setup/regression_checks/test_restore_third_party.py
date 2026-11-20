from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock


class RestoreThirdPartyTests(unittest.TestCase):
    """Exercise the safe third-party checkout lifecycle."""

    @classmethod
    def setUpClass(cls) -> None:
        """Create shared local bare repositories for the fixture suite."""
        cls._source_temp_dir = tempfile.TemporaryDirectory(dir=cls._build_temp_root())
        cls._source_root = Path(cls._source_temp_dir.name)
        cls.source_urls: dict[str, str] = {}
        cls.pins: dict[str, str] = {}
        cls.alternates: dict[str, str] = {}
        for index, relative in enumerate(
            (
                "third-party/foxglove-sdk",
                "third-party/mcap",
                "third-party/ROS-TCP-Connector",
            )
        ):
            bare, pin, alternate = cls._create_source_repository(index)
            cls.source_urls[relative] = bare.as_uri()
            cls.pins[relative] = pin
            cls.alternates[relative] = alternate

    @classmethod
    def tearDownClass(cls) -> None:
        """Remove shared source repositories after the fixture suite."""
        cls._source_temp_dir.cleanup()

    def setUp(self) -> None:
        """Create an isolated target root for one test."""
        self.root = Path(tempfile.mkdtemp(dir=self._source_root))
        self.source_urls = type(self).source_urls
        self.pins = type(self).pins
        self.alternates = type(self).alternates

    def tearDown(self) -> None:
        """Remove the isolated target root."""
        shutil.rmtree(self.root, ignore_errors=True)

    def test_restore_clones_each_repository_at_the_pinned_commit(self) -> None:
        """Restore creates detached clean checkouts at the requested commits."""
        module = self._module()

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            result = module.restore_third_party(
                self.root / "target",
                restore=True,
                source_urls=self.source_urls,
            )

        self.assertEqual(result, 0)
        for relative, pin in self.pins.items():
            checkout = self.root / "target" / relative
            self.assertEqual(self._git(checkout, "rev-parse", "HEAD"), pin)
            self.assertEqual(self._git(checkout, "status", "--porcelain"), "")
            self.assertEqual(
                self._git(checkout, "remote", "get-url", "origin"),
                self.source_urls[relative],
            )

    def test_restore_skips_an_existing_pinned_clean_checkout(self) -> None:
        """A clean checkout already at the pin is left untouched and skipped."""
        module = self._module()
        target = self.root / "target"

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            self.assertEqual(
                module.restore_third_party(
                    target,
                    restore=True,
                    source_urls=self.source_urls,
                ),
                0,
            )
            before = {
                relative: (target / relative).stat().st_mtime_ns
                for relative in self.pins
            }
            self.assertEqual(
                module.restore_third_party(
                    target,
                    restore=True,
                    source_urls=self.source_urls,
                ),
                0,
            )

        after = {
            relative: (target / relative).stat().st_mtime_ns
            for relative in self.pins
        }
        self.assertEqual(before, after)

    def test_restore_rejects_a_clean_checkout_at_the_wrong_head(self) -> None:
        """A clean checkout at another commit is rejected without replacement."""
        module = self._module()
        target = self.root / "target"

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            self.assertEqual(
                module.restore_third_party(
                    target,
                    restore=True,
                    source_urls=self.source_urls,
                ),
                0,
            )
            relative = next(iter(self.pins))
            checkout = target / relative
            self._git(checkout, "fetch", "origin", self.alternates[relative])
            self._git(checkout, "checkout", "--detach", self.alternates[relative])
            result = module.restore_third_party(
                target,
                restore=True,
                source_urls=self.source_urls,
            )

        self.assertNotEqual(result, 0)
        self.assertEqual(self._git(checkout, "rev-parse", "HEAD"), self.alternates[relative])

    def test_restore_rejects_a_dirty_checkout_without_cleaning_it(self) -> None:
        """A dirty checkout is rejected and its untracked file remains."""
        module = self._module()
        target = self.root / "target"

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            self.assertEqual(
                module.restore_third_party(
                    target,
                    restore=True,
                    source_urls=self.source_urls,
                ),
                0,
            )
            relative = next(iter(self.pins))
            checkout = target / relative
            marker = checkout / "local-change.txt"
            marker.write_text("keep", encoding="utf-8")
            result = module.restore_third_party(
                target,
                restore=True,
                source_urls=self.source_urls,
            )

        self.assertNotEqual(result, 0)
        self.assertTrue(marker.exists())
        self.assertIn("local-change.txt", self._git(checkout, "status", "--porcelain"))

    def test_restore_rejects_a_symlinked_checkout(self) -> None:
        """A symlink at a vendor path is rejected without following it."""
        module = self._module()
        target = self.root / "target"
        outside = self.root / "outside"
        outside.mkdir()
        link = target / "third-party" / "foxglove-sdk"
        link.parent.mkdir(parents=True)
        try:
            os.symlink(outside, link, target_is_directory=True)
        except (OSError, NotImplementedError) as exc:
            self.skipTest(f"symlink creation unavailable: {exc}")

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            result = module.restore_third_party(
                target,
                restore=False,
                source_urls=self.source_urls,
            )

        self.assertNotEqual(result, 0)
        self.assertTrue(link.is_symlink())
        self.assertFalse((outside / ".git").exists())

    def test_restore_rejects_a_symlinked_vendor_parent(self) -> None:
        """A symlinked third-party parent is rejected before any checkout."""
        module = self._module()
        target = self.root / "target"
        outside = self.root / "outside"
        outside.mkdir()
        parent = target / "third-party"
        parent.parent.mkdir(parents=True)
        try:
            os.symlink(outside, parent, target_is_directory=True)
        except (OSError, NotImplementedError) as exc:
            self.skipTest(f"symlink creation unavailable: {exc}")

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            result = module.restore_third_party(
                target,
                restore=False,
                source_urls=self.source_urls,
            )

        self.assertNotEqual(result, 0)
        self.assertTrue(parent.is_symlink())
        self.assertFalse((outside / "foxglove-sdk").exists())

    def test_restore_removes_a_new_checkout_after_fetch_failure(self) -> None:
        """A failed new fetch removes only the directory created by that attempt."""
        module = self._module()
        target = self.root / "target"
        broken_urls = dict(self.source_urls)
        broken_urls["third-party/foxglove-sdk"] = (
            (self.root / "missing-source.git").resolve().as_uri()
        )

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            result = module.restore_third_party(
                target,
                restore=True,
                source_urls=broken_urls,
            )

        self.assertNotEqual(result, 0)
        self.assertFalse((target / "third-party/foxglove-sdk").exists())

    def test_source_url_matching_accepts_only_official_optional_git_suffix(self) -> None:
        """Official HTTPS origins may omit .git while other spellings remain rejected."""
        module = self._module()

        self.assertTrue(
            module.source_url_matches(
                "https://github.com/foxglove/foxglove-sdk.git",
                "https://github.com/foxglove/foxglove-sdk",
            )
        )
        self.assertTrue(
            module.source_url_matches(
                "https://github.com/foxglove/foxglove-sdk",
                "https://github.com/foxglove/foxglove-sdk.git",
            )
        )
        self.assertFalse(
            module.source_url_matches(
                "https://github.com/foxglove/foxglove-sdk.git",
                "https://evil.example/foxglove/foxglove-sdk.git",
            )
        )
        self.assertFalse(
            module.source_url_matches(
                "https://github.com/foxglove/foxglove-sdk.git",
                "git@github.com:foxglove/foxglove-sdk.git",
            )
        )

    def test_status_mode_does_not_create_missing_checkouts(self) -> None:
        """Status mode reports missing vendors without creating directories."""
        module = self._module()
        target = self.root / "target"

        with mock.patch.object(module, "VENDOR_ROOTS", self.pins):
            result = module.restore_third_party(
                target,
                restore=False,
                source_urls=self.source_urls,
            )

        self.assertEqual(result, 0)
        self.assertFalse(target.exists())

    def _module(self):
        """Import the restore script under test."""
        from Scripts.setup import restore_third_party

        return restore_third_party

    @staticmethod
    def _build_temp_root() -> str:
        """Return the repository-local temporary directory for fixtures."""
        path = Path("build/tmp/idtmp").resolve()
        path.mkdir(parents=True, exist_ok=True)
        return str(path)

    @classmethod
    def _create_source_repository(cls, index: int) -> tuple[Path, str, str]:
        """Create a local bare repository with a pinned and alternate commit."""
        work = cls._source_root / f"source-{index}"
        work.mkdir()
        cls._run("git", "init", "-b", "main", cwd=work)
        cls._run("git", "config", "user.name", "Regression Test", cwd=work)
        cls._run("git", "config", "user.email", "regression@example.invalid", cwd=work)
        file_path = work / "fixture.txt"
        file_path.write_text("pinned", encoding="utf-8")
        cls._run("git", "add", "fixture.txt", cwd=work)
        cls._run("git", "commit", "-m", "fixture: pinned source", cwd=work)
        pin = cls._git(work, "rev-parse", "HEAD")
        file_path.write_text("alternate", encoding="utf-8")
        cls._run("git", "commit", "-am", "fixture: alternate source", cwd=work)
        alternate = cls._git(work, "rev-parse", "HEAD")
        bare = cls._source_root / f"source-{index}.git"
        cls._run("git", "clone", "--bare", str(work), bare)
        return bare, pin, alternate

    @classmethod
    def _git(cls, cwd: Path, *args: str) -> str:
        """Run Git and return trimmed standard output."""
        return cls._run("git", *args, cwd=cwd).stdout.strip()

    @staticmethod
    def _run(*args: str | Path, cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
        """Run a fixture command with a deterministic test identity."""
        command = [str(arg) for arg in args]
        environment = os.environ.copy()
        environment.update(
            {
                "GIT_AUTHOR_NAME": "Regression Test",
                "GIT_AUTHOR_EMAIL": "regression@example.invalid",
                "GIT_COMMITTER_NAME": "Regression Test",
                "GIT_COMMITTER_EMAIL": "regression@example.invalid",
            }
        )
        return subprocess.run(
            command,
            cwd=str(cwd) if cwd is not None else None,
            env=environment,
            check=True,
            capture_output=True,
            text=True,
        )


if __name__ == "__main__":
    raise SystemExit(unittest.main())
