from __future__ import annotations

from .foundation import *

def _surface(
    root: Path,
    facade: Path,
    section_modules=None,
    *,
    reject_conditional: bool = True,
) -> tuple:
    """Internal helper for _surface."""
    relative = facade.relative_to(root).as_posix()
    package = _section_package_for_facade(facade)
    if package is None:
        raise ValueError(f"Missing source section package for {facade}")
    section_modules = section_modules or _section_modules_for_root(root)
    sections = section_modules(package)
    section_exports: set[str] = set()
    section_export_map: list[tuple[str, frozenset[str]]] = []
    owner_map: dict[str, set[str]] = {}
    conditional_names: set[str] = set()
    risk_signatures: set[str] = set()
    conditional_contract: list[str] = []
    relative_star_contract: list[str] = []
    module_import_names: set[str] = set()
    legacy_test_section = "regression_checks" in facade.parts or facade.stem.startswith("test_")
    manifest_package = not (package / "__init__.py").is_file()
    reviewed_identity_tooling_split = _reviewed_identity_tooling_test_split(relative, sections)

    def add_conditional_contract(signature: str) -> None:
        """Internal helper for add_conditional_contract."""
        conditional_contract.append(signature)

    for section in sections:
        section_source = section.read_text(encoding="utf-8")
        section_tree = _parse_source(section_source, section)
        phase121_configuration = _is_phase121_configuration_path(section, root)
        module_import_names.update(_direct_import_bound_names(section_tree))
        relative_star_contract.extend(
            _relative_import_contract(
                section_tree,
                package,
                section,
                frozenset(path.stem for path in sections),
            )
        )
        nested_facade = _section_package_for_facade(section)
        if manifest_package or nested_facade is not None:
            exports = _symbols(
                section_source,
                str(section),
                reject_conditional=reject_conditional,
                repository_root=root,
            )
            if nested_facade is not None:
                declared = next(
                    (
                        statement.value
                        for statement in section_tree.body
                        if isinstance(statement, ast.Assign)
                        and any(
                            isinstance(target, ast.Name) and target.id == "__all__"
                            for target in statement.targets
                        )
                    ),
                    None,
                )
                if isinstance(declared, (ast.List, ast.Tuple)):
                    allowed = {
                        element.value
                        for element in declared.elts
                        if isinstance(element, ast.Constant)
                        and isinstance(element.value, str)
                    }
                    exports = frozenset(name for name in exports if name in allowed)
        else:
            exports = _section_exports(
                section_source,
                str(section),
                reject_conditional=reject_conditional,
                reject_module_registry=(not legacy_test_section and not phase121_configuration),
                validate_namespace=not phase121_configuration and not reviewed_identity_tooling_split,
                repository_root=root,
            )
        section_exports.update(exports)
        for name in exports:
            owner_map.setdefault(name, set()).add(f"section:{section.stem}")
        section_export_map.append((section.stem, frozenset(exports)))
        risk_signatures.update(_namespace_risk_signatures(section_tree))
        for signature in _conditional_contract_signatures(section_tree):
            add_conditional_contract(f"section:{section.stem}:{signature}")
        conditional_names.update(
            _conditional_only_public_names(
                section_tree,
                include_private=True,
            )
        )
    facade_source = facade.read_text(encoding="utf-8")
    facade_tree = _parse_source(facade_source, facade)
    module_import_names.update(_direct_import_bound_names(facade_tree))
    relative_star_contract.extend(
        _relative_import_contract(
            facade_tree,
            facade.parent,
            facade,
            frozenset(path.stem for path in sections),
        )
    )
    facade_symbols = (
        frozenset(
            name
            for name in _top_level_names(facade_tree, include_imports=True)
            if not name.startswith("__")
        )
        if manifest_package
        else _symbols(
            facade_source,
            str(facade),
            reject_conditional=reject_conditional,
            repository_root=root,
        )
    )
    exported = set(section_exports)
    exported.update(facade_symbols)
    for name in facade_symbols:
        owner_map.setdefault(name, set()).add("facade")
    risk_signatures.update(_namespace_risk_signatures(facade_tree))
    for signature in _conditional_contract_signatures(facade_tree):
        add_conditional_contract(f"facade:{signature}")
    conditional_names.update(
        _conditional_only_public_names(
            facade_tree,
            include_private=True,
        )
    )
    initializer = package / "__init__.py"
    if initializer.is_file():
        initializer_tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
        module_import_names.update(_direct_import_bound_names(initializer_tree))
        _relative_import_contract(
            initializer_tree,
            package,
            initializer,
            frozenset(path.stem for path in sections),
        )
    else:
        initializer_tree = ast.parse(
            "pass\n",
            filename=str(package / "sections.manifest"),
        )
    entrypoint_present, entrypoint_digest, entrypoint_contract, entrypoint_risks = _entrypoint_contract(
        package / "__main__.py",
        frozenset(path.stem for path in sections),
        reject_conditional=reject_conditional,
        repository_root=root,
    )
    risk_signatures.update(entrypoint_risks)
    _reject_unsupported_module_bindings(initializer_tree, str(initializer))
    risk_signatures.update(_namespace_risk_signatures(initializer_tree))
    for signature in _conditional_contract_signatures(initializer_tree):
        add_conditional_contract(f"initializer:{signature}")
    initializer_names = {
        name for name in _top_level_names(initializer_tree, include_imports=True)
        if not name.startswith("_")
    }
    exported.update(initializer_names)
    for name in initializer_names:
        owner_map.setdefault(name, set()).add("initializer")
    owner_map_tuple = tuple(
        (name, tuple(sorted(owners)))
        for name, owners in sorted(owner_map.items())
    )
    public_symbols = frozenset(
        name
        for name in exported
        if not name.startswith("_") and name not in module_import_names
    )
    nested_contract: list[tuple[str, tuple[str, ...], tuple[str, ...], tuple[str, ...]]] = []
    for nested in sorted(package.rglob("*.py")):
        if nested.parent == package or nested.name in {"__init__.py", "__main__.py"}:
            continue
        nested_tree = _parse_source(nested.read_text(encoding="utf-8"), nested)
        nested_contract.append(
            (
                nested.relative_to(package).as_posix(),
                tuple(sorted(_top_level_names(nested_tree, include_imports=False))),
                tuple(sorted(_direct_import_bound_names(nested_tree))),
                tuple(sorted(_namespace_risk_signatures(nested_tree))),
            )
        )
    return (
        frozenset(exported),
        tuple(path.stem for path in sections),
        _initializer_lines(initializer)
        if initializer.is_file()
        else tuple(
            line.strip()
            for line in (package / "sections.manifest")
            .read_text(encoding="utf-8")
            .splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        ),
        frozenset(section_exports),
        _initializer_digest(initializer)
        if initializer.is_file()
        else hashlib.sha256(
            (package / "sections.manifest").read_bytes()
        ).hexdigest(),
        frozenset(conditional_names),
        entrypoint_present,
        tuple(section_export_map),
        frozenset(risk_signatures),
        tuple(conditional_contract),
        entrypoint_digest,
        entrypoint_contract,
        owner_map_tuple,
        tuple(relative_star_contract),
        public_symbols,
        tuple(nested_contract),
    )

