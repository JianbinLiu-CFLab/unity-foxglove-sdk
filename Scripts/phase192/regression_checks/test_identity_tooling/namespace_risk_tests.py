from .foundation import *


class NamespaceRiskTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_split_section_rejects_shadowed_globals_builtin(self) -> None:
        """Dynamic __all__ cannot invoke a module-defined globals replacement."""
        source = (
            "def globals():\n"
            "    return {'PUBLIC': 1}\n"
            "PUBLIC = 1\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_entrypoint_rejects_untrusted_test_suite_generator(self) -> None:
        """Generated test entrypoints cannot execute a side-effecting iterator."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("modules = ()\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import unittest\nfrom . import modules\n"
                "def side_effect():\n    return modules\n"
                "suite = unittest.TestSuite(unittest.defaultTestLoader.loadTestsFromModule(m) for m in side_effect())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_nested_f_string_side_effect(self) -> None:
        """Generated entrypoints cannot execute calls inside error messages."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("def main(): return 0\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import sys\nfrom . import main\n"
                "if __name__ == \"__main__\":\n"
                "    try:\n"
                "        raise SystemExit(main())\n"
                "    except Exception as exc:\n"
                "        print(f\"FAIL {print('unexpected')}\", file=sys.stderr)\n"
                "        raise SystemExit(1)\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_raise_cause_side_effect(self) -> None:
        """Generated entrypoints cannot execute calls in raise causes."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("def main(): return 0\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import sys\nfrom . import main\n"
                "if __name__ == \"__main__\":\n"
                "    try:\n"
                "        raise SystemExit(main())\n"
                "    except Exception as exc:\n"
                "        print(\"FAIL\", file=sys.stderr)\n"
                "        raise SystemExit(1) from print('unexpected')\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_try_without_delegated_call(self) -> None:
        """Generated entrypoints must call an approved command on the normal path."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("def main(): return 0\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import sys\nfrom . import main\n"
                "if __name__ == \"__main__\":\n"
                "    try:\n"
                "        print(\"OK\", file=sys.stderr)\n"
                "    except Exception as exc:\n"
                "        print(\"FAIL\", file=sys.stderr)\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_text_runner_keywords(self) -> None:
        """Generated test entrypoints cannot execute TextTestRunner arguments."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("__all__ = [\"modules\"]\nmodules = ()\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import unittest\nfrom . import *\n"
                "if __name__ == \"__main__\":\n"
                "    suite = unittest.TestSuite(unittest.defaultTestLoader.loadTestsFromModule(m) for m in modules)\n"
                "    result = unittest.TextTestRunner(print(\"unexpected\")).run(suite)\n"
                "    raise SystemExit(1)\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_generated_section_rejects_top_level_metric_exec(self) -> None:
        """Metric helper exec is not allowed at generated-section import time."""
        source = "POINTCLOUD2_METRIC_HELPERS = 'x'\nnamespace = {}\nexec(POINTCLOUD2_METRIC_HELPERS, namespace, namespace)\n"
        with self.assertRaises(ValueError):
            compare_identity_surfaces._validate_generated_section(
                source,
                Path("Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance/pointcloud_metrics.py"),
                Path("Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance"),
                frozenset({"pointcloud_metrics"}),
            )

    def test_generated_section_rejects_import_time_alias_side_effects(self) -> None:
        """Import-time process and filesystem calls cannot hide behind aliases."""
        sources = (
            "import os as operating_system\noperating_system.system('bad')\n",
            "import subprocess as process\nprocess.run(['bad'])\n",
            "import shutil\nshutil.rmtree('bad')\n",
            "from pathlib import Path\nPath('bad').write_text('x')\n",
            "import sys\nsys.path.insert(0, 'bad')\n",
            "import os\n@os.system('bad')\ndef helper():\n    pass\n",
        )
        for source in sources:
            with self.subTest(source=source), self.assertRaises(ValueError):
                compare_identity_surfaces._validate_generated_section(
                    source + "PUBLIC = 1\n__all__ = ['PUBLIC']\n",
                    Path("Scripts/smoke/fixture.py"),
                    Path("Scripts/smoke"),
                    frozenset({"fixture"}),
                )

    def test_generated_initializer_marker_requires_real_template_node(self) -> None:
        """Comments and string literals cannot opt a package into generated mode."""
        self.assertFalse(
            compare_identity_surfaces._phase192_generated_initializer(
                "# _Phase192PackageModule\nVALUE = '_Phase138UPackageModule'\n"
            )
        )

    def test_entrypoint_rejects_dynamic_unittest_arguments(self) -> None:
        """Unittest entrypoints must use literal module and exit options."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "import unittest\ndef module_name(): return 'bad'\n"
                "if __name__ == '__main__':\n"
                "    unittest.main(module=module_name())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_namespace_risk_signatures_distinguish_import_time_defaults(self) -> None:
        """Default expressions remain import-time risks unlike function bodies."""
        body = ast.parse('def helper():\n    return eval("1+1")\n')
        default = ast.parse('def helper(value=eval("1+1")):\n    return value\n')
        self.assertNotEqual(
            compare_identity_surfaces._namespace_risk_signatures(body),
            compare_identity_surfaces._namespace_risk_signatures(default),
        )

    def test_namespace_risk_signatures_detect_import_time_process_aliases(self) -> None:
        """Import-time calls through process-module aliases remain visible to the gate."""
        sources = (
            "import os as host\n"
            "def helper():\n"
            "    host.popen('bad')\n"
            "helper()\n",
            "import shutil\n"
            "def helper():\n"
            "    shutil.rmtree('bad')\n"
            "helper()\n",
        )
        for source in sources:
            with self.subTest(source=source):
                self.assertTrue(compare_identity_surfaces._namespace_risk_signatures(ast.parse(source)))

    def test_generated_section_rejects_called_function_namespace_risks(self) -> None:
        """Generated sections reject risky calls reachable from import-time helpers."""
        sources = (
            "def helper():\n"
            "    exec('bad')\n"
            "helper()\n",
            "def helper():\n"
            "    getattr(object(), 'bad')\n"
            "helper()\n",
            "def helper():\n"
            "    globals()\n"
            "helper()\n",
            "import os\n"
            "def helper():\n"
            "    os.popen('bad')\n"
            "helper()\n",
            "import shutil\n"
            "def helper():\n"
            "    shutil.rmtree('bad')\n"
            "helper()\n",
        )
        for source in sources:
            with self.subTest(source=source), self.assertRaises(ValueError):
                compare_identity_surfaces._validate_generated_section(
                    source + "PUBLIC = 1\n__all__ = ['PUBLIC']\n",
                    Path("Scripts/smoke/fixture.py"),
                    Path("Scripts/smoke"),
                    frozenset({"fixture"}),
                )

    def test_generated_section_rejects_path_and_sys_path_mutations(self) -> None:
        """Generated sections reject filesystem opens and path list mutation."""
        sources = (
            "from pathlib import Path\nPath('bad').open('w')\n",
            "from pathlib import Path\nPath('bad').touch()\n",
            "import sys\nsys.path.append('bad')\n",
            "import sys\nsys.path.extend(['bad'])\n",
        )
        for source in sources:
            with self.subTest(source=source), self.assertRaises(ValueError):
                compare_identity_surfaces._validate_generated_section(
                    source + "PUBLIC = 1\n__all__ = ['PUBLIC']\n",
                    Path("Scripts/smoke/fixture.py"),
                    Path("Scripts/smoke"),
                    frozenset({"fixture"}),
                )

    def test_generated_section_allows_only_file_derived_sys_path_bootstrap(self) -> None:
        """Generated sections cannot redirect imports to an arbitrary path."""
        accepted = (
            "from pathlib import Path\n"
            "import sys as _phase192_sys\n"
            "_phase192_parent = Path(__file__).resolve().parent\n"
            "_phase192_sys.path.insert(0, str(_phase192_parent))\n"
            "PUBLIC = 1\n__all__ = ['PUBLIC']\n"
        )
        compare_identity_surfaces._validate_generated_section(
            accepted,
            Path("Scripts/smoke/fixture.py"),
            Path("Scripts/smoke"),
            frozenset({"fixture"}),
        )
        rejected = (
            "import sys\n"
            "sys.path.insert(1, '/tmp/evil')\n"
            "PUBLIC = 1\n__all__ = ['PUBLIC']\n",
            "import sys\n"
            "sys.path.insert(0, '/tmp/evil')\n"
            "PUBLIC = 1\n__all__ = ['PUBLIC']\n",
        )
        for source in rejected:
            with self.subTest(source=source), self.assertRaisesRegex(ValueError, "sys.path bootstrap"):
                compare_identity_surfaces._validate_generated_section(
                    source,
                    Path("Scripts/smoke/fixture.py"),
                    Path("Scripts/smoke"),
                    frozenset({"fixture"}),
                )

    def test_loader_registry_rejects_untrusted_spec_name_assignment(self) -> None:
        """Loader registry writes must restore only the current module object."""
        source = ast.parse(
            "import sys as _phase192_sys\n"
            "class Spec:\n"
            "    name = 'subprocess'\n"
            "spec = Spec()\n"
            "_phase192_sys.modules[spec.name] = object()\n"
        )
        with self.assertRaisesRegex(ValueError, "module registry mutation"):
            compare_identity_surfaces._phase192_validate_template_registry_mutations(
                source,
                "Scripts/package/validate_source_generator_dll.py",
            )

    def test_configuration_exemption_is_phase121_specific(self) -> None:
        """Only the reviewed Phase121 configuration gets the narrow exemption."""
        self.assertTrue(
            compare_identity_surfaces._is_phase121_configuration_path(
                "Scripts/mcap/conformance/run_phase121_conformance/configuration.py"
            )
        )
        self.assertFalse(
            compare_identity_surfaces._is_phase121_configuration_path(
                "Scripts/other/configuration.py"
            )
        )
        self.assertFalse(
            compare_identity_surfaces._is_phase121_configuration_path(
                "Scripts/otherScripts/mcap/conformance/run_phase121_conformance/configuration.py"
            )
        )
        self.assertFalse(
            compare_identity_surfaces._is_phase121_configuration_path(
                "C:/other/Scripts/mcap/conformance/run_phase121_conformance/configuration.py"
            )
        )

    def test_configuration_exemption_uses_supplied_checkout_root(self) -> None:
        """Archive paths are matched relative to the checkout being inspected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            relative = "Scripts/mcap/conformance/run_phase121_conformance/configuration.py"
            self.assertTrue(compare_identity_surfaces._is_phase121_configuration_path(relative, root))
            self.assertTrue(
                compare_identity_surfaces._is_phase121_configuration_path(root / relative, root)
            )
            self.assertFalse(
                compare_identity_surfaces._is_phase121_configuration_path(
                    root / "other" / relative,
                    root,
                )
            )

    def test_identity_surfaces_accept_archive_checkout_root(self) -> None:
        """The identity gate accepts its Phase121 exception inside an archive checkout."""
        repository = Path(__file__).resolve().parents[3]
        with compare_identity_surfaces._revision_checkout(repository, "HEAD") as checkout:
            surfaces = compare_identity_surfaces._surfaces(checkout, strict=False)
        self.assertTrue(surfaces)

    def test_namespace_gate_ignores_local_variable_annotations(self) -> None:
        """Local and postponed variable annotations are not evaluated at import time."""
        sources = (
            "import subprocess\n"
            "def initialize():\n"
            "    value: subprocess.run([\"bad\"]) = 1\n"
            "initialize()\n",
            "from __future__ import annotations\n"
            "import subprocess\n"
            "value: subprocess.run([\"bad\"]) = 1\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertFalse(any("subprocess" in item for item in risks))

    def test_workflow_keeps_identity_checker_trusted_base_only(self) -> None:
        """The compatibility gate must not overwrite the trusted base checker."""
        workflow = Path('.github/workflows/dotnet-tests.yml').read_text(encoding='utf-8')
        self.assertNotIn('cp Scripts/phase192/compare_identity_surfaces.py "$base_checker"', workflow)

    def test_workflow_fallback_allowlists_cover_reviewed_lyrical_transitions(self) -> None:
        """Fallback identity transitions must enumerate the complete reviewed file sets."""
        workflow = Path('.github/workflows/dotnet-tests.yml').read_text(encoding='utf-8')
        reviewed_transition = {
            "Scripts/phase192/regression_checks/test_identity_tooling.py",
            "Scripts/phase192/source_layout.py",
            "Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py",
            "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build/implementation.py",
            "Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package/matrix_tests.py",
            "Scripts/ros2forunity/windows/lyrical/regression_checks/test_phase146b_build_defaults.py",
            "Scripts/ros2forunity/windows/lyrical/regression_checks/test_validate_r2fu_runtime_package/layout_metadata_tests.py",
        }
        for path in reviewed_transition:
            with self.subTest(path=path):
                self.assertIn(path, workflow)
        self.assertIn("IDENTITY_GATE_REVIEWED_LYRICAL_VALIDATOR_SECTION_SPLIT", workflow)
        self.assertIn("IDENTITY_GATE_REVIEWED_PHASE146B_BUILD_FACADE_SPLIT", workflow)
        self.assertIn("sensitive_transition_paths = {", workflow)
        self.assertIn("changed_paths & sensitive_transition_paths", workflow)
        expected_block = workflow.split("expected_phase192_split_paths = {", 1)[1].split("}", 1)[0]
        self.assertNotIn(".github/workflows/dotnet-tests.yml", expected_block)
        self.assertNotIn("Scripts/phase192/compare_identity_surfaces.py", expected_block)
        package_block = workflow.split("expected_lyrical_package_paths = {", 1)[1].split("}", 1)[0]
        self.assertNotIn(".github/workflows/dotnet-tests.yml", package_block)
        self.assertNotIn("Scripts/phase192/compare_identity_surfaces.py", package_block)
        self.assertIn("phase146b_validator_errors = [", workflow)
        self.assertIn('"capture_cmd_environment",', workflow)
        self.assertIn('"capture_ros2_environment",', workflow)
        self.assertIn('"classify_output",', workflow)
        self.assertIn("phase146b_expected_missing_symbols = (", workflow)
        self.assertIn(
            "actual_missing_symbols != list(phase146b_expected_missing_symbols)",
            workflow,
        )
        self.assertNotIn("expected_validator_split_paths = expected_phase192_split_paths", workflow)
        validator_block = workflow.split("expected_validator_split_paths = {", 1)[1].split("}", 1)[0]
        for path in (
            "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package/__init__.py",
            "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package/implementation_lyrical.py",
            "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package/manifest_and_inventory.py",
            "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package/runtime_checks.py",
        ):
            self.assertIn(path, validator_block)
        self.assertNotIn(".github/workflows/dotnet-tests.yml", validator_block)
        self.assertNotIn("Scripts/phase192/compare_identity_surfaces.py", validator_block)

    def test_compatibility_rejects_mutation_of_legacy_entrypoint(self) -> None:
        """Legacy compatibility paths still reject an entrypoint digest mutation."""
        relative = 'Scripts/mcap/conformance/run_phase121_conformance.py'
        base = {relative: (frozenset({'main'}), (), (), frozenset(), 'base', frozenset(), True, (), frozenset(), (), 'entry-base', (), (), (), frozenset({'main'}))}
        head = {relative: (frozenset({'main'}), (), (), frozenset(), 'base', frozenset(), True, (), frozenset(), (), 'entry-head', (), (), (), frozenset({'main'}))}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn('MODULE_ENTRYPOINT_CHANGED ' + relative, errors)

    def test_compatibility_allows_reviewed_lyrical_direct_entrypoint(self) -> None:
        """The reviewed Lyrical path bootstrap is compatible with the old wrapper."""
        relative = 'Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py'
        base_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "from . import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=None))\n"
        )
        head_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
            "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=__name__))\n"
        )
        base_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(base_source)
        )
        head_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(head_source)
        )
        self.assertTrue(
            compare_identity_surfaces._reviewed_direct_entrypoint_transition(
                relative,
                base_contract,
                head_contract,
            )
        )

    def test_compatibility_rejects_unreviewed_lyrical_entrypoint_call(self) -> None:
        """The reviewed path does not authorize extra executable calls."""
        relative = 'Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py'
        base_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "from . import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=None))\n"
        )
        head_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
            "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n"
            "print('unexpected')\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=__name__))\n"
        )
        base_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(base_source)
        )
        head_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(head_source)
        )
        self.assertFalse(
            compare_identity_surfaces._reviewed_direct_entrypoint_transition(
                relative,
                base_contract,
                head_contract,
            )
        )

    def test_compatibility_rejects_unreviewed_lyrical_entrypoint_definitions_and_order(self) -> None:
        """The reviewed path preserves top-level definitions and bootstrap order."""
        relative = 'Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py'
        base_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "from . import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=None))\n"
        )
        canonical_head = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
            "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=__name__))\n"
        )
        variants = (
            canonical_head.replace(
                "if __name__ == '__main__':\n",
                "def unittest():\n"
                "    pass\n"
                "if __name__ == '__main__':\n",
                1,
            ),
            canonical_head + "\ndef load_tests(loader, tests, pattern):\n    raise SystemExit(99)\n",
            canonical_head + "\ndef __getattr__(name):\n    return None\n",
            canonical_head.replace(
                "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n",
                "",
            ).replace(
                "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n",
                "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n"
                "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n",
            ),
        )
        base_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(base_source)
        )
        base_surface = (
            frozenset({'main'}), (), (), frozenset(), 'digest', frozenset(), True, (),
            frozenset(), (), 'entry-base', base_contract, (), (), frozenset({'main'}),
        )
        for source in variants:
            with self.subTest(source=source):
                head_contract = compare_identity_surfaces._entrypoint_contract_signatures(
                    ast.parse(source)
                )
                self.assertFalse(
                    compare_identity_surfaces._reviewed_direct_entrypoint_transition(
                        relative,
                        base_contract,
                        head_contract,
                    )
                )
                head_surface = (
                    frozenset({'main'}), (), (), frozenset(), 'digest', frozenset(), True, (),
                    frozenset(), (), 'entry-head', head_contract, (), (), frozenset({'main'}),
                )
                errors = compare_identity_surfaces._compare_surfaces(
                    {relative: base_surface},
                    {relative: head_surface},
                    strict=False,
                )
                self.assertIn('MODULE_ENTRYPOINT_CHANGED ' + relative, errors)

    def test_compatibility_does_not_waive_namespace_contract_for_entrypoint_transition(self) -> None:
        """Entrypoint migration cannot mask a simultaneous namespace-risk change."""
        relative = 'Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py'
        base_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "from . import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=None))\n"
        )
        head_source = (
            "from __future__ import annotations\n"
            "import unittest\n"
            "import sys\n"
            "from pathlib import Path\n"
            "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
            "from Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package import *\n"
            "if __name__ == '__main__':\n"
            "    raise SystemExit(unittest.main(module=__name__))\n"
        )
        base_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(base_source)
        )
        head_contract = compare_identity_surfaces._entrypoint_contract_signatures(
            ast.parse(head_source)
        )
        base = {
            relative: (
                frozenset({'main'}), (), (), frozenset(), 'digest', frozenset(), True, (),
                frozenset(), (), 'entry-base', base_contract, (), (), frozenset({'main'}),
            )
        }
        head = {
            relative: (
                frozenset({'main'}), (), (), frozenset(), 'digest', frozenset(), True, (),
                frozenset({'namespace-risk'}), (), 'entry-head', head_contract, (), (), frozenset({'main'}),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn('NAMESPACE_CONTRACT_CHANGED ' + relative, errors)

    def test_archive_entrypoint_accepts_reviewed_absolute_import_without_runtime_path(self) -> None:
        """The trusted archive checker resolves the reviewed import from the checkout tree."""
        relative = Path(
            "Scripts/ros2forunity/windows/lyrical/regression_checks/"
            "test_build_r2fu_runtime_package.py"
        )
        with tempfile.TemporaryDirectory() as temp:
            repository = Path(temp)
            package = repository / relative.parent / relative.stem
            package.mkdir(parents=True)
            (package / "__init__.py").write_text("VALUE = 1\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "from __future__ import annotations\n"
                "import unittest\n"
                "import sys\n"
                "from pathlib import Path\n"
                "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
                "from Scripts.ros2forunity.windows.lyrical.regression_checks."
                "test_build_r2fu_runtime_package import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(unittest.main(module=__name__))\n",
                encoding="utf-8",
            )
            with mock.patch.object(
                compare_identity_surfaces,
                "_module_spec_exists",
                side_effect=lambda name: not name.startswith("Scripts."),
            ):
                present, *_ = compare_identity_surfaces._entrypoint_contract(
                    path,
                    frozenset(),
                    repository_root=repository,
                )
            self.assertTrue(present)

    def test_archive_entrypoint_rejects_unreviewed_absolute_import_without_runtime_path(self) -> None:
        """Archive fallback does not authorize an unrelated absolute import."""
        with tempfile.TemporaryDirectory() as temp:
            repository = Path(temp)
            package = repository / "Scripts/other/regression_checks/example"
            package.mkdir(parents=True)
            (package / "__init__.py").write_text("VALUE = 1\n", encoding="utf-8")
            path = package.parent / "__main__.py"
            path.write_text(
                "import unittest\n"
                "from Scripts.other.regression_checks.example import *\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(unittest.main(module=__name__))\n",
                encoding="utf-8",
            )
            with mock.patch.object(
                compare_identity_surfaces,
                "_module_spec_exists",
                side_effect=lambda name: not name.startswith("Scripts."),
            ):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(
                        path,
                        frozenset(),
                        repository_root=repository,
                    )

    def test_generated_initializer_kind_uses_ast_classification(self) -> None:
        """Initializer routing follows AST nodes rather than source substrings."""
        self.assertEqual(
            "phase192",
            compare_identity_surfaces._phase192_initializer_kind(
                "class _Phase192PackageModule: pass\n"
            ),
        )
        self.assertEqual(
            "phase138u",
            compare_identity_surfaces._phase192_initializer_kind(
                "from .pointcloud_metrics import value\n"
            ),
        )
        self.assertIsNone(
            compare_identity_surfaces._phase192_initializer_kind(
                "VALUE = '_Phase192PackageModule'\n"
            )
        )


__all__ = ["NamespaceRiskTests"]
