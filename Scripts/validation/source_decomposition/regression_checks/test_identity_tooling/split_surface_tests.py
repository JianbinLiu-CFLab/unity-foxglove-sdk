from .foundation import *


class SplitSurfaceTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_identity_surface_parses_all_current_split_sections(self) -> None:
        """Every checked-in split section passes the static identity parser."""
        surfaces = compare_identity_surfaces._surfaces(
            compare_identity_surfaces.REPOSITORY_ROOT
        )
        self.assertEqual(85, len(surfaces))
        jazzy = surfaces[
            "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py"
        ]
        self.assertEqual(
            (
                "01_types_and_inputs",
                "02_package_files",
                "03_runtime_extraction_and_patching",
                "04_build_pipeline",
            ),
            jazzy[1],
        )
        lyrical = surfaces[
            "Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py"
        ]
        self.assertEqual(jazzy[1], lyrical[1])
        lyrical_orchestrator = surfaces[
            "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build.py"
        ]
        self.assertEqual(
            ("safety_and_environment", "build_pipeline", "evidence"),
            lyrical_orchestrator[1],
        )
        release_tooling = surfaces[
            "Scripts/release/regression_checks/test_release_tooling.py"
        ]
        self.assertEqual(
            (
                "foundation",
                "version_bump_tests",
                "run_ci_tests_part_1",
                "run_ci_tests_part_2",
                "run_ci_tests_part_3",
                "run_ci_tests_part_4",
                "run_ci_tests_part_5",
                "local_entrypoint_tests",
                "mcap_conformance_tests",
                "r2fu_artifact_handoff_tests",
                "unity_il2cpp_tests_part_1",
                "unity_il2cpp_tests_part_2",
                "unity_batch_compile_gate_tests",
            ),
            release_tooling[1],
        )
        template_registry = surfaces[
            "Scripts/validation/source_decomposition/compare_identity_surfaces/template_registry.py"
        ]
        self.assertEqual(
            ("template_helpers", "closure_mutations", "critical_bindings", "registry_mutations"),
            template_registry[1],
        )
        identity_tooling = surfaces[
            "Scripts/validation/source_decomposition/regression_checks/test_identity_tooling.py"
        ]
        self.assertEqual(
            (
                "foundation",
                "surface_rows_tests",
                "split_surface_tests",
                "split_contract_tests",
                "compatibility_surface_tests",
                "compatibility_initializer_tests",
                "loader_isolation_tests",
                "loader_entrypoint_tests",
                "loader_mutation_tests",
                "namespace_risk_tests",
                "namespace_execution_core_tests",
                "namespace_execution_edges_tests",
                "aggregate",
            ),
            identity_tooling[1],
        )

    def test_identity_surface_includes_nested_split_modules(self) -> None:
        """Nested package modules participate in the identity contract."""
        surface = compare_identity_surfaces._surfaces(
            compare_identity_surfaces.REPOSITORY_ROOT
        )["Scripts/unity_build/unity_il2cpp.py"]
        self.assertEqual(16, len(surface))
        self.assertIn(
            "parts/il2cpp_logging.py",
            {entry[0] for entry in surface[-1]},
        )

    def test_identity_foundation_imports_without_ast_trystar(self) -> None:
        """The identity foundation keeps Python 3.10 AST compatibility."""
        probe = (
            "import ast; "
            "hasattr(ast, 'TryStar') and delattr(ast, 'TryStar'); "
            "import Scripts.validation.source_decomposition.compare_identity_surfaces.foundation"
        )
        completed = subprocess.run(
            [sys.executable, "-c", probe],
            cwd=compare_identity_surfaces.REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_identity_package_imports_from_external_cwd_with_legacy_paths(self) -> None:
        """Legacy PYTHONPATH imports remain valid outside the repository root."""
        repository = compare_identity_surfaces.REPOSITORY_ROOT
        cases = (
            (repository / "Scripts" / "phase192", "import compare_identity_surfaces"),
            (repository / "Scripts", "import phase192.compare_identity_surfaces"),
        )
        with tempfile.TemporaryDirectory() as temporary:
            for python_path, script in cases:
                with self.subTest(python_path=python_path):
                    environment = os.environ.copy()
                    environment["PYTHONPATH"] = str(python_path)
                    completed = subprocess.run(
                        [sys.executable, "-c", script],
                        cwd=temporary,
                        env=environment,
                        capture_output=True,
                        text=True,
                        check=False,
                    )
                    self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_identity_foundation_preserves_type_alias_helper(self) -> None:
        """The split foundation preserves the legacy TypeAlias helper value."""
        self.assertIs(
            compare_identity_surfaces._TYPE_ALIAS_TYPE,
            ast.TypeAlias if hasattr(ast, "TypeAlias") else None,
        )

    def test_identity_namespace_facade_reexports_namespace_risk_constants(self) -> None:
        """The identity facade keeps the namespace-risk compatibility names."""
        namespace = __import__(
            "Scripts.validation.source_decomposition.identity_namespace_analysis",
            fromlist=["_NAMESPACE_RISK_NAMES"],
        )
        with self.subTest(name="_NAMESPACE_RISK_NAMES"):
            self.assertEqual(
                namespace._NAMESPACE_RISK_NAMES,
                compare_identity_surfaces._NAMESPACE_RISK_NAMES,
            )
        with self.subTest(name="_NAMESPACE_RISK_ATTRIBUTES"):
            self.assertEqual(
                namespace._NAMESPACE_RISK_ATTRIBUTES,
                compare_identity_surfaces._NAMESPACE_RISK_ATTRIBUTES,
            )
        with self.subTest(name="_NAMESPACE_RISK_MODULE_ATTRIBUTES"):
            self.assertEqual(
                namespace._NAMESPACE_RISK_MODULE_ATTRIBUTES,
                compare_identity_surfaces._NAMESPACE_RISK_MODULE_ATTRIBUTES,
            )

    def test_namespace_analysis_helpers_stay_below_source_split_threshold(self) -> None:
        """Namespace analysis implementation remains split into maintainable modules."""
        for relative in (
            "Scripts/validation/source_decomposition/identity_namespace_analysis.py",
            "Scripts/validation/source_decomposition/identity_namespace_scanner.py",
        ):
            with self.subTest(relative=relative):
                lines = (compare_identity_surfaces.REPOSITORY_ROOT / relative).read_text(encoding="utf-8").splitlines()
                self.assertLess(len(lines), 1000)

    def test_template_registry_facade_sections_stay_below_source_split_threshold(self) -> None:
        """Template registry validation is split into maintainable sections."""
        repository = compare_identity_surfaces.REPOSITORY_ROOT
        facade = repository / "Scripts/validation/source_decomposition/compare_identity_surfaces/template_registry.py"
        self.assertLess(len(facade.read_text(encoding="utf-8").splitlines()), 1000)
        package = facade.with_suffix("")
        sections = tuple(
            path
            for path in package.glob("*.py")
            if path.name != "__init__.py"
        )
        self.assertGreaterEqual(len(sections), 2)
        for section in sections:
            with self.subTest(section=section.name):
                self.assertLess(len(section.read_text(encoding="utf-8").splitlines()), 1000)

    def test_reviewed_template_registry_split_preserves_identity_contract(self) -> None:
        """The reviewed template registry split has an exact compatibility transition."""
        nested = tuple(
            (f"template_registry/{name}.py", (), (), ())
            for name in (
                "closure_mutations",
                "critical_bindings",
                "registry_mutations",
                "template_helpers",
            )
        )
        base_relative = ("legacy-foundation", "legacy-surface")
        head_relative = (
            "ImportFrom(module='template_registry', names=[alias(name='*')], level=1)",
            "legacy-surface",
        )
        self.assertTrue(
            compare_identity_surfaces._reviewed_template_registry_split(
                "Scripts/validation/source_decomposition/compare_identity_surfaces.py",
                (),
                nested,
                base_relative,
                head_relative,
            )
        )
        self.assertTrue(
            compare_identity_surfaces._reviewed_template_registry_split(
                "Scripts/validation/source_decomposition/compare_identity_surfaces.py",
                (),
                nested,
                (
                    "ImportFrom(module='foundation', names=[alias(name='a')], level=1)",
                    "legacy-middle",
                    "ImportFrom(module='foundation', names=[alias(name='b')], level=1)",
                    "legacy-surface",
                ),
                (
                    "ImportFrom(module='template_registry', names=[alias(name='*')], level=1)",
                    "ImportFrom(module='foundation', names=[alias(name='a')], level=1)",
                    "legacy-middle",
                    "legacy-surface",
                ),
            )
        )
        self.assertFalse(
            compare_identity_surfaces._reviewed_template_registry_split(
                "Scripts/validation/source_decomposition/compare_identity_surfaces.py",
                (),
                nested[:-1],
                base_relative,
                head_relative,
            )
        )

    def test_template_registry_facade_loads_via_fresh_module(self) -> None:
        """The nested facade remains loadable through the identity loader."""
        facade = (
            compare_identity_surfaces.REPOSITORY_ROOT
            / "Scripts/validation/source_decomposition/compare_identity_surfaces/template_registry.py"
        )
        loaded = source_layout.load_fresh_module(
            "Scripts.validation.source_decomposition.compare_identity_surfaces.template_registry",
            facade,
        )
        self.assertTrue(hasattr(loaded, "_phase192_template_name"))

    def test_template_registry_facade_loads_in_clean_process(self) -> None:
        """The identity loader works without a preloaded compare package."""
        repository = compare_identity_surfaces.REPOSITORY_ROOT
        script = """
from pathlib import Path
from Scripts.validation.source_decomposition import source_layout

facade = Path.cwd() / "Scripts/validation/source_decomposition/compare_identity_surfaces/template_registry.py"
loaded = source_layout.load_fresh_module(
    "Scripts.validation.source_decomposition.compare_identity_surfaces.template_registry",
    facade,
)
assert hasattr(loaded, "_phase192_template_name")
"""
        result = subprocess.run(
            [sys.executable, "-B", "-c", script],
            cwd=repository,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_reviewed_validator_isolation_transition_requires_exact_cleanup(self) -> None:
        """Only the reviewed eight-module cleanup insertion is compatibility-waived."""
        cleanup = "facade::7cdf800c9922e9368a642e72b34a53b8fa0049b842fda84b2ca8041537599915"
        humble_base = ("facade:old", "facade:rest")
        humble_head = (cleanup, *humble_base)
        self.assertTrue(
            compare_identity_surfaces._reviewed_validator_isolation_transition(
                "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
                humble_base,
                humble_head,
            )
        )
        for evil_head in (("facade:evil", *humble_base), ("facade:evil", "facade:old", "facade:rest")):
            with self.subTest(evil_head=evil_head):
                self.assertFalse(
                    compare_identity_surfaces._reviewed_validator_isolation_transition(
                        "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
                        humble_base,
                        evil_head,
                    )
                )
        self.assertFalse(
            compare_identity_surfaces._reviewed_validator_isolation_transition(
                "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
                humble_base,
                ("facade::mutated-cleanup", *humble_base),
            )
        )
        jazzy_base = ("section:core:stable", "facade:old", "facade:rest")
        self.assertTrue(
            compare_identity_surfaces._reviewed_validator_isolation_transition(
                "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
                jazzy_base,
                (jazzy_base[0], cleanup, *jazzy_base[1:]),
            )
        )

    def test_identity_helper_source_contract_reports_helper_only_changes(self) -> None:
        """Identity tooling reports changes made only in imported helper implementations."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            helper = root / "Scripts/validation/source_decomposition/identity_surface_conditions.py"
            helper.parent.mkdir(parents=True)
            helper.write_text("VALUE = 'base'\n", encoding="utf-8")
            base = compare_identity_surfaces._identity_helper_source_contract(root)
            helper.write_text("VALUE = 'head'\n", encoding="utf-8")
            head = compare_identity_surfaces._identity_helper_source_contract(root)
            self.assertNotEqual(base, head)
            self.assertIn(
                "HELPER_SOURCE_CHANGED Scripts/validation/source_decomposition/identity_surface_conditions.py",
                compare_identity_surfaces._compare_identity_helper_contracts(base, head),
            )
            self.assertIn(
                "Scripts/validation/source_decomposition/identity_surface_conditions.py",
                {entry[0] for entry in head},
            )

    def test_reviewed_namespace_split_requires_exact_helper_digests(self) -> None:
        """The reviewed namespace split waiver rejects any later helper mutation."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            base_root = root / "base"
            head_root = root / "head"
            for checkout in (base_root, head_root):
                for relative in compare_identity_surfaces._IDENTITY_HELPER_RELATIVES:
                    path = checkout / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    if relative == "Scripts/validation/source_decomposition/identity_namespace_analysis.py":
                        continue
                    if relative == "Scripts/validation/source_decomposition/identity_namespace_scanner.py":
                        continue
                    path.write_text("VALUE = 1\n", encoding="utf-8")
            base_analysis = subprocess.check_output(
                ["git", "show", "origin/main:Scripts/phase192/identity_namespace_analysis.py"],
                cwd=compare_identity_surfaces.REPOSITORY_ROOT,
            )
            head_analysis = (
                compare_identity_surfaces.REPOSITORY_ROOT
                / "Scripts/validation/source_decomposition/identity_namespace_analysis.py"
            ).read_bytes()
            head_scanner = (
                compare_identity_surfaces.REPOSITORY_ROOT
                / "Scripts/validation/source_decomposition/identity_namespace_scanner.py"
            ).read_bytes()
            (base_root / "Scripts/validation/source_decomposition/identity_namespace_analysis.py").write_bytes(base_analysis)
            (head_root / "Scripts/validation/source_decomposition/identity_namespace_analysis.py").write_bytes(head_analysis)
            (head_root / "Scripts/validation/source_decomposition/identity_namespace_scanner.py").write_bytes(head_scanner)
            base_contract = compare_identity_surfaces._identity_helper_source_contract(base_root)
            head_contract = compare_identity_surfaces._identity_helper_source_contract(head_root)
            self.assertEqual(
                [],
                compare_identity_surfaces._compare_identity_helper_contracts(
                    base_contract,
                    head_contract,
                    base_root=base_root,
                    head_root=head_root,
                ),
            )
            for relative in (
                "Scripts/validation/source_decomposition/identity_namespace_analysis.py",
                "Scripts/validation/source_decomposition/identity_namespace_scanner.py",
            ):
                with self.subTest(relative=relative):
                    path = head_root / relative
                    path.write_bytes(path.read_bytes() + b"\nMUTATION = True\n")
                    mutated = compare_identity_surfaces._identity_helper_source_contract(head_root)
                    errors = compare_identity_surfaces._compare_identity_helper_contracts(
                        base_contract,
                        mutated,
                        base_root=base_root,
                        head_root=head_root,
                    )
                    self.assertIn(f"HELPER_SOURCE_CHANGED {relative}", errors)
                    path.write_bytes(head_analysis if relative.endswith("analysis.py") else head_scanner)

    def test_load_fresh_module_isolates_absolute_phase192_helpers_across_checkouts(self) -> None:
        """Fresh loads must replace Scripts.validation.source_decomposition helpers from checkout A with checkout B."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            roots = []
            for marker in ("A", "B"):
                checkout = root / marker
                package = checkout / "Scripts/validation/source_decomposition"
                package.mkdir(parents=True)
                (checkout / "Scripts/__init__.py").write_text("\n", encoding="utf-8")
                (checkout / "Scripts/validation/__init__.py").write_text("\n", encoding="utf-8")
                (package / "__init__.py").write_text("\n", encoding="utf-8")
                (package / "identity_surface_conditions.py").write_text(
                    f"MARKER = {marker!r}\n", encoding="utf-8"
                )
                (package / "identity_namespace_analysis.py").write_text(
                    "from Scripts.validation.source_decomposition.identity_surface_conditions import MARKER\n"
                    "NAMESPACE_MARKER = MARKER\n",
                    encoding="utf-8",
                )
                facade = package / "compare_identity_surfaces.py"
                facade.write_text(
                    "from Scripts.validation.source_decomposition.identity_surface_conditions import MARKER\n"
                    "from Scripts.validation.source_decomposition.identity_namespace_analysis import NAMESPACE_MARKER\n",
                    encoding="utf-8",
                )
                roots.append((checkout, facade))

            names = (
                "compare_identity_surfaces",
                "Scripts",
                "Scripts.validation.source_decomposition",
                "Scripts.validation.source_decomposition.identity_surface_conditions",
                "Scripts.validation.source_decomposition.identity_namespace_analysis",
            )
            saved_modules = dict(sys.modules)
            for name in names:
                sys.modules.pop(name, None)
            try:
                first = source_layout.load_fresh_module("compare_identity_surfaces", roots[0][1])
                second = source_layout.load_fresh_module("compare_identity_surfaces", roots[1][1])
                self.assertEqual("A", first.MARKER)
                self.assertEqual("B", second.MARKER)
                self.assertEqual("B", second.NAMESPACE_MARKER)
                self.assertTrue(str(roots[1][0]) in sys.modules["Scripts.validation.source_decomposition.identity_namespace_analysis"].__file__)
                self.assertTrue(str(roots[1][0]) in sys.modules["Scripts.validation.source_decomposition.identity_surface_conditions"].__file__)
            finally:
                for name in tuple(sys.modules):
                    if name not in saved_modules:
                        sys.modules.pop(name, None)
                sys.modules.update(saved_modules)

    def test_load_fresh_module_preserves_real_scripts_package_graph_across_checkouts(self) -> None:
        """Fresh loads keep every Scripts parent package valid for real facade paths."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_names = (
                "package",
                "smoke",
                "smoke/foxglove",
                "smoke/foxrun",
                "unity_build",
                "ros2forunity",
                "validation",
                "validation/source_decomposition",
            )
            checkouts = []
            for marker in ("A", "B"):
                checkout = root / marker
                scripts = checkout / "Scripts"
                scripts.mkdir(parents=True)
                (scripts / "__init__.py").write_text(
                    f"MARKER = {marker!r}\n", encoding="utf-8"
                )
                for package_name in package_names:
                    package = scripts / package_name
                    package.mkdir(parents=True, exist_ok=True)
                    (package / "__init__.py").write_text(
                        f"MARKER = {marker!r}\n", encoding="utf-8"
                    )
                facade = scripts / "smoke/foxrun/phase184_profile_acceptance.py"
                facade.write_text(
                    "from Scripts import MARKER as SCRIPTS_MARKER\n"
                    "from Scripts.package import MARKER as PACKAGE_MARKER\n"
                    "from Scripts.smoke import MARKER as SMOKE_MARKER\n"
                    "from Scripts.smoke.foxglove import MARKER as FOXGLOVE_MARKER\n"
                    "from Scripts.smoke.foxrun import MARKER as FOXRUN_MARKER\n"
                    "from Scripts.unity_build import MARKER as UNITY_BUILD_MARKER\n"
                    "from Scripts.ros2forunity import MARKER as ROS2FORUNITY_MARKER\n"
                    "from Scripts.validation.source_decomposition import MARKER as PHASE192_MARKER\n"
                    "VALUE = (SCRIPTS_MARKER, PACKAGE_MARKER, SMOKE_MARKER, FOXGLOVE_MARKER, FOXRUN_MARKER, UNITY_BUILD_MARKER, ROS2FORUNITY_MARKER, PHASE192_MARKER)\n",
                    encoding="utf-8",
                )
                checkouts.append((checkout, facade))

            names = [
                "phase184_profile_acceptance",
                "Scripts",
                "Scripts.package",
                "Scripts.smoke",
                "Scripts.smoke.foxglove",
                "Scripts.smoke.foxrun",
                "Scripts.unity_build",
                "Scripts.ros2forunity",
                "Scripts.validation",
                "Scripts.validation.source_decomposition",
            ]
            saved_modules = dict(sys.modules)
            for name in names:
                sys.modules.pop(name, None)
            try:
                first = source_layout.load_fresh_module("phase184_profile_acceptance", checkouts[0][1])
                self.assertEqual(("A",) * 8, first.VALUE)
                second = source_layout.load_fresh_module("phase184_profile_acceptance", checkouts[1][1])
                self.assertEqual(("B",) * 8, second.VALUE)
                for name in names[1:]:
                    module = sys.modules[name]
                    self.assertIn(str(checkouts[1][0]), str(module.__file__))
            finally:
                for name in tuple(sys.modules):
                    if name == "Scripts" or name.startswith("Scripts."):
                        if name not in saved_modules:
                            sys.modules.pop(name, None)
                sys.modules.update(saved_modules)

    def test_load_fresh_module_removes_arbitrary_source_aliases(self) -> None:
        """Fresh loads remove aliases for source modules without a hardcoded package list."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            scripts = root / "Scripts"
            tool = scripts / "tool"
            tool.mkdir(parents=True)
            (scripts / "__init__.py").write_text("", encoding="utf-8")
            (tool / "__init__.py").write_text(
                "from Scripts.tool.implementation import VALUE\n",
                encoding="utf-8",
            )
            (tool / "implementation.py").write_text(
                "VALUE = 'fresh'\n",
                encoding="utf-8",
            )
            facade = tool / "facade.py"
            facade.write_text(
                "from Scripts.tool.implementation import VALUE\n",
                encoding="utf-8",
            )
            alias_module = types.ModuleType("Scripts.tool.implementation")
            alias_module.__file__ = str(tool / "implementation.py")
            alias_only_module = types.ModuleType("tool_alias.implementation")
            alias_only_module.__file__ = str(tool / "implementation.py")
            suffix_alias_module = types.ModuleType("implementation")
            suffix_alias_module.__file__ = str(tool / "implementation.py")
            names = (
                "facade",
                "Scripts",
                "Scripts.tool",
                "Scripts.tool.implementation",
                "tool_alias.implementation",
                "implementation",
            )
            saved_modules = dict(sys.modules)
            for name in names:
                sys.modules.pop(name, None)
            sys.modules["tool_alias.implementation"] = alias_only_module
            sys.modules["shared.implementation"] = suffix_alias_module
            try:
                loaded = source_layout.load_fresh_module("facade", facade)
                self.assertEqual("fresh", loaded.VALUE)
                self.assertIsNot(alias_module, sys.modules.get("Scripts.tool.implementation"))
                self.assertNotIn("tool_alias.implementation", sys.modules)
                self.assertNotIn("implementation", sys.modules)
            finally:
                for name in tuple(sys.modules):
                    if name not in saved_modules:
                        sys.modules.pop(name, None)
                sys.modules.update(saved_modules)

    def test_load_fresh_module_preserves_unrelated_repo_source_aliases(self) -> None:
        """Fresh loads must not evict an unrelated repo package used by later tests."""
        repository = compare_identity_surfaces.REPOSITORY_ROOT
        ambient_name = "ambient_ros2_alias.foundation"
        ambient = types.ModuleType(ambient_name)
        ambient.__file__ = str(
            repository / "Scripts/smoke/ros2/_ros2_windows_env/foundation.py"
        )
        saved_ambient = sys.modules.get(ambient_name)
        sys.modules[ambient_name] = ambient
        try:
            with tempfile.TemporaryDirectory(dir=repository / "Scripts") as temp:
                facade = Path(temp) / "tool_facade.py"
                facade.write_text("VALUE = 'fresh'\n", encoding="utf-8")
                loaded = source_layout.load_fresh_module("tool_facade", facade)
                self.assertEqual("fresh", loaded.VALUE)
                self.assertIs(ambient, sys.modules.get(ambient_name))
                sys.modules.pop("tool_facade", None)
        finally:
            if saved_ambient is None:
                sys.modules.pop(ambient_name, None)
            else:
                sys.modules[ambient_name] = saved_ambient

    def test_phase163_37_harness_guard_reads_composed_csharp_source(self) -> None:
        """Phase163-37 must inspect the facade and its split TestSources sections."""
        path = (
            compare_identity_surfaces.REPOSITORY_ROOT
            / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase163_37Validation.cs"
        )
        source = path.read_text(encoding="utf-8")
        self.assertIn("PhaseValidationSourceHelpers.ReadRequiredRepoText(", source)
        self.assertIn(
            '"Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/RuntimeValidationOptimizationTests.cs"',
            source,
        )

    def test_jazzy_ctrl_c_handler_has_shutdown_shared_implementation(self) -> None:
        """Jazzy ENABLE_MONO Ctrl+C handling resolves to a defined shared shutdown method."""
        facade = (
            compare_identity_surfaces.REPOSITORY_ROOT
            / "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
        )
        lifecycle = facade.parent / "Decomposed/ROS2ForUnity/ROS2ForUnity.Lifecycle.cs"
        combined = facade.read_text(encoding="utf-8") + "\n" + lifecycle.read_text(encoding="utf-8")
        self.assertIn("ShutdownShared();", combined)
        self.assertRegex(combined, r"(?:private|internal) static void ShutdownShared\s*\(")

    def test_identity_surface_uses_revision_local_source_layout(self) -> None:
        """Base/head worktrees load their own section-order parser."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package = root / "Scripts/smoke/foxrun/fixture"
            package.mkdir(parents=True)
            (package / "__init__.py").write_text(
                "from . import revision_live\n", encoding="utf-8"
            )
            (package / "revision_live.py").write_text(
                "VALUE = 1\n__all__ = ['VALUE']\n", encoding="utf-8"
            )
            parser_path = root / "Scripts/validation/source_decomposition/source_layout.py"
            parser_path.parent.mkdir(parents=True)
            parser_path.write_text(
                "raise RuntimeError('revision parser must not execute')\n",
                encoding="utf-8",
            )
            loaded = compare_identity_surfaces._section_modules_for_root(root)
            self.assertEqual((package / "revision_live.py",), loaded(package))

    def test_identity_surface_scans_all_split_roots(self) -> None:
        """The gate includes every checked-in decomposed source root."""
        facades = compare_identity_surfaces._split_facades(
            compare_identity_surfaces.REPOSITORY_ROOT
        )
        relative = {
            path.relative_to(compare_identity_surfaces.REPOSITORY_ROOT).as_posix()
            for path in facades
        }
        self.assertIn(
            "Scripts/smoke/websocket/phase185_foxrun_messagepack_probe.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/websocket/phase189_component_messagepack_probe.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/ros2/phase162_lyrical_zenoh_player_smoke.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/ros2/phase138s_imu_native_dds_acceptance.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/ros2/launch_phase138m_rviz2.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/ros2/launch_phase138t_camera_raw_rviz2.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/ros2/phase160_humble_lidar_deskew_acceptance.py",
            relative,
        )
        self.assertIn(
            "Scripts/smoke/foxrun/phase184_windows_job_owner.py",
            relative,
        )
        self.assertIn("Scripts/package/validate_unity_package.py", relative)
        self.assertIn("Scripts/release/bump_version.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py", relative)
        self.assertIn("Scripts/schema/regression_checks/test_schema_tooling.py", relative)
        self.assertIn("Scripts/smoke/test_core_smoke_scripts.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/humble/validate_r2fu_runtime_package.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py", relative)
        self.assertIn("Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py", relative)
        self.assertIn("Scripts/unity_build/unity_il2cpp.py", relative)
        self.assertIn("Scripts/release/run_ci.py", relative)
        self.assertIn(
            "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build.py",
            relative,
        )
        self.assertIn("Scripts/release/regression_checks/test_release_tooling.py", relative)
        self.assertIn("Scripts/validation/source_decomposition/compare_identity_surfaces.py", relative)
        self.assertEqual(85, len(relative))

    def test_identity_surface_recognizes_manifest_backed_decomposed_facade(self) -> None:
        """A Decomposed/<facade-stem> manifest participates in identity checks."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "Scripts/ros2forunity/windows/humble/build.py"
            package = root / "Scripts/ros2forunity/windows/humble/Decomposed/build"
            package.mkdir(parents=True)
            facade.write_text("PUBLIC = 1\n", encoding="utf-8")
            (package / "sections.manifest").write_text("01_core.py\n", encoding="utf-8")
            (package / "01_core.py").write_text("VALUE = 1\n", encoding="utf-8")
            surfaces = compare_identity_surfaces._surfaces(root, strict=True)
            self.assertIn("Scripts/ros2forunity/windows/humble/build.py", surfaces)
            self.assertEqual(("01_core",), surfaces["Scripts/ros2forunity/windows/humble/build.py"][1])

    def test_manifest_backed_identity_rejects_section_mutation(self) -> None:
        """A public section mutation is visible through a Decomposed manifest."""
        with tempfile.TemporaryDirectory() as temp:
            roots = [Path(temp) / name for name in ("base", "head")]
            names = (
                "01_types_and_inputs.py",
                "02_package_files.py",
                "03_runtime_extraction_and_patching.py",
                "04_build_pipeline.py",
            )
            for root in roots:
                package = root / "Scripts/ros2forunity/windows/jazzy/Decomposed/build_r2fu_runtime_package"
                package.mkdir(parents=True)
                (root / "Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py").write_text(
                    "PUBLIC = 1\n", encoding="utf-8"
                )
                (package / "sections.manifest").write_text(
                    "".join(name + "\n" for name in names), encoding="utf-8"
                )
                for name in names:
                    (package / name).write_text(
                        "SECTION_VALUE = 1\n", encoding="utf-8"
                    )
            (
                roots[1]
                / "Scripts/ros2forunity/windows/jazzy/Decomposed/build_r2fu_runtime_package/sections.manifest"
            ).write_text(
                "".join(name + "\n" for name in (names[0], names[1], names[3], names[2])),
                encoding="utf-8",
            )
            base = compare_identity_surfaces._surfaces(roots[0], strict=True)
            head = compare_identity_surfaces._surfaces(roots[1], strict=True)
            errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
            self.assertTrue(errors)
            self.assertIn(
                "SECTION_ORDER_CHANGED Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py",
                errors,
            )


__all__ = ["SplitSurfaceTests"]