def _surfaces(
    root: Path,
    *,
    strict: bool = True,
) -> dict[str, tuple]:
    """Internal helper for _surfaces."""
    result: dict[str, tuple] = {}
    section_modules = _section_modules_for_root(root)
    for facade in _split_facades(root):
        relative = facade.relative_to(root).as_posix()
        package = _section_package_for_facade(facade)
        if package is None:
            raise ValueError(f"Missing source section package for {facade}")
        listed_paths = section_modules(package)
        listed = {path.name for path in listed_paths}
        actual_paths = [
            path for path in package.glob("*.py")
            if path.name not in {"__init__.py", "__main__.py"}
        ]
        actual_by_casefold: dict[str, Path] = {}
        for path in actual_paths:
            key = path.stem.casefold()
            if key in actual_by_casefold:
                raise ValueError(
                    f"Duplicate source section file (case-insensitive): {actual_by_casefold[key]} / {path}"
                )
            actual_by_casefold[key] = path
        actual = {path.name for path in actual_paths}
        missing = sorted(actual - listed)
        extra = sorted(listed - actual)
        if extra or (strict and missing):
            raise ValueError(
                f"{relative}: exported sections differ from package files; "
                f"missing={', '.join(missing) or '-'} extra={', '.join(extra) or '-'}"
            )
        entrypoint = package / "__main__.py"
        if entrypoint.is_file():
            entrypoint_tree = _parse_source(entrypoint.read_text(encoding="utf-8"), entrypoint)
            if not any(
                isinstance(node, ast.ImportFrom) and node.level == 1
                for node in ast.walk(entrypoint_tree)
            ) and not _is_unittest_module_entrypoint(entrypoint_tree):
                raise ValueError(f"{relative}: module entrypoint does not import its package")
        result[relative] = _surface(
            root,
            facade,
            section_modules,
            reject_conditional=strict,
        )
    return result

