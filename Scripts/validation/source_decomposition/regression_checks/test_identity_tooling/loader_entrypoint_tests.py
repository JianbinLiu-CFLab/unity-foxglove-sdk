from .foundation import *


class LoaderEntrypointTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_identity_cli_defaults_to_strict_mode(self) -> None:
        """The command-line default remains the exact decomposition audit."""
        with mock.patch.object(compare_identity_surfaces, "compare_revisions", return_value=0) as compare:
            self.assertEqual(0, compare_identity_surfaces.main(["--base", "a", "--head", "b"]))
        self.assertEqual(True, compare.call_args.kwargs["strict"])

    def test_identity_cli_compatibility_mode_is_explicit(self) -> None:
        """The compatibility gate is selected only by its explicit flag."""
        with mock.patch.object(compare_identity_surfaces, "compare_revisions", return_value=0) as compare:
            self.assertEqual(0, compare_identity_surfaces.main(["--base", "a", "--head", "b", "--compatibility"]))
        self.assertEqual(False, compare.call_args.kwargs["strict"])

    def test_identity_cli_rejects_conflicting_modes(self) -> None:
        """Strict and compatibility modes cannot be combined."""
        with self.assertRaises(SystemExit) as error:
            compare_identity_surfaces.main(["--base", "a", "--head", "b", "--strict", "--compatibility"])
        self.assertEqual(2, error.exception.code)

    def test_split_identity_surface_rejects_facade_section_owner_collision(self) -> None:
        """A facade binding cannot shadow an existing section export in compatibility mode."""
        base = {
            "fixture.py": (
                frozenset({"X"}), ("a",), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})),), frozenset(), (), None, (),
                (("X", ("section:a",)),),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X"}), ("a",), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})),), frozenset(), (), None, (),
                (("X", ("facade", "section:a")),),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("SYMBOL_OWNER_CHANGED fixture.py: X", errors)

    def test_split_identity_surface_strict_mode_detects_section_owner_swap(self) -> None:
        """Strict decomposition mode also compares section ownership."""
        base = {
            "fixture.py": (
                frozenset({"X"}), ("a", "b"), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset({"X"})), ("b", frozenset())), frozenset(), (), None, (),
                (("X", ("section:a",)),),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X"}), ("a", "b"), (), frozenset({"X"}), None, frozenset(), False,
                (("a", frozenset()), ("b", frozenset({"X"}))), frozenset(), (), None, (),
                (("X", ("section:b",)),),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("SYMBOL_OWNER_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_section_export_owner_swap(self) -> None:
        """Moving an existing export between sections changes facade resolution."""
        base = {
            "fixture.py": (
                frozenset({"X", "Y"}),
                ("a", "b"),
                (),
                frozenset({"X", "Y"}),
                None,
                frozenset(),
                True,
                (("a", frozenset({"X"})), ("b", frozenset({"Y"}))),
                frozenset(),
                (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"X", "Y"}),
                ("a", "b"),
                (),
                frozenset({"X", "Y"}),
                None,
                frozenset(),
                True,
                (("a", frozenset({"Y"})), ("b", frozenset({"X"}))),
                frozenset(),
                (),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("SECTION_EXPORT_OWNER_CHANGED fixture.py: X,Y", errors)

    def test_conditional_contract_includes_body_and_duplicate_events(self) -> None:
        """Conditional contracts distinguish body changes and repeated writes."""
        first = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\nif FLAG:\n    _helper = 2\n")
        )
        changed = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\nif FLAG:\n    _helper = 3\n")
        )
        shortened = compare_identity_surfaces._conditional_contract_signatures(
            compare_identity_surfaces.ast.parse("if FLAG:\n    _helper = 1\n")
        )
        self.assertEqual(2, len(first))
        self.assertNotEqual(first, changed)
        self.assertNotEqual(first, shortened)

    def test_split_identity_surface_rejects_conditional_contract_change(self) -> None:
        """Changing an import-time condition is a compatibility break even if names match."""
        base = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("base-condition",),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("head-condition",),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("CONDITIONAL_CONTRACT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_loader_metadata_risk_delta(self) -> None:
        """Loader metadata restore changes remain subject to the namespace contract."""
        base = {
            "Scripts/package/validate_source_generator_dll.py": (
                frozenset({"PUBLIC"}), (), (), frozenset({"PUBLIC"}), None,
                frozenset(), True, (), frozenset(), (), None, (), (), (), frozenset({"PUBLIC"}),
            )
        }
        name_restore = "Call(func=Name(id='setattr', ctx=Load()), args=[Name(id='_phase192_canonical_self', ctx=Load()), Constant(value='__name__'), Name(id='_phase192_previous_name', ctx=Load())])"
        package_restore = "Call(func=Name(id='setattr', ctx=Load()), args=[Name(id='_phase192_canonical_self', ctx=Load()), Constant(value='__package__'), Name(id='_phase192_previous_package', ctx=Load())])"
        head = dict(base)
        head["Scripts/package/validate_source_generator_dll.py"] = (
            frozenset({"PUBLIC"}), (), (), frozenset({"PUBLIC"}), None,
            frozenset(), True, (), frozenset({name_restore, package_restore}), (), None, (), (), (), frozenset({"PUBLIC"}),
        )
        self.assertIn(
            "NAMESPACE_CONTRACT_CHANGED Scripts/package/validate_source_generator_dll.py",
            compare_identity_surfaces._compare_surfaces(base, head, strict=True),
        )

    def test_loader_compatibility_rejects_added_conditional_contract(self) -> None:
        """Loader conditional contracts remain exact in compatibility mode."""
        base = {
            "Scripts/package/validate_source_generator_dll.py": (
                frozenset({"PUBLIC"}), (), (), frozenset({"PUBLIC"}), None,
                frozenset(), True, (), frozenset(), ("facade:base",), None, (), (), (), frozenset({"PUBLIC"}),
            )
        }
        head = {
            "Scripts/package/validate_source_generator_dll.py": (
                frozenset({"PUBLIC"}), (), (), frozenset({"PUBLIC"}), None,
                frozenset(), True, (), frozenset(), ("facade:base", "facade:added"), None, (), (), (), frozenset({"PUBLIC"}),
            )
        }
        self.assertIn(
            "CONDITIONAL_CONTRACT_CHANGED Scripts/package/validate_source_generator_dll.py",
            compare_identity_surfaces._compare_surfaces(base, head, strict=False),
        )

    def test_loader_registry_exception_rejects_unrelated_dynamic_keys(self) -> None:
        """Dynamic registry mutations are accepted only in the validated loader transaction."""
        source = ast.parse(
            "import sys as _phase192_sys\n"
            "_phase192_name = 'unrelated'\n"
            "_phase192_previous_submodules = {}\n"
            "_phase192_sys.modules[_phase192_name] = "
            "_phase192_previous_submodules[_phase192_name]\n"
            "_phase192_sys.modules.pop(_phase192_name, None)\n"
        )
        with self.assertRaisesRegex(ValueError, "Unsupported generated module registry mutation"):
            compare_identity_surfaces._phase192_validate_template_registry_mutations(
                source, "Scripts/package/validate_source_generator_dll.py"
            )

    def test_loader_registry_transaction_rejects_control_flow_and_rebinding_variants(self) -> None:
        """The loader exception rejects guard escapes, rebinding, and path spoofing."""
        root = compare_identity_surfaces.REPOSITORY_ROOT
        facade = root / "Scripts/package/validate_source_generator_dll.py"
        source = facade.read_text(encoding="utf-8")
        variants = {
            "pop_outside_guard": source.replace(
                "            _phase192_sys.modules.pop(_phase192_name, None)\n",
                "            pass\n        _phase192_sys.modules.pop(_phase192_name, None)\n",
                1,
            ),
            "cleanup_key_rebind": source.replace(
                "            _phase192_sys.modules.pop(_phase192_name, None)\n",
                '            _phase192_name = "os"\n            _phase192_sys.modules.pop(_phase192_name, None)\n',
                1,
            ),
            "restore_key_rebind": source.replace(
                "        _phase192_sys.modules[_phase192_name] = _phase192_previous_submodules[_phase192_name]\n",
                '        _phase192_name = "os"\n        _phase192_sys.modules[_phase192_name] = _phase192_previous_submodules[_phase192_name]\n',
                1,
            ),
            "cleanup_loop_input_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_name = "os"\n    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "self_binding_loop_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    for _phase192_self in (None,):\n        pass\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "package_module_binding_loop_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    for _phase192_package_module in (None,):\n        pass\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "module_binding_loop_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    for _phase192_module in (None,):\n        pass\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "restore_loop_input_rebind": source.replace(
                "    for _phase192_name in _phase192_previous_submodules:\n",
                '    _phase192_name = "os"\n    for _phase192_name in _phase192_previous_submodules:\n',
                1,
            ),
            "snapshot_item_mutation": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_previous_submodules["os"] = None\n    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "snapshot_method_mutation": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_previous_submodules.pop("os", None)\n    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "canonical_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_canonical_self = "evil"\n    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "sys_alias_rebind": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_sys = "evil"\n    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "metadata_target_forgery": source.replace(
                '        setattr(_phase192_canonical_self, "__name__", _phase192_previous_name)\n',
                '        setattr(_phase192_canonical_self if False else "evil", "__name__", _phase192_previous_name)\n',
                1,
            ),
            "facade_class_rebind": source + '\n_Phase192FacadeModule = _phase192_types.ModuleType\n',
            "module_types_alias": source.replace(
                "import types as _phase192_types",
                "import sys as _phase192_types",
                1,
            ),
            "module_name_after_guard": source.replace(
                "    _phase192_module.__class__ = _Phase192FacadeModule\n\n",
                "    _phase192_module.__class__ = _Phase192FacadeModule\n\n_phase192_module.__name__ = \"evil\"\n",
                1,
            ),
            "metadata_extra_setattr": source.replace(
                '        setattr(_phase192_canonical_self, "__package__", _phase192_previous_package)\n',
                '        setattr(_phase192_canonical_self, "__package__", _phase192_previous_package)\n    setattr(_phase192_canonical_self, "__name__", "evil")\n',
                1,
            ),
            "metadata_extra_delattr": source.replace(
                '        setattr(_phase192_canonical_self, "__package__", _phase192_previous_package)\n',
                '        setattr(_phase192_canonical_self, "__package__", _phase192_previous_package)\n    delattr(_phase192_canonical_self, "__name__")\n',
                1,
            ),
            "annotated_package_rebind": source.replace(
                "try:\n    if _phase192_canonical_self is not None:\n",
                'try:\n    __package__: str = "evil"\n    if _phase192_canonical_self is not None:\n',
                1,
            ),
            "canonical_name_duplicate": source.replace(
                '        _phase192_canonical_self.__name__ = "validate_source_generator_dll"\n',
                '        _phase192_canonical_self.__name__ = "validate_source_generator_dll"\n'
                '        _phase192_canonical_self.__name__ = "validate_source_generator_dll"\n',
                1,
            ),
            "canonical_package_duplicate": source.replace(
                '        _phase192_canonical_self.__package__ = ""\n',
                '        _phase192_canonical_self.__package__ = ""\n'
                '        _phase192_canonical_self.__package__ = ""\n',
                1,
            ),
            "canonical_name_nested": source.replace(
                '        _phase192_canonical_self.__name__ = "validate_source_generator_dll"\n',
                '        if True:\n'
                '            _phase192_canonical_self.__name__ = "validate_source_generator_dll"\n',
                1,
            ),
            "helper_attribute_store": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_package_module.evil = "evil"\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "helper_dunder_call": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_package_module.__setattr__("evil", "evil")\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "types_attribute_store": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_types.ModuleType = object\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "sys_path_store": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_sys.path = []\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "previous_path_store": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_previous_path[0] = "evil"\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "facade_class_attribute_store": source + '\n_Phase192FacadeModule.__bases__ = (object,)\n',
            "main_attribute_store": source + '\nmain.__name__ = "evil"\n',
            "canonical_metadata_outside_transaction": source
            + '\nsetattr(_phase192_canonical_self, "__name__", "evil")\n',
            "nested_helper_call": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    setattr(getattr(_phase192_package_module, "x"), "y", "evil")\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "conditional_helper_call": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    setattr(_phase192_types if True else evil, "x", "evil")\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "wrapped_previous_path_store": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    [_phase192_previous_path][0][0] = "evil"\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "trusted_alias_assignment": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                '    _phase192_main = getattr(_phase192_package_module, "__setattr__")\n'
                '    for _phase192_name in tuple(_phase192_sys.modules):\n',
                1,
            ),
            "trusted_alias_import": source.replace(
                "from __future__ import annotations\n",
                "from __future__ import annotations\nimport sys as _phase192_main\n",
                1,
            ),
            "path_constructor_rebind": source + '\n_Phase192Path = evil\n',
            "path_constructor_duplicate_import": source.replace(
                "        from pathlib import Path as _Phase192Path\n",
                "        from pathlib import Path as _Phase192Path\n"
                "        from pathlib import Path as _Phase192Path\n",
                1,
            ),
            "system_exit_rebind": source + '\nSystemExit = evil\n',
            "spec_rebind": source + '\n__spec__ = evil\n',
            "future_rebind": source + '\n__future__ = evil\n',
            "loader_metadata_rebind": source + '\n__loader__ = evil\n',
            "cached_metadata_rebind": source + '\n__cached__ = evil\n',
            "file_metadata_rebind": source + '\n__file__ = evil\n',
            "doc_metadata_rebind": source + '\n__doc__ = evil\n',
            "all_metadata_rebind": source + '\n__all__ = evil\n',
            "final_name_binding": source.replace(
                "__name__ = __spec__.name if __spec__ is not None else __name__",
                '__name__ = "evil"',
                1,
            ),
            "final_name_duplicate": source + '\n__name__ = "evil"\n',
            "final_package_binding": source.replace(
                "__package__ = __spec__.parent if __spec__ is not None else __package__",
                '__package__ = "evil"',
                1,
            ),
            "final_package_duplicate": source + '\n__package__ = "evil"\n',
            "metadata_restore_guard_false": source.replace(
                "    if _phase192_canonical_self is not None:\n        setattr(_phase192_canonical_self, \"__name__\", _phase192_previous_name)\n",
                "    if False:\n        setattr(_phase192_canonical_self, \"__name__\", _phase192_previous_name)\n",
                1,
            ),
            "module_class_binding": source.replace(
                "    _phase192_module.__class__ = _Phase192FacadeModule\n",
                "    _phase192_module.__class__ = object\n",
                1,
            ),
            "module_class_duplicate": source.replace(
                "    _phase192_module.__class__ = _Phase192FacadeModule\n",
                "    _phase192_module.__class__ = _Phase192FacadeModule\n    _phase192_module.__class__ = _Phase192FacadeModule\n",
                1,
            ),
            "module_class_guard_false": source.replace(
                "if _phase192_module is not None:\n    _phase192_module.__class__ = _Phase192FacadeModule\n",
                "if False:\n    _phase192_module.__class__ = _Phase192FacadeModule\n",
                1,
            ),
            "module_class_guard_true": source.replace(
                "if _phase192_module is not None:\n    _phase192_module.__class__ = _Phase192FacadeModule\n",
                "if True:\n    _phase192_module.__class__ = _Phase192FacadeModule\n",
                1,
            ),
            "cli_dispatch_guard": source.replace(
                '    raise SystemExit(main())\n',
                "    pass\n",
                1,
            ),
            "cli_guard_pre_exit": source.replace(
                "from __future__ import annotations\n",
                "from __future__ import annotations\nif __name__ == '__main__':\n    raise SystemExit(0)\n",
                1,
            ),
            "cli_guard_pre_main": source.replace(
                "from __future__ import annotations\n",
                "from __future__ import annotations\nif __name__ == '__main__':\n    main()\n",
                1,
            ),
            "cli_guard_post_exit": source + "\nif __name__ == '__main__':\n    raise SystemExit(0)\n",
            "registry_augassign": source + '\n_phase192_sys.modules["os"] |= {}\n',
            "registry_annassign": source + '\n_phase192_sys.modules["os"]: object = None\n',
            "except_binding": source.replace(
                "try:\n    if _phase192_canonical_self is not None:\n",
                "try:\n    try:\n        pass\n    except Exception as _phase192_previous_submodules:\n        pass\n    if _phase192_canonical_self is not None:\n",
                1,
            ),
            "with_binding": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    with _phase192_previous_path as _phase192_previous_submodules:\n        pass\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "for_binding": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    for _phase192_previous_submodules in ():\n        pass\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "import_alias_binding": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    import sys as _phase192_previous_submodules\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "import_from_alias_binding": source.replace(
                "    for _phase192_name in tuple(_phase192_sys.modules):\n",
                "    from sys import modules as _phase192_name\n    for _phase192_name in tuple(_phase192_sys.modules):\n",
                1,
            ),
            "snapshot_rebind": source + '\n_phase192_previous_submodules = {"os": None}\n',
        }
        reordered_tree = ast.parse(source)
        reordered_indexes = {
            target.id: index
            for index, statement in enumerate(reordered_tree.body)
            if isinstance(statement, ast.Assign)
            for target in statement.targets
            if isinstance(target, ast.Name)
            and target.id in {
                "_phase192_canonical_self",
                "_phase192_previous_path",
            }
        }
        first_index = reordered_indexes["_phase192_canonical_self"]
        second_index = reordered_indexes["_phase192_previous_path"]
        reordered_tree.body[first_index], reordered_tree.body[second_index] = (
            reordered_tree.body[second_index],
            reordered_tree.body[first_index],
        )
        variants["state_initialization_reorder"] = ast.unparse(reordered_tree)
        for name, variant in variants.items():
            with self.subTest(name=name):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._symbols(
                        variant,
                        str(facade),
                        repository_root=root,
                    )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._phase192_validate_template_registry_mutations(
                ast.parse(source),
                str(root / "evil/Scripts/package/validate_source_generator_dll.py"),
                root,
            )

    def test_split_identity_surface_allows_new_conditional_names_but_not_existing_bindings(self) -> None:
        """New inert conditional names are additive; existing bindings retain their contract."""
        base = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE", "_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base",), None, (),
            )
        }
        additive = {
            "fixture.py": (
                frozenset({"LIVE", "_helper", "NEW"}), ("live",), (), frozenset({"LIVE", "_helper", "NEW"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base", "NEW:new"), None, (),
            )
        }
        self.assertEqual([], compare_identity_surfaces._compare_surfaces(base, additive, strict=False))
        changed = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE", "_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:base", "_helper:other"), None, (),
            )
        }
        self.assertIn(
            "CONDITIONAL_CONTRACT_CHANGED fixture.py",
            compare_identity_surfaces._compare_surfaces(base, changed, strict=False),
        )

    def test_split_identity_surface_rejects_scoped_conditional_binding_change(self) -> None:
        """Scoped section contracts must not hide a changed existing binding."""
        base = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(),
                ("section:live:_helper:base",), None, (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"LIVE", "_helper"}), ("live",), (), frozenset({"LIVE"}), None,
                frozenset({"_helper"}), True, (), frozenset(),
                ("section:live:_helper:changed",), None, (),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("CONDITIONAL_CONTRACT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_conditional_contract_reorder(self) -> None:
        """Reordering conditional writes can change the final runtime binding."""
        base = {
            "fixture.py": (
                frozenset({"_helper"}), (), (), frozenset({"_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:first", "_helper:second"), None, (),
            )
        }
        head = {
            "fixture.py": (
                frozenset({"_helper"}), (), (), frozenset({"_helper"}), None,
                frozenset({"_helper"}), True, (), frozenset(), ("_helper:second", "_helper:first"), None, (),
            )
        }
        self.assertIn(
            "CONDITIONAL_CONTRACT_CHANGED fixture.py",
            compare_identity_surfaces._compare_surfaces(base, head, strict=False),
        )

    def test_conditional_contract_preserves_branch_and_handler_boundaries(self) -> None:
        """Control-flow branch/handler rearrangements change the contract."""
        base = ast.parse(
            "if flag:\n    _a = 1\nelse:\n    _b = 2\n"
            "try:\n    _c = 3\nexcept ValueError:\n    pass\nelse:\n    _d = 4\n"
        )
        head = ast.parse(
            "if flag:\n    _a = 1\n    _b = 2\n"
            "try:\n    _c = 3\n    _d = 4\nexcept ValueError:\n    pass\n"
        )
        self.assertNotEqual(
            compare_identity_surfaces._conditional_contract_signatures(base),
            compare_identity_surfaces._conditional_contract_signatures(head),
        )

    def test_split_identity_surface_rejects_module_entrypoint_replacement(self) -> None:
        """An existing -m wrapper cannot be replaced while retaining a relative import."""
        base = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(), (), None, ("base-import",)
            )
        }
        head = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(), (), None, ("head-import",)
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MODULE_ENTRYPOINT_CHANGED fixture.py", errors)

    def test_split_identity_surface_rejects_entrypoint_call_addition(self) -> None:
        """An existing module entrypoint cannot gain an unreviewed executable call."""
        base = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(),
                (), "base-digest", ("delegate-call",),
            )
        }
        head = {
            "fixture.py": (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (), frozenset(),
                (), "head-digest", ("delegate-call", "evil-call"),
            )
        }
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=False)
        self.assertIn("MODULE_ENTRYPOINT_CHANGED fixture.py", errors)

    def test_entrypoint_rejects_wildcard_side_effect(self) -> None:
        """A wildcard protocol wrapper cannot execute unrelated import-time code."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "from . import *\nimport os\nos.system('bad')\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_unlisted_orphan_relative_module(self) -> None:
        """An executable entrypoint cannot import an unregistered orphan section."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("from . import core\n", encoding="utf-8")
            (package / "core.py").write_text("def main(): pass\n__all__ = ['main']\n", encoding="utf-8")
            (package / "evil.py").write_text("def run(): pass\n__all__ = ['run']\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "from .evil import run\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(run())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path, frozenset({"core"}))

    def test_entrypoint_rejects_missing_relative_module(self) -> None:
        """A -m wrapper cannot delegate to a module absent from its package."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            path = package / "__main__.py"
            path.write_text(
                "from .missing import main\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_missing_package_alias(self) -> None:
        """A module-level package import must name an exported symbol."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "from . import missing\nif __name__ == '__main__':\n    raise SystemExit(missing())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_misplaced_future_import(self) -> None:
        """Entrypoint syntax must be valid for Python, not only ast.parse."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "__main__.py"
            path.write_text(
                "value = 1\nfrom __future__ import annotations\nfrom . import main\n",
                encoding="utf-8",
            )
            with self.assertRaises(SyntaxError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_hidden_guard_import_and_unsafe_control_flow(self) -> None:
        """A module entrypoint guard may only dispatch; it cannot hide imports or loops."""
        sources = (
            "from . import main\nif __name__ == '__main__':\n    from . import missing\n    raise SystemExit(main())\n",
            "from . import main\nif __name__ == '__main__':\n    while True:\n        pass\n    raise SystemExit(main())\n",
            "from . import main\nif __name__ is '__main__':\n    raise SystemExit(main())\n",
        )
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temp:
                package = Path(temp)
                (package / "__init__.py").write_text("def main(): pass\n", encoding="utf-8")
                path = package / "__main__.py"
                path.write_text(source, encoding="utf-8")
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_unresolvable_absolute_import(self) -> None:
        """Allowlisted absolute imports must name real modules or members."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("def main(): pass\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "from argparse import NO_SUCH\n"
                "from . import main\n"
                "if __name__ == '__main__':\n    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_conditional_relative_target(self) -> None:
        """Entrypoints cannot import a target that is only conditionally bound."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("from . import core\n", encoding="utf-8")
            (package / "core.py").write_text(
                "if False:\n    def run(): pass\n__all__ = ['run']\n",
                encoding="utf-8",
            )
            path = package / "__main__.py"
            path.write_text(
                "from .core import run\n"
                "if __name__ == '__main__':\n    raise SystemExit(run())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_unlisted_package_alias(self) -> None:
        """A package-level alias for an orphan file is not an executable surface."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("from . import core\n", encoding="utf-8")
            (package / "core.py").write_text("def main(): pass\n__all__ = ['main']\n", encoding="utf-8")
            (package / "evil.py").write_text("def run(): pass\n__all__ = ['run']\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "from . import evil\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(evil.run())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path, frozenset({"core"}))

    def test_entrypoint_rejects_parent_relative_import(self) -> None:
        """Entrypoints cannot escape their package through parent-relative imports."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "child"
            package.mkdir()
            (package / "__init__.py").write_text("def main(): pass\n", encoding="utf-8")
            path = package / "__main__.py"
            path.write_text(
                "from . import main\n"
                "from ..missing import nope\n"
                "if __name__ == '__main__':\n    raise SystemExit(main())\n",
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_rejects_unsafe_local_function_body(self) -> None:
        """A locally delegated wrapper cannot hide imports, process execution, or loops."""
        bodies = (
            "    __import__('os').system('bad')\n",
            "    open('bad', 'w')\n",
            "    obj.write('bad')\n",
            "    obj.unlink()\n",
            "    while True:\n        pass\n",
        )
        for body in bodies:
            with self.subTest(body=body), tempfile.TemporaryDirectory() as temp:
                package = Path(temp)
                (package / "__init__.py").write_text("def main(): pass\n", encoding="utf-8")
                path = package / "__main__.py"
                path.write_text(
                    "from . import main\n"
                    "def evil():\n" + body +
                    "if __name__ == '__main__':\n    raise SystemExit(evil())\n",
                    encoding="utf-8",
                )
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_compatibility_allows_new_conditional_section_names(self) -> None:
        """Compatibility scanning permits additive conditional exports without freezing them."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text("from . import core\n", encoding="utf-8")
            (package / "core.py").write_text(
                "PUBLIC = 1\nif False:\n    NEW = 2\n"
                "__all__ = [name for name in globals() if not name.startswith('__')]\n",
                encoding="utf-8",
            )
            path = package / "__main__.py"
            path.write_text("from . import *\n", encoding="utf-8")
            self.assertTrue(
                compare_identity_surfaces._entrypoint_contract(
                    path,
                    frozenset({"core"}),
                    reject_conditional=False,
                )[0]
            )

    def test_entrypoint_rejects_unapproved_absolute_imports(self) -> None:
        """Executable wrappers only import the small approved CLI/runtime roots."""
        for module in ("os", "pathlib", "subprocess"):
            with self.subTest(module=module), tempfile.TemporaryDirectory() as temp:
                package = Path(temp)
                (package / "__init__.py").write_text("def main(): pass\n", encoding="utf-8")
                path = package / "__main__.py"
                path.write_text(
                    f"import {module}\nfrom . import main\n"
                    "if __name__ == '__main__':\n    raise SystemExit(main())\n",
                    encoding="utf-8",
                )
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(path)

    def test_entrypoint_accepts_legacy_timeout_tuple_guard(self) -> None:
        """The pre-split timeout guard remains a supported entrypoint contract."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            (package / "__init__.py").write_text(
                "def main(argv=None): pass\n", encoding="utf-8"
            )
            path = package / "__main__.py"
            path.write_text(
                "from __future__ import annotations\n"
                "import subprocess\n"
                "import sys\n"
                "from . import main\n"
                "try:\n"
                "    raise SystemExit(main(sys.argv[1:]))\n"
                "except (RuntimeError, TimeoutError, FileNotFoundError, subprocess.TimeoutExpired) as exc:\n"
                "    raise SystemExit(1) from exc\n",
                encoding="utf-8",
            )
            self.assertTrue(compare_identity_surfaces._entrypoint_contract(path)[0])

    def test_entrypoint_accepts_legacy_bare_dispatch_guards(self) -> None:
        """The pre-split bare dispatch forms remain valid for -m wrappers."""
        for dispatch in ("raise SystemExit(main(sys.argv[1:]))", "raise SystemExit(main())"):
            with self.subTest(dispatch=dispatch), tempfile.TemporaryDirectory() as temp:
                package = Path(temp)
                (package / "__init__.py").write_text(
                    "def main(argv=None): pass\n", encoding="utf-8"
                )
                path = package / "__main__.py"
                path.write_text(
                    "import sys\nfrom . import main\nif __name__ == '__main__':\n    "
                    + dispatch
                    + "\n",
                    encoding="utf-8",
                )
                self.assertTrue(compare_identity_surfaces._entrypoint_contract(path)[0])

    def test_entrypoint_rejects_unknown_import_and_definition_side_effects(self) -> None:
        """Entrypoints reject unknown imports and import-time definition effects."""
        sources = (
            "from . import main\nimport evil\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
            "from . import main\ndef helper(value=evil()):\n    return value\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
            "from . import main\nclass Helper(metaclass=evil()):\n    pass\nif __name__ == '__main__':\n    raise SystemExit(main())\n",
        )
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temp:
                path = Path(temp) / "__main__.py"
                path.write_text(source, encoding="utf-8")
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._entrypoint_contract(path)

    def test_split_identity_surface_rejects_added_module_entrypoint_in_strict_mode(self) -> None:
        """Strict decomposition identity does not allow a new executable entrypoint."""
        base = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), False)}
        head = {"fixture.py": (frozenset(), (), (), frozenset(), None, frozenset(), True)}
        errors = compare_identity_surfaces._compare_surfaces(base, head, strict=True)
        self.assertIn("ADDED_MODULE_ENTRYPOINT fixture.py", errors)

    def test_split_identity_surface_rejects_indirect_module_registry_aliases(self) -> None:
        """Functions and object attributes cannot smuggle sys.modules into mutations."""
        sources = (
            "import sys\ndef registry():\n    return sys.modules\nregistry()[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
            "import sys\nclass Holder:\n    pass\nholder = Holder()\nholder.modules = sys.modules\nholder.modules[__name__].__setattr__('PUBLIC', 2)\nPUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
        )
        for source in sources:
            with self.subTest(source=source):
                with self.assertRaises(ValueError):
                    compare_identity_surfaces._section_exports(source, "fixture.py")

    def test_split_identity_surface_rejects_type_alias_bindings(self) -> None:
        """Unsupported Python 3.12 type aliases fail closed instead of hiding exports."""
        if not hasattr(ast, "TypeAlias"):
            self.skipTest("TypeAlias is unavailable on this Python version")
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "type PUBLIC = int\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
                reject_conditional=False,
            )

    def test_split_identity_surface_rejects_try_star_exports(self) -> None:
        """except* bindings are either tracked or rejected, never silently omitted."""
        if not hasattr(ast, "TryStar"):
            self.skipTest("TryStar is unavailable on this Python version")
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(
                "try:\n    pass\nexcept* Exception:\n    PUBLIC = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n",
                "fixture.py",
            )

    def test_split_identity_surface_rejects_private_module_delete_bypass(self) -> None:
        """Private deletes can alter the dynamic export set and must fail closed."""
        source = (
            "_helper = 1\n"
            "del (_helper,)\n"
            "__all__ = [name for name in globals() if not name.startswith('__')]\n"
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._section_exports(source, "fixture.py")


    def test_generated_facade_rejects_unapproved_module_registry_assignment(self) -> None:
        """Generated facades may only restore their own module alias."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + '\n_phase192_sys.modules["unexpected_module"] = None\n'
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_imported_module_registry_alias(self) -> None:
        """Generated facades may not mutate sys.modules through an imported alias."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + (
            '\nfrom sys import modules as _phase192_registry\n'
            '_phase192_registry.pop("unrelated_module", None)\n'
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_propagated_module_registry_alias(self) -> None:
        """Generated facades may not mutate sys.modules through a propagated alias."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + (
            '\n_phase192_registry = _phase192_sys.modules\n'
            '_phase192_registry.pop("unrelated_module", None)\n'
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_imported_private_helper_alias(self) -> None:
        """Generated facades may not disguise sys.exit as an approved helper."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + (
            '\nfrom sys import exit as _phase192_main\n'
            '_phase192_main(0)\n'
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_module_registry_attribute_mutation(self) -> None:
        """Generated facades may not mutate attributes through a registry subscript."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + (
            '\n_phase192_sys.modules["sys"].__setattr__("unexpected_attribute", True)\n'
        )
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_unapproved_phase192_call(self) -> None:
        """Generated facades may call only reviewed private helpers."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + '\n_phase192_stop(0)\n'
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_unrelated_module_registry_key(self) -> None:
        """Generated facades may not restore an unrelated module key."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + '\n_phase192_sys.modules["unexpected_module"] = _phase192_self\n'
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))

    def test_generated_facade_rejects_unrelated_module_registry_pop(self) -> None:
        """Generated facades may not remove an unrelated module key."""
        facade = Path("Scripts/mcap/mcap_time_sync.py")
        source = facade.read_text(encoding="utf-8") + '\n_phase192_sys.modules.pop("unexpected_module", None)\n'
        with self.assertRaises(ValueError):
            compare_identity_surfaces._symbols(source, str(facade))


__all__ = ["LoaderEntrypointTests"]
