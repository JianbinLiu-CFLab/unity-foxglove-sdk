from .foundation import *


class LoaderMutationTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_generated_facade_rejects_closure_mutations(self) -> None:
        """Generated facades cannot mutate protected state through wrappers or aliases."""
        for facade in (
            Path("Scripts/package/validate_source_generator_dll.py"),
            Path("Scripts/mcap/mcap_time_sync.py"),
        ):
            source = facade.read_text(encoding="utf-8")
            variants = {
                "facade_method_body": source.replace(
                    "        super().__setattr__(name, value)\n",
                    "        super().__setattr__(name, value)\n"
                    "        setattr(_phase192_package_module, \"__name__\", \"evil\")\n",
                    1,
                ),
                "protected_types_alias": source + (
                    "\nimport types as _evil_types\n"
                    "_evil_types.ModuleType = object\n"
                ),
                "protected_types_propagated_alias": source + (
                    "\n_evil_types = _phase192_types\n"
                    "_evil_types.ModuleType = object\n"
                ),
                "protected_package_propagated_alias": source + (
                    "\n_evil_package = _phase192_package_module\n"
                    "_evil_package.__name__ = \"evil\"\n"
                ),
                "protected_metadata_alias": source + (
                    "\n_evil_spec = __spec__\n"
                    "_evil_spec.name = \"evil\"\n"
                ),
                "protected_metadata_store": source + '\n__spec__.name = "evil"\n',
                "globals_protected_store": source + (
                    "\nglobals()[\"_phase192_package_module\"].__name__ = \"evil\"\n"
                ),
                "globals_get_protected_store": source + (
                    "\nglobals().get(\"_phase192_package_module\").__name__ = \"evil\"\n"
                ),
                "globals_get_protected_subscript": source + (
                    "\nglobals().get(\"_phase192_package_module\")[\"__name__\"] = \"evil\"\n"
                ),
                "dict_protected_store": source + (
                    "\n__dict__[\"__spec__\"].name = \"evil\"\n"
                ),
                "dict_get_protected_store": source + (
                    "\n__dict__.get(\"__spec__\").name = \"evil\"\n"
                ),
                "dict_alias_protected_store": source + (
                    "\nd = __dict__\n"
                    "d[\"__spec__\"].name = \"evil\"\n"
                ),
                "for_alias_target": source + (
                    "\nif True:\n"
                    "    for _evil_package in (_phase192_package_module,):\n"
                    "        _evil_package.__name__ = \"evil\"\n"
                ),
                "async_for_alias_target": source + (
                    "\nif True:\n"
                    "    async def _phase192_probe():\n"
                    "        async for _evil_package in (_phase192_package_module,):\n"
                    "            _evil_package.__name__ = \"evil\"\n"
                ),
                "for_globals_get_alias": source + (
                    "\nif True:\n"
                    "    for _evil_package in (globals().get(\"_phase192_package_module\"),):\n"
                    "        _evil_package.__name__ = \"evil\"\n"
                ),
                "async_for_globals_get_alias": source + (
                    "\nif True:\n"
                    "    async def _phase192_probe():\n"
                    "        async for _evil_package in (globals().get(\"_phase192_package_module\"),):\n"
                    "            _evil_package.__name__ = \"evil\"\n"
                ),
                "comprehension_alias_target": source + (
                    "\n[_evil_package.__setattr__(\"__name__\", \"evil\") for _evil_package in (_phase192_package_module,)]\n"
                ),
                "cli_main_globals_store": source + (
                    "\n_phase192_main.__globals__[\"_phase192_package_module\"].__name__ = \"evil\"\n"
                ),
                "main_globals_store": source + (
                    "\nmain.__globals__[\"DEFAULT_IMU_TOPIC\"] = \"evil-topic\"\n"
                ),
                "main_globals_getattr_pop": source + (
                    "\nif callable(main):\n"
                    "    getattr(main, \"__globals__\").pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "main_globals_getattr_default_pop": source + (
                    "\ngetattr(main, \"__globals__\", None).pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "main_globals_getattr_dynamic_pop": source + (
                    "\ngetattr(main, \"__\" + \"globals__\").pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "main_globals_getattr_keyword_pop": source + (
                    "\ngetattr(object=main, name=\"__globals__\", default=None).pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "cli_main_globals_getattr_store": source + (
                    "\ngetattr(_phase192_main, \"__globals__\")[\"_phase192_package_module\"] = None\n"
                ),
                "cli_entrypoint_globals_getattr_attr": source + (
                    "\ngetattr(_phase192_entrypoint, \"__globals__\").__setitem__(\n"
                    "    \"_phase192_package_module\", None\n"
                    ")\n"
                ),
                "getattr_globals_alias": source + (
                    "\n_evil_globals = getattr(_phase192_main, \"__globals__\")\n"
                    "_evil_globals[\"DEFAULT_IMU_TOPIC\"] = \"evil-topic\"\n"
                ),
                "getattr_globals_for_alias": source + (
                    "\nif True:\n"
                    "    for _evil_globals in (getattr(_phase192_entrypoint, \"__globals__\"),):\n"
                    "        _evil_globals.pop(\"_phase192_package_module\", None)\n"
                ),
                "getattr_globals_comprehension": source + (
                    "\n[getattr(_phase192_main, \"__globals__\").pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                    " for _x in (0,)]\n"
                ),
                "globals_get_main_globals_pop": source + (
                    "\nglobals().get(\"main\").__globals__.pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "globals_subscript_main_globals_pop": source + (
                    "\nglobals()[\"main\"].__globals__.pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "globals_get_alias_loop": source + (
                    "\nif True:\n"
                    "    for _evil_globals in (globals().get(\"main\").__globals__,):\n"
                    "        _evil_globals.pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "cli_entrypoint_globals_store": source + (
                    "\n_phase192_entrypoint.__globals__[\"_phase192_package_module\"].__name__ = \"evil\"\n"
                ),
                "previous_path_alias": source + (
                    "\n_evil_path = _phase192_previous_path\n"
                    "_evil_path[0] = \"evil\"\n"
                ),
            }
            for name, variant in variants.items():
                with self.subTest(facade=str(facade), name=name), self.assertRaises(ValueError):
                    compare_identity_surfaces._symbols(
                        variant,
                        str(facade),
                        repository_root=Path.cwd(),
                    )

    def test_split_section_rejects_getattr_globals_mutation(self) -> None:
        """Split sections use the same protected-mutation validator as facades."""
        with tempfile.TemporaryDirectory() as temp:
            variants = {
                "direct": (
                    "getattr(_phase192_main, \"__globals__\", None).pop(\n"
                    "    \"DEFAULT_IMU_TOPIC\", None\n"
                    ")\n"
                ),
                "alias": (
                    "_f = _phase192_main\n"
                    "getattr(_f, \"__globals__\", None).pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "loop": (
                    "for _f in (_phase192_main,):\n"
                    "    getattr(_f, \"__globals__\", None).pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                ),
                "parameter": (
                    "def mutate(fn):\n"
                    "    getattr(fn, \"__globals__\", None).pop(\"DEFAULT_IMU_TOPIC\", None)\n"
                    "mutate(_phase192_main)\n"
                ),
            }
            for name, mutation in variants.items():
                with self.subTest(name=name):
                    section = Path(temp) / f"section_{name}.py"
                    source = "__all__ = []\n" + mutation
                    section.write_text(source, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        compare_identity_surfaces._section_exports(
                            source,
                            str(section),
                            repository_root=Path(temp),
                        )

    def test_generated_initializer_rejects_import_time_main_call(self) -> None:
        """Package initializers cannot invoke an exported CLI during import."""
        initializer = Path("Scripts/mcap/mcap_time_sync/__init__.py")
        tree = ast.parse(initializer.read_text(encoding="utf-8") + "\nmain()\n", str(initializer))
        with self.assertRaises(ValueError):
            compare_identity_surfaces._phase192_template_validate_initializer(tree, str(initializer))

    def test_generated_initializer_rejects_untrusted_section_export_value(self) -> None:
        """Section export loops must copy the named section attribute exactly."""
        initializer = Path("Scripts/smoke/websocket/pointcloud_qos_probe/__init__.py")
        source = initializer.read_text(encoding="utf-8")
        for replacement in (
            "globals()[_phase192_name] = __builtins__",
            "globals()[_phase192_name] = _phase192_section",
        ):
            with self.subTest(replacement=replacement), self.assertRaises(ValueError):
                compare_identity_surfaces._phase192_template_validate_initializer(
                    ast.parse(
                        source.replace(
                            "globals()[_phase192_name] = getattr(_phase192_section, _phase192_name)",
                            replacement,
                            1,
                        ),
                        str(initializer),
                    ),
                    str(initializer),
                )

    def test_reviewed_validator_split_rejects_initializer_rebindings(self) -> None:
        """The reviewed validator split cannot waive arbitrary facade rebindings."""
        relative = "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py"
        initializer = Path(
            "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package/__init__.py"
        )
        source = initializer.read_text(encoding="utf-8")
        base_surface = {
            relative: (
                frozenset({"main", "run_checks"}),
                ("implementation_lyrical",),
                "from . import implementation_lyrical\n",
                frozenset(),
            )
        }
        mutations = (
            ("public", source + "\nmain = lambda: 0\n"),
            (
                "section_export",
                source + "\n_phase192_section_0.run_checks = lambda *args, **kwargs: []\n",
            ),
            ("section_public", source + "\n_phase192_section_0.main = None\n"),
            ("export_names", source + "\n_PHASE192_EXPORT_NAMES = set()\n"),
            ("section_modules", source + "\n_PHASE192_SECTION_MODULES = ()\n"),
            ("section_binding", source + "\n_phase192_section_0 = None\n"),
            (
                "configuration_rhs",
                source + '\n_phase192_section_0.ROOT = Path("/tmp/untrusted")\n',
            ),
            (
                "module_class_rhs",
                source + "\n_phase192_module.__class__ = _phase192_types.ModuleType\n",
            ),
            (
                "section_sync_rhs",
                source.replace(
                    "_phase192_section.__dict__[_phase192_name] = globals()[_phase192_name]",
                    "_phase192_section.__dict__[_phase192_name] = None",
                    1,
                ),
            ),
        )
        for mutation, mutated_source in mutations:
            with self.subTest(mutation=mutation):
                head_surface = {
                    relative: (
                        frozenset({"main", "run_checks"}),
                        ("implementation_lyrical", "manifest_and_inventory", "runtime_checks"),
                        mutated_source,
                        frozenset(),
                    )
                }
                errors = compare_identity_surfaces._compare_surfaces(
                    base_surface,
                    head_surface,
                    strict=False,
                )
                self.assertIn("PACKAGE_INITIALIZER_CHANGED " + relative, errors)


__all__ = ["LoaderMutationTests"]