def _conditional_signature_names(signature: str) -> set[str]:
    """Extract bound names from a scoped conditional signature."""
    if signature.startswith("section:"):
        payload = signature.split(":", 2)[-1]
    elif signature.startswith(("facade:", "initializer:")):
        payload = signature.split(":", 1)[-1]
    else:
        payload = signature
    name_text = payload.rsplit(":", 1)[0]
    return {name for name in name_text.split(",") if name}

def _reviewed_validator_section_split(
    relative: str,
    base_sections: tuple[str, ...],
    head_sections: tuple[str, ...],
) -> bool:
    """Recognize the reviewed Lyrical validator implementation split."""
    return (
        relative == "Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py"
        and base_sections == ("implementation_lyrical",)
        and head_sections == ("implementation_lyrical", "manifest_and_inventory", "runtime_checks")
    )

def _reviewed_template_registry_split(
    relative: str,
    base_nested_contract: tuple,
    head_nested_contract: tuple,
    base_relative_star_contract: tuple[str, ...],
    head_relative_star_contract: tuple[str, ...],
) -> bool:
    """Recognize the reviewed template registry package split."""
    expected_paths = (
        "template_registry/closure_mutations.py",
        "template_registry/critical_bindings.py",
        "template_registry/registry_mutations.py",
        "template_registry/template_helpers.py",
    )
    head_paths = tuple(item[0] for item in head_nested_contract)
    relative_contract_matches = False
    if (
        head_relative_star_contract
        and head_relative_star_contract[0].startswith(
            "ImportFrom(module='template_registry',"
        )
        and len(head_relative_star_contract) == len(base_relative_star_contract)
    ):
        head_tail = head_relative_star_contract[1:]
        relative_contract_matches = head_tail == base_relative_star_contract[1:]
        if not relative_contract_matches and len(base_relative_star_contract) == len(head_tail) + 1:
            relative_contract_matches = any(
                base_relative_star_contract[:index] + base_relative_star_contract[index + 1:]
                == head_tail
                and base_relative_star_contract[index].startswith(
                    "ImportFrom(module='foundation',"
                )
                for index in range(len(base_relative_star_contract))
            )
    elif len(base_relative_star_contract) == len(head_relative_star_contract) + 1:
        relative_contract_matches = any(
            base_relative_star_contract[:index] + base_relative_star_contract[index + 1:]
            == head_relative_star_contract
            and base_relative_star_contract[index].startswith(
                "ImportFrom(module='foundation',"
            )
            for index in range(len(base_relative_star_contract))
        )
    return (
        _canonical_source_decomposition_relative(relative)
        == "Scripts/validation/source_decomposition/compare_identity_surfaces.py"
        and not base_nested_contract
        and set(head_paths) == set(expected_paths)
        and len(head_paths) == len(expected_paths)
        and len(head_relative_star_contract) >= 0
        and relative_contract_matches
    )


def _normalize_migrated_source_decomposition_surfaces(
    surfaces: dict[str, tuple],
) -> dict[str, tuple]:
    """Map the legacy Phase192 surface prefix to its validation location."""
    normalized = dict(surfaces)
    legacy_prefix = "Scripts/phase192/"
    current_prefix = "Scripts/validation/source_decomposition/"
    for relative, surface in list(surfaces.items()):
        if not relative.startswith(current_prefix):
            continue
        legacy = legacy_prefix + relative[len(current_prefix):]
        if legacy not in normalized:
            normalized[legacy] = surface
            normalized.pop(relative, None)
    return normalized


