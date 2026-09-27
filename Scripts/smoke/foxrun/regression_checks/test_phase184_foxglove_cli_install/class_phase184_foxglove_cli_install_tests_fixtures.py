from __future__ import annotations
from .class_phase184_foxglove_cli_install_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184FoxgloveCliInstallTests_fixtures:
    def test_minimal_environment_rejects_case_insensitive_windows_duplicates(self):
        """Verify minimal environment rejects case insensitive windows duplicates."""

        for first, second in (
            ("PATH", "Path"),
            ("SystemRoot", "SYSTEMROOT"),
            ("TEMP", "temp"),
        ):
            with self.subTest(first=first, second=second):
                with self.assertRaises(protocol.AcceptanceFailure):
                    installer.build_minimal_process_environment(
                        {
                            first: r"C:\Phase184\first",
                            second: r"C:\Phase184\second",
                        }
                    )
    def test_resolver_mutation_is_hash_rejected_before_resolved_execution(self):
        """Verify resolver mutation is hash rejected before resolved execution."""

        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)
        environment.after_resolve_hook = lambda: environment.fs.write_bytes(
            INSTALL_PATH,
            b"resolver swapped executable",
        )

        self.assert_provenance_failure(
            lambda: self.verify_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                dependencies=environment.dependencies(),
            )
        )

        runs = [
            event
            for event in environment.events
            if event[0] == "run"
        ]
        self.assertEqual(1, len(runs))
        self.assertEqual(0, environment.active_leases)
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "lease-close"),
        )
    def test_verifier_holds_read_only_lease_across_pre_post_hash_and_execution(self):
        """Verify verifier holds read only lease across pre post hash and execution."""

        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)

        self.verify_cli(
            INSTALL_PATH,
            RECEIPT_PATH,
            dependencies=environment.dependencies(),
        )

        event_names = [event[0] for event in environment.events]
        self.assertEqual(1, event_names.count("lease-open"))
        self.assertEqual(1, event_names.count("lease-close"))
        self.assertGreaterEqual(event_names.count("lease-snapshot"), 4)
        self.assertLess(
            event_names.index("lease-open"),
            event_names.index("run"),
        )
        self.assertGreater(
            event_names.index("lease-close"),
            max(
                index
                for index, name in enumerate(event_names)
                if name == "run"
            ),
        )
        self.assertEqual(0, environment.active_leases)
        self.assert_verifier_read_only(environment)
    def test_version_execution_mutation_fails_and_releases_lease(self):
        """Verify version execution mutation fails and releases lease."""

        environment = FakeEnvironment(self.physical_root)
        self.prepare_verifier_fixture(environment)
        environment.after_command_hook = lambda: environment.fs.write_bytes(
            INSTALL_PATH,
            b"mutated while version child returned",
        )

        self.assert_provenance_failure(
            lambda: self.verify_cli(
                INSTALL_PATH,
                RECEIPT_PATH,
                dependencies=environment.dependencies(),
            )
        )

        self.assertEqual(0, environment.active_leases)
        self.assertEqual(
            1,
            sum(1 for event in environment.events if event[0] == "lease-close"),
        )
        self.assertFalse(
            any(event[0] == "resolve" for event in environment.events)
        )
    def test_read_only_verifier_rejects_missing_and_malformed_receipts(self):
        """Verify read only verifier rejects missing and malformed receipts."""

        for case in ("missing", "malformed-json", "extra-key"):
            with self.subTest(case=case):
                environment = FakeEnvironment(
                    self.physical_root / case,
                    existing=NEW_BYTES,
                )
                if case == "malformed-json":
                    environment.fs.write_bytes(RECEIPT_PATH, b"{not-json")
                elif case == "extra-key":
                    _, receipt = self.prepare_verifier_fixture(environment)
                    receipt["unexpected"] = "rejected"
                    environment.fs.write_receipt(RECEIPT_PATH, receipt)
                environment.events.clear()

                self.assert_provenance_failure(
                    lambda environment=environment: self.verify_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        dependencies=environment.dependencies(),
                    )
                )

                self.assertFalse(
                    any(event[0] == "run" for event in environment.events)
                )
                self.assert_verifier_read_only(environment)
    def test_read_only_verifier_production_rejects_non_windows_before_io(self):
        """Verify read only verifier production rejects non windows before I/O."""

        with (
            mock.patch.object(installer.os, "name", "posix"),
            mock.patch(
                "urllib.request.urlopen",
                side_effect=AssertionError("must fail before network"),
            ),
        ):
            self.assert_provenance_failure(
                lambda: self.verify_cli(
                    INSTALL_PATH,
                    RECEIPT_PATH,
                )
            )
    def test_release_selection_requires_one_exact_official_asset_and_matching_tag(self):
        """Verify release selection requires one exact official asset and matching tag."""

        valid = {
            "tag_name": "v1.2.3",
            "assets": [
                {
                    "name": "foxglove-linux-amd64",
                    "browser_download_url": "https://example.invalid/ignored",
                },
                official_asset(
                    CURRENT_OFFICIAL_ASSET_NAME,
                    CURRENT_OFFICIAL_ASSET_URL,
                ),
                {
                    "name": "foxglove-windows-arm64",
                    "browser_download_url": (
                        "https://github.com/foxglove/foxglove-cli/releases/"
                        "download/v1.2.3/foxglove-windows-arm64"
                    ),
                },
            ],
        }
        selected = installer.select_release_asset(valid)
        self.assertEqual("v1.2.3", selected.release_tag)
        self.assertEqual("1.2.3", selected.release_version)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_NAME, selected.asset_name)
        self.assertEqual(CURRENT_OFFICIAL_ASSET_URL, selected.asset_url)
        self.assertEqual(len(NEW_BYTES), selected.asset_size)
        self.assertEqual(sha256_bytes(NEW_BYTES), selected.asset_sha256)

        invalid_releases = (
            {"tag_name": "latest", "assets": valid["assets"]},
            {"tag_name": "v1.2.3", "assets": []},
            {
                "tag_name": "v1.2.3",
                "assets": [
                    official_asset(
                        protocol.CLI_ASSET_NAME,
                        OFFICIAL_ASSET_URL,
                    ),
                    official_asset(
                        CURRENT_OFFICIAL_ASSET_NAME,
                        CURRENT_OFFICIAL_ASSET_URL,
                    ),
                ],
            },
            {
                "tag_name": "v1.2.3",
                "assets": [
                    official_asset(
                        protocol.CLI_ASSET_NAME,
                        (
                            "https://evil.invalid/foxglove-windows-amd64.exe"
                        ),
                    ),
                ],
            },
            {
                "tag_name": "v1.2.3",
                "assets": [
                    official_asset(
                        CURRENT_OFFICIAL_ASSET_NAME,
                        OFFICIAL_ASSET_URL,
                    )
                ],
            },
            {
                "tag_name": "v1.2.4",
                "assets": [
                    official_asset(
                        protocol.CLI_ASSET_NAME,
                        OFFICIAL_ASSET_URL,
                    )
                ],
            },
        )
        for release in invalid_releases:
            with self.subTest(release=release):
                self.assert_provenance_failure(
                    lambda release=release: installer.select_release_asset(release)
                )
    def test_release_selection_requires_canonical_sha256_and_bounded_size(self):
        """Release metadata must independently bind the selected asset bytes."""

        base = official_asset(
            CURRENT_OFFICIAL_ASSET_NAME,
            CURRENT_OFFICIAL_ASSET_URL,
        )
        invalid_assets = (
            {key: value for key, value in base.items() if key != "digest"},
            {**base, "digest": None},
            {**base, "digest": "sha1:" + ("a" * 40)},
            {**base, "digest": "sha256:" + ("A" * 64)},
            {**base, "digest": "sha256:" + ("g" * 64)},
            {**base, "size": True},
            {**base, "size": 0},
            {**base, "size": installer.MAX_DOWNLOAD_BYTES + 1},
        )

        for asset in invalid_assets:
            with self.subTest(asset=asset):
                self.assert_provenance_failure(
                    lambda asset=asset: installer.select_release_asset(
                        {"tag_name": "v1.2.3", "assets": [asset]}
                    )
                )
    def test_install_rejects_download_not_matching_release_metadata_before_execution(self):
        """A redirected or replaced asset must never run before digest verification."""

        for payload in (
            b"short",
            b"x" * len(NEW_BYTES),
        ):
            with self.subTest(payload=payload):
                environment = FakeEnvironment(self.physical_root)
                environment.download_bytes = payload

                self.assert_provenance_failure(
                    lambda: installer.install_cli(
                        INSTALL_PATH,
                        RECEIPT_PATH,
                        environment.dependencies(),
                    )
                )

                self.assertFalse(
                    any(event[0] == "run" for event in environment.events),
                    environment.events,
                )
                self.assertFalse(environment.fs.exists(INSTALL_PATH))
    def test_release_download_redirects_require_https_exact_github_hosts(self):
        """Every automatic redirect must remain on an exact HTTPS GitHub route."""

        validator = getattr(
            installer,
            "validate_official_download_hop_url",
            None,
        )
        handler_type = getattr(
            installer,
            "OfficialReleaseRedirectHandler",
            None,
        )
        self.assertIsNotNone(validator)
        self.assertIsNotNone(handler_type)
        if validator is None or handler_type is None:
            return

        release_asset_cdn = (
            "https://release-assets.githubusercontent.com/"
            "github-production-release-asset/431693744/object?sig=opaque"
        )
        object_cdn = (
            "https://objects.githubusercontent.com/"
            "github-production-release-asset/431693744/object?sig=opaque"
        )
        for url in (
            OFFICIAL_ASSET_URL,
            release_asset_cdn,
            object_cdn,
        ):
            with self.subTest(url=url):
                self.assertEqual(url, validator(url))

        for url in (
            "http://release-assets.githubusercontent.com/object?sig=opaque",
            "ftp://release-assets.githubusercontent.com/object?sig=opaque",
            "https://release-assets.githubusercontent.com.evil.invalid/object",
            "https://user@release-assets.githubusercontent.com/object",
            "https://release-assets.githubusercontent.com:443/object",
            "https://evil.invalid/object",
            release_asset_cdn + "#fragment",
        ):
            with self.subTest(url=url):
                self.assert_provenance_failure(lambda url=url: validator(url))

        handler = handler_type()
        request = urllib.request.Request(OFFICIAL_ASSET_URL, method="GET")
        redirected = handler.redirect_request(
            request,
            None,
            302,
            "Found",
            {},
            release_asset_cdn,
        )
        self.assertEqual(release_asset_cdn, redirected.full_url)
        self.assert_provenance_failure(
            lambda: handler.redirect_request(
                request,
                None,
                302,
                "Found",
                {},
                "https://evil.invalid/object",
            )
        )
        downloader_source = inspect.getsource(installer._download_production)
        self.assertIn("OfficialReleaseRedirectHandler()", downloader_source)
        self.assertIn("validate_official_download_hop_url", downloader_source)
        self.assertNotIn("urllib.request.urlopen(", downloader_source)


__all__ = [name for name in globals() if not name.startswith("__")]
