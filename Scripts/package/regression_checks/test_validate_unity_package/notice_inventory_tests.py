from __future__ import annotations

from .foundation import *


class PackageNoticeInventoryTests:
    """Exercise third-party notice and package-file inventories."""

    def test_third_party_notice_requirements_cover_runtime_plugin_dlls(self) -> None:
        """Every bundled DLL should retain its exact notice-token contract."""
        requirements = {
            requirement[0].relative_to(self.validator.PACKAGE).as_posix():
                requirement[1]
            for requirement in self.validator.THIRD_PARTY_NOTICE_REQUIREMENTS
        }

        expected = {
            "Plugins/Google.Protobuf/Google.Protobuf.dll": (
                "Google.Protobuf",
                "BSD-3-Clause",
                "Plugins/Google.Protobuf/Google.Protobuf.dll",
            ),
            "Runtime/Plugins/compression/K4os.Compression.LZ4.dll": (
                "K4os.Compression.LZ4",
                "MIT",
                "Runtime/Plugins/compression/K4os.Compression.LZ4.dll",
            ),
            "Runtime/Plugins/compression/K4os.Compression.LZ4.Streams.dll": (
                "K4os.Compression.LZ4.Streams",
                "MIT",
                "Runtime/Plugins/compression/K4os.Compression.LZ4.Streams.dll",
            ),
            "Runtime/Plugins/compression/K4os.Hash.xxHash.dll": (
                "K4os.Hash.xxHash",
                "MIT",
                "Runtime/Plugins/compression/K4os.Hash.xxHash.dll",
            ),
            "Runtime/Plugins/compression/System.IO.Pipelines.dll": (
                "System.IO.Pipelines",
                "MIT",
                "Runtime/Plugins/compression/System.IO.Pipelines.dll",
            ),
            "Runtime/Plugins/compression/ZstdSharp.dll": (
                "ZstdSharp.Port",
                "MIT",
                "Runtime/Plugins/compression/ZstdSharp.dll",
            ),
            "Runtime/Plugins/StbImageWriteSharp.dll": (
                "StbImageWriteSharp",
                "Public Domain",
                "Runtime/Plugins/StbImageWriteSharp.dll",
            ),
            "Runtime/Plugins/Windows/x86_64/Unity2FoxgloveDracoNative.dll": (
                "Google Draco",
                "Apache-2.0",
                "Runtime/Plugins/Windows/x86_64/Unity2FoxgloveDracoNative.dll",
            ),
        }
        self.assertEqual(expected, requirements)

    def test_third_party_notice_inventory_rejects_unlisted_package_dlls(self) -> None:
        """Every regular SDK DLL is either notice-listed or explicitly first-party."""
        requirements_template = tuple(self.validator.THIRD_PARTY_NOTICE_REQUIREMENTS)

        def run_inventory(
            extra_relative: str | None = None,
            add_requirement: bool = False,
            extra_directory: bool = False,
        ):
            """Run inventory validation against an isolated temporary package fixture."""
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                package = root / "sdk"
                notices = root / "THIRD_PARTY_NOTICES.md"
                requirements = []
                notice_lines = []
                for index, (_original, tokens) in enumerate(requirements_template):
                    relative = Path("Runtime/Plugins") / f"Dependency{index}.dll"
                    artifact = package / relative
                    artifact.parent.mkdir(parents=True, exist_ok=True)
                    artifact.write_bytes(b"dll")
                    requirements.append((artifact, tokens))
                    notice_lines.extend(tokens)
                if extra_relative is not None:
                    extra = package / extra_relative
                    extra.parent.mkdir(parents=True, exist_ok=True)
                    if extra_directory:
                        extra.mkdir(exist_ok=True)
                    elif extra.suffix.casefold() == ".dll":
                        extra.write_bytes(b"extra")
                    else:
                        extra.write_text("ignored", encoding="utf-8")
                    if add_requirement and extra.is_file():
                        requirements.append((extra, ("ExtraDependency", "MIT", extra_relative.replace("\\", "/"))))
                        notice_lines.extend(requirements[-1][1])
                analyzer = package / "Editor/SourceGenerators/analyzers/dotnet/cs/AnalyzerExtra.dll"
                analyzer.parent.mkdir(parents=True, exist_ok=True)
                analyzer.write_bytes(b"first-party")
                notices.write_text("\n".join(notice_lines), encoding="utf-8")
                with mock.patch.object(self.validator, "PACKAGE", package), \
                    mock.patch.object(self.validator, "THIRD_PARTY_NOTICES", notices), \
                    mock.patch.object(self.validator, "THIRD_PARTY_NOTICE_REQUIREMENTS", tuple(requirements)):
                    checker = getattr(self.validator, "check_third_party_notices", None)
                    self.assertIsNotNone(checker, "third-party notice checker is missing")
                    if checker is None:
                        return None
                    self.assertGreaterEqual(
                        len(inspect.signature(checker).parameters),
                        2,
                        "checker must accept the caller's package snapshot",
                    )
                    results = []
                    checker(results, list(package.rglob("*")))
                    return {item.name: item for item in results}

        baseline = run_inventory()
        self.assertTrue(baseline["third-party notice inventory closed"].ok)
        self.assertTrue(baseline["third-party notice artifact scope visible"].ok)
        self.assertTrue(baseline["third-party notices cover bundled binaries"].ok)

        for relative in ("UnexpectedDependency.dll", "Runtime/Plugins/UnexpectedNested.dll", "Uppercase.DLL"):
            with self.subTest(relative=relative):
                results = run_inventory(relative)
                self.assertFalse(results["third-party notice inventory closed"].ok)
                self.assertIn(Path(relative).name, results["third-party notice inventory closed"].detail)

        allowed = run_inventory("AllowedDependency.dll", add_requirement=True)
        self.assertTrue(allowed["third-party notice inventory closed"].ok)

        ignored = run_inventory("README.md")
        self.assertTrue(ignored["third-party notice inventory closed"].ok)
        self.assertTrue(run_inventory("metadata.json")["third-party notice inventory closed"].ok)
        self.assertTrue(run_inventory("Directory.dll", extra_directory=True)["third-party notice inventory closed"].ok)

    def test_main_passes_its_package_snapshot_to_notice_inventory(self) -> None:
        """The public entry point reuses its initial package enumeration."""
        with mock.patch.object(self.validator, "check_third_party_notices") as checker:
            with mock.patch.object(self.validator, "load_package_json", return_value={}):
                self.assertEqual(0, self.validator.main())
        checker.assert_called_once()
        self.assertGreaterEqual(
            len(checker.call_args.args),
            2,
            "main must pass its package snapshot to the notice checker",
        )
        if len(checker.call_args.args) >= 2:
            self.assertIsInstance(checker.call_args.args[1], list)

    def test_required_package_files_require_meta_sidecars(self) -> None:
        """Release-critical Unity assets must retain their tracked identities."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "package"
            required = (
                package / "README.md",
                package / "LICENSE",
                package / "Runtime/Unity.FoxgloveSDK.asmdef",
                package / "Editor/Unity.FoxgloveSDK.Editor.asmdef",
                package / "Editor/SourceGenerators/src/Unity.FoxgloveSDK.SourceGenerators.asmdef",
                package / "Runtime/Schemas/Proto/Unity.FoxgloveSDK.Proto.asmdef",
                package / "Runtime/link.xml",
                package / "Plugins/Google.Protobuf/Google.Protobuf.dll",
            )
            for path in required:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"content")
            self.validator.PACKAGE = package
            results = []
            self.validator.check_required_files(results)

        meta_results = [item for item in results if item.name.startswith("required meta:")]
        self.assertEqual(len(required), len(meta_results))
        self.assertTrue(all(not item.ok for item in meta_results))

    def test_third_party_notice_scope_rejects_absent_listed_artifact(self) -> None:
        """A missing configured artifact must not receive a scope PASS."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            notices = root / "THIRD_PARTY_NOTICES.md"
            notices.write_text("Example MIT missing.dll\n", encoding="utf-8")
            self.validator.THIRD_PARTY_NOTICES = notices
            self.validator.THIRD_PARTY_NOTICE_REQUIREMENTS = (
                (root / "missing.dll", ("Example", "MIT", "missing.dll")),
            )
            results = []
            self.validator.check_third_party_notices(results)

        scope = next(
            item for item in results
            if item.name == "third-party notice artifact scope visible"
        )
        self.assertFalse(scope.ok)

    def test_third_party_notices_require_tokens_in_one_attributable_section(self) -> None:
        """A global token match must not substitute for one artifact notice section."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "package"
            artifact = package / "Runtime" / "Plugins" / "ExampleDependency.dll"
            artifact.parent.mkdir(parents=True)
            artifact.write_bytes(b"dll")
            notices = root / "THIRD_PARTY_NOTICES.md"
            notices.write_text(
                "## Artifact name\nExampleDependency\n\n"
                "## License declaration\nMIT\n\n"
                "## Distribution path\nRuntime/Plugins/ExampleDependency.dll\n",
                encoding="utf-8",
            )

            with mock.patch.object(self.validator, "PACKAGE", package), \
                mock.patch.object(self.validator, "THIRD_PARTY_NOTICES", notices), \
                mock.patch.object(
                    self.validator,
                    "THIRD_PARTY_NOTICE_REQUIREMENTS",
                    ((artifact, ("ExampleDependency", "MIT", "Runtime/Plugins/ExampleDependency.dll")),),
                ):
                results = []
                self.validator.check_third_party_notices(results, list(package.rglob("*")))

        coverage = next(
            item for item in results
            if item.name == "third-party notices cover bundled binaries"
        )
        self.assertFalse(coverage.ok)
        self.assertIn("ExampleDependency.dll", coverage.detail)


__all__ = ["PackageNoticeInventoryTests"]