def _directory_migration_is_active(
    base_surfaces: Mapping[str, tuple],
    head_surfaces: Mapping[str, tuple],
    *,
    strict: bool,
) -> bool:
    """Return whether the compared revisions cross the legacy directory boundary."""
    return (
        not strict
        and any(path.startswith("Scripts/phase192/") for path in base_surfaces)
        and any(
            path.startswith("Scripts/validation/source_decomposition/")
            for path in head_surfaces
        )
    )


_REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE = (
    "Scripts/validation/source_decomposition/compare_identity_surfaces/template_registry.py"
)
_REVIEWED_TEMPLATE_REGISTRY_FACADE_DIGEST = (
    "a09566701f4369d7cc58bcaef4ae3270e61e7a8d1dd3913234afb900c4ef5fcf"
)


def _reviewed_template_registry_facade_transition(
    relative: str,
    source_digests: Mapping[str, str] | None,
) -> bool:
    """Recognize the exact reviewed template-registry facade loader transition."""
    return (
        source_digests is not None
        and source_digests.get(_REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE)
        == _REVIEWED_TEMPLATE_REGISTRY_FACADE_DIGEST
        and relative
        in {
            _REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE,
            "Scripts/validation/source_decomposition/compare_identity_surfaces.py",
        }
    )

_REVIEWED_VALIDATOR_CLEANUP_SIGNATURE = (
    "facade::7cdf800c9922e9368a642e72b34a53b8fa0049b842fda84b2ca8041537599915"
)
_IDENTITY_HELPER_RELATIVES = (
    "Scripts/validation/source_decomposition/identity_surface_conditions.py",
    "Scripts/validation/source_decomposition/identity_namespace_analysis.py",
    "Scripts/validation/source_decomposition/identity_namespace_scanner.py",
    "Scripts/validation/source_decomposition/identity_initializer_contracts.py",
)
_REVIEWED_NAMESPACE_SPLIT_HELPER_DIGESTS = {
    "Scripts/validation/source_decomposition/identity_namespace_analysis.py": "41685a4bd95a9c45e7979bf4501fb8d062a9b4fc404fcedf4ca071c1471ce947",
    "Scripts/validation/source_decomposition/identity_namespace_scanner.py": "392e527bcc853bea3620c390bdc2844cd8e0c07c0ec957f00560e8046c40ed36",
}


def _normalized_source_digest(path: Path) -> str:
    """Return a stable LF-normalized source digest."""
    normalized = path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    normalized = normalized.replace(
        b"Scripts.validation.source_decomposition",
        b"Scripts.phase192",
    )
    normalized = normalized.replace(
        b"Scripts/validation/source_decomposition",
        b"Scripts/phase192",
    )
    return hashlib.sha256(normalized).hexdigest()


def _identity_helper_source_contract(root: Path) -> tuple[tuple[str, str | None], ...]:
    """Return source digests for identity helpers used by the checker."""
    contract: list[tuple[str, str | None]] = []
    for relative in _IDENTITY_HELPER_RELATIVES:
        path = _resolve_source_decomposition_path(root, relative)
        if path.is_file():
            digest = _normalized_source_digest(path)
        else:
            digest = None
        contract.append((relative, digest))
    return tuple(contract)


def _compare_identity_helper_contracts(
    base_contract: tuple[tuple[str, str | None], ...],
    head_contract: tuple[tuple[str, str | None], ...],
    *,
    base_root: Path | None = None,
    head_root: Path | None = None,
) -> list[str]:
    """Report helper implementation changes independently of facade surfaces."""
    base_map = dict(base_contract)
    head_map = dict(head_contract)
    errors: list[str] = []
    reviewed_namespace_split = False
    if (
        base_root is not None
        and head_root is not None
        and not _resolve_source_decomposition_path(
            base_root,
            "Scripts/validation/source_decomposition/identity_namespace_scanner.py",
        ).is_file()
    ):
        reviewed_namespace_split = all(
            _resolve_source_decomposition_path(head_root, relative).is_file()
            and _normalized_source_digest(
                _resolve_source_decomposition_path(head_root, relative)
            )
            == digest
            for relative, digest in _REVIEWED_NAMESPACE_SPLIT_HELPER_DIGESTS.items()
        )
    for relative in _IDENTITY_HELPER_RELATIVES:
        if base_map.get(relative) != head_map.get(relative):
            if reviewed_namespace_split and relative in {
                "Scripts/validation/source_decomposition/identity_namespace_analysis.py",
                "Scripts/validation/source_decomposition/identity_namespace_scanner.py",
            }:
                continue
            errors.append(f"HELPER_SOURCE_CHANGED {relative}")
    return errors


def _reviewed_validator_isolation_transition(
    relative: str,
    base_contract: tuple[str, ...],
    head_contract: tuple[str, ...],
) -> bool:
    """Recognize the reviewed child-module cleanup added to legacy validators."""
    if relative not in {
        "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
        "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
    }:
        return False
    if relative.endswith("/humble/validate_ros2forunity_package.py"):
        return (
            len(head_contract) == len(base_contract) + 1
            and head_contract[0] == _REVIEWED_VALIDATOR_CLEANUP_SIGNATURE
            and head_contract[1:] == base_contract
        )
    return (
        len(head_contract) == len(base_contract) + 1
        and len(base_contract) > 0
        and head_contract[0] == base_contract[0]
        and head_contract[1] == _REVIEWED_VALIDATOR_CLEANUP_SIGNATURE
        and head_contract[2:] == base_contract[1:]
    )

def _compare_surfaces(
    base_surfaces,
    head_surfaces,
    *,
    strict: bool = True,
    waived_symbols: Mapping[str, frozenset[str]] | None = None,
    allow_directory_migration: bool = False,
    reviewed_source_digests: Mapping[str, str] | None = None,
) -> list[str]:
    """Compare identity surfaces using waivers from the trusted base revision."""
    errors: list[str] = []
    waiver_map = waived_symbols or {}
    unknown_waiver_paths = sorted(set(waiver_map) - set(base_surfaces))
    for relative in unknown_waiver_paths:
        errors.append(f"INVALID_IDENTITY_WAIVER {relative}: facade is not in the base surface")

    def unpack(surface):
        """Internal helper for unpack."""
        if len(surface) > 16:
            raise ValueError(f"unsupported identity surface shape: {len(surface)} fields")
        defaults = (
            frozenset(), (), (), frozenset(), None, frozenset(), None, (),
            frozenset(), (), None, (), (), (), frozenset(), (),
        )
        return tuple(surface) + defaults[len(surface):]

    for relative, base_surface in sorted(base_surfaces.items()):
        migrated_relative = (
            allow_directory_migration
            and relative.startswith("Scripts/phase192/")
        )
        current = head_surfaces.get(relative)
        if current is None:
            errors.append(f"REMOVED_FACADE {relative}")
            continue
        (
            base_symbols,
            base_sections,
            base_initializer,
            base_section_exports,
            base_digest,
            base_conditional,
            base_entrypoint,
            base_section_map,
            base_risks,
            base_conditional_contract,
            base_entrypoint_digest,
            base_entrypoint_contract,
            base_owner_map,
            base_relative_star_contract,
            base_public_symbols,
            base_nested_contract,
        ) = unpack(base_surface)
        (
            head_symbols,
            head_sections,
            head_initializer,
            head_section_exports,
            head_digest,
            head_conditional,
            head_entrypoint,
            head_section_map,
            head_risks,
            head_conditional_contract,
            head_entrypoint_digest,
            head_entrypoint_contract,
            head_owner_map,
            head_relative_star_contract,
            head_public_symbols,
            head_nested_contract,
        ) = unpack(current)
        if not base_public_symbols and base_symbols:
            base_public_symbols = frozenset(
                name for name in base_symbols if not name.startswith("_")
            )
        if not head_public_symbols and head_symbols:
            head_public_symbols = frozenset(
                name for name in head_symbols if not name.startswith("_")
            )
        reviewed_entrypoint_transition = _reviewed_direct_entrypoint_transition(
            relative,
            tuple(base_entrypoint_contract or ()),
            tuple(head_entrypoint_contract or ()),
        )
        reviewed_validator_split = _reviewed_validator_section_split(
            relative,
            tuple(base_sections),
            tuple(head_sections),
        )
        reviewed_template_registry_split = _reviewed_template_registry_split(
            relative,
            tuple(base_nested_contract or ()),
            tuple(head_nested_contract or ()),
            tuple(base_relative_star_contract or ()),
            tuple(head_relative_star_contract or ()),
        )
        reviewed_template_registry_facade = _reviewed_template_registry_facade_transition(
            relative,
            reviewed_source_digests,
        )
        missing_sections = sorted(set(base_sections) - set(head_sections))
        extra_sections = sorted(set(head_sections) - set(base_sections))
        if strict:
            missing_symbols = sorted(set(base_symbols) - set(head_symbols))
        else:
            effective_head_public_symbols = set(head_public_symbols)
            if reviewed_validator_split:
                effective_head_public_symbols.update(set(base_public_symbols) & set(head_symbols))
            public_removals = set(base_public_symbols) - effective_head_public_symbols
            waiver = set(waiver_map.get(relative, frozenset())) & set(base_public_symbols)
            missing_symbols = sorted(public_removals - waiver)
        extra_symbols = sorted(set(head_symbols) - set(base_symbols))
        if strict and base_risks != head_risks:
            errors.append(f"NAMESPACE_CONTRACT_CHANGED {relative}")
        if (
            strict
            and tuple(base_conditional_contract or ()) != tuple(head_conditional_contract or ())
        ):
            errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_section_map or ()) != tuple(head_section_map or ()):
            errors.append(f"SECTION_EXPORT_OWNER_CHANGED {relative}")
        if (
            tuple(base_nested_contract or ()) != tuple(head_nested_contract or ())
            and not reviewed_template_registry_split
        ):
            errors.append(f"NESTED_MODULE_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_owner_map or ()) != tuple(head_owner_map or ()):
            errors.append(f"SYMBOL_OWNER_CHANGED {relative}")
        base_relative = tuple(base_relative_star_contract or ())
        head_relative = tuple(head_relative_star_contract or ())
        if (
            (strict and base_relative != head_relative)
            or (
                not strict
                and not reviewed_template_registry_split
                and not reviewed_template_registry_facade
                and not _contract_preserves_order(base_relative, head_relative)
            )
        ):
            errors.append(f"RELATIVE_STAR_IMPORT_CHANGED {relative}")
        if not strict and not reviewed_template_registry_facade and base_risks != head_risks:
            errors.append(f"NAMESPACE_CONTRACT_CHANGED {relative}")
        if not strict:
            base_contract = tuple(base_conditional_contract or ())
            head_contract = tuple(head_conditional_contract or ())
            reviewed_validator_isolation = _reviewed_validator_isolation_transition(
                relative,
                base_contract,
                head_contract,
            )
            if (
                relative == "Scripts/package/validate_source_generator_dll.py"
                and base_contract != head_contract
            ):
                errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
                continue
            matched_head_indexes: set[int] = set()
            cursor = 0
            preserves_order = True
            for signature in base_contract:
                try:
                    index = head_contract.index(signature, cursor)
                except ValueError:
                    preserves_order = False
                    break
                matched_head_indexes.add(index)
                cursor = index + 1
            added_contract = [
                signature for index, signature in enumerate(head_contract)
                if index not in matched_head_indexes
            ]
            existing_conditional_names = set(base_symbols) | set(base_conditional)
            added_existing_bindings = {
                signature for signature in added_contract
                if any(
                    name in existing_conditional_names
                    for name in _conditional_signature_names(signature)
                )
            }
            if (
                not migrated_relative
                and not reviewed_template_registry_facade
                and not reviewed_validator_isolation
                and not reviewed_template_registry_split
                and (not preserves_order or added_existing_bindings)
            ):
                errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
        if base_entrypoint and head_entrypoint:
            if strict and base_entrypoint_digest != head_entrypoint_digest:
                errors.append(f"MODULE_ENTRYPOINT_CHANGED {relative}")
            elif not strict:
                entrypoint_changed = (
                    (
                        base_entrypoint_digest is not None
                        and head_entrypoint_digest is not None
                        and base_entrypoint_digest != head_entrypoint_digest
                    )
                    or (
                        (base_entrypoint_digest is None or head_entrypoint_digest is None)
                        and tuple(base_entrypoint_contract or ()) != tuple(head_entrypoint_contract or ())
                    )
                )
                if (
                    entrypoint_changed
                    and not reviewed_entrypoint_transition
                    and not migrated_relative
                    and not reviewed_template_registry_facade
                ):
                    errors.append(f"MODULE_ENTRYPOINT_CHANGED {relative}")
        if not strict and base_owner_map and head_owner_map and not reviewed_template_registry_facade:
            base_owners = dict(base_owner_map)
            head_owners = dict(head_owner_map)
            owner_changes = sorted(
                name for name in set(base_owners) & set(head_owners)
                if tuple(base_owners[name]) != tuple(head_owners[name])
            )
            new_collisions = sorted(
                name for name, owners in head_owners.items()
                if len(owners) > 1 and name not in base_owners
            )
            if owner_changes:
                errors.append(f"SYMBOL_OWNER_CHANGED {relative}: {','.join(owner_changes)}")
            if new_collisions:
                errors.append(f"SYMBOL_OWNER_COLLISION {relative}: {','.join(new_collisions)}")
        if not strict and base_section_map and head_section_map and not reviewed_template_registry_facade:
            base_exports_by_section = dict(base_section_map)
            head_exports_by_section = dict(head_section_map)
            def duplicate_exports(exports_by_section):
                """Internal helper for duplicate_exports."""
                owners: dict[str, set[str]] = {}
                for section_name, exports in exports_by_section.items():
                    for name in exports:
                        owners.setdefault(name, set()).add(section_name)
                return {name for name, sections_for_name in owners.items() if len(sections_for_name) > 1}
            new_collisions = duplicate_exports(head_exports_by_section) - duplicate_exports(base_exports_by_section)
            base_owners: dict[str, set[str]] = {}
            head_owners: dict[str, set[str]] = {}
            for section_name, exports in base_exports_by_section.items():
                for name in exports:
                    base_owners.setdefault(name, set()).add(section_name)
            for section_name, exports in head_exports_by_section.items():
                for name in exports:
                    head_owners.setdefault(name, set()).add(section_name)
            owner_changes = sorted(
                name for name in set(base_owners) & set(head_owners)
                if base_owners[name] != head_owners[name]
            )
            base_section_exports_all = set().union(*base_exports_by_section.values()) if base_exports_by_section else set()
            new_sections = set(head_exports_by_section) - set(base_exports_by_section)
            new_section_collisions = {
                f"{section_name}:{name}"
                for section_name in new_sections
                for name in head_exports_by_section[section_name]
                if name in base_section_exports_all or name in base_symbols
            }
            if new_collisions or new_section_collisions:
                details = sorted(new_collisions) + sorted(new_section_collisions)
                errors.append(f"SECTION_EXPORT_COLLISION {relative}: {','.join(details)}")
            if owner_changes:
                errors.append(f"SECTION_EXPORT_OWNER_CHANGED {relative}: {','.join(owner_changes)}")
        if strict:
            initializer_ok = (
                base_digest == head_digest
                if base_digest is not None and head_digest is not None
                else base_initializer == head_initializer
            )
        else:
            if base_owner_map:
                base_external_names = {
                    name
                    for name, owners in base_owner_map
                    if any(owner != "initializer" for owner in owners)
                }
            else:
                base_external_names = set(base_symbols)
            initializer_ok = _initializer_is_additive(
                base_initializer,
                head_initializer,
                frozenset(head_sections),
                frozenset(base_external_names)
                | (set(head_section_exports) - set(base_section_exports)),
                dict(head_section_map or ()),
            )
        if reviewed_validator_split:
            try:
                _reviewed_validator_initializer_is_safe(head_initializer, relative)
            except (SyntaxError, ValueError):
                initializer_ok = False
            else:
                initializer_ok = True
        if not initializer_ok and not migrated_relative and not reviewed_template_registry_facade:
            errors.append(f"PACKAGE_INITIALIZER_CHANGED {relative}")
        if not _sections_preserve_order(base_sections, head_sections):
            errors.append(f"SECTION_ORDER_CHANGED {relative}")
        if missing_sections:
            errors.append(f"MISSING_SECTIONS {relative}: {','.join(missing_sections)}")
        if strict and extra_sections:
            errors.append(f"EXTRA_SECTIONS {relative}: {','.join(extra_sections)}")
        if missing_symbols:
            errors.append(f"MISSING_SYMBOLS {relative}: {','.join(missing_symbols)}")
        conditional_changed = sorted(
            (set(base_symbols) - set(base_conditional)) & set(head_conditional)
        )
        if conditional_changed:
            errors.append(
                f"CONDITIONAL_SYMBOL_CHANGED {relative}: {','.join(conditional_changed)}"
            )
        if base_entrypoint and not head_entrypoint:
            errors.append(f"MISSING_MODULE_ENTRYPOINT {relative}")
        if strict and not base_entrypoint and head_entrypoint:
            errors.append(f"ADDED_MODULE_ENTRYPOINT {relative}")
        if strict and extra_symbols:
            errors.append(f"EXTRA_SYMBOLS {relative}: {','.join(extra_symbols)}")
    if strict:
        for relative in sorted(set(head_surfaces) - set(base_surfaces)):
            errors.append(f"ADDED_FACADE {relative}")
    return errors

def compare_revisions(repository: Path, base: str, head: str, *, strict: bool = True) -> int:
    """Internal helper for compare_revisions."""
    base = _git(repository, "rev-parse", "--verify", base + "^{commit}").strip()
    head = _git(repository, "rev-parse", "--verify", head + "^{commit}").strip()
    with _revision_checkout(repository, base) as base_root, _revision_checkout(repository, head) as head_root:
        base_surfaces = _surfaces(base_root, strict=strict)
        head_surfaces = _surfaces(head_root, strict=strict)
        allow_directory_migration = _directory_migration_is_active(
            base_surfaces,
            head_surfaces,
            strict=strict,
        )
        if allow_directory_migration:
            head_surfaces = _normalize_migrated_source_decomposition_surfaces(head_surfaces)
        base_helper_contract = _identity_helper_source_contract(base_root)
        head_helper_contract = _identity_helper_source_contract(head_root)
        reviewed_source_digests = {
            _REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE: _normalized_source_digest(
                head_root / _REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE
            )
        }
        helper_errors = _compare_identity_helper_contracts(
            base_helper_contract,
            head_helper_contract,
            base_root=base_root,
            head_root=head_root,
        )
        base_waivers = _load_identity_waivers(base_root)
        head_waivers = _load_identity_waivers(head_root)
        if strict and base_waivers != head_waivers:
            print("IDENTITY_WAIVER_CHANGED Scripts/validation/source_decomposition/identity_waivers.json")
            return 1
        if not strict:
            _validate_compatibility_new_sections(
                base_root,
                head_root,
                base_surfaces,
                head_surfaces,
            )
    errors = _compare_surfaces(
        base_surfaces,
        head_surfaces,
        strict=strict,
        waived_symbols=base_waivers if not strict else None,
        allow_directory_migration=allow_directory_migration,
        reviewed_source_digests=reviewed_source_digests,
    )
    errors = helper_errors + errors
    if errors:
        for error in errors:
            print(error)
        return 1
    verdict = "IDENTITY_EQUIVALENT" if strict else "IDENTITY_COMPATIBLE"
    print(
        f"{verdict} base={base} head={head} "
        f"base_facades={len(base_surfaces)} head_facades={len(head_surfaces)}"
    )
    return 0

def main(argv: list[str] | None = None) -> int:
    """Internal helper for main."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--repository", type=Path, default=Path("."))
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument(
        "--strict",
        dest="strict",
        action="store_true",
        default=True,
        help="reject additions and initializer changes; use for a decomposition audit (default)",
    )
    modes.add_argument(
        "--compatibility",
        dest="strict",
        action="store_false",
        help="allow additive surface and initializer changes while blocking removals",
    )
    args = parser.parse_args(argv)
    return compare_revisions(args.repository.resolve(), args.base, args.head, strict=args.strict)

__all__ = ['_surface', '_surfaces', '_conditional_signature_names', '_reviewed_validator_section_split', '_reviewed_template_registry_split', '_normalize_migrated_source_decomposition_surfaces', '_directory_migration_is_active', '_reviewed_template_registry_facade_transition', '_REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE', '_REVIEWED_TEMPLATE_REGISTRY_FACADE_DIGEST', '_REVIEWED_VALIDATOR_CLEANUP_SIGNATURE', '_IDENTITY_HELPER_RELATIVES', '_REVIEWED_NAMESPACE_SPLIT_HELPER_DIGESTS', '_normalized_source_digest', '_identity_helper_source_contract', '_compare_identity_helper_contracts', '_reviewed_validator_isolation_transition', '_compare_surfaces', 'compare_revisions', 'main']
