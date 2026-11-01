from .foundation import *


class NamespaceExecutionCoreTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_namespace_gate_rejects_import_time_local_subprocess_call(self) -> None:
        """Import-time calls hidden behind a local function remain visible to the gate."""
        tree = ast.parse("import subprocess\ndef hidden():\n    subprocess.run([\"bad\"])\nhidden()")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("subprocess" in item and "run" in item for item in risks))

    def test_namespace_gate_rejects_import_time_print(self) -> None:
        """Top-level output is an observable import-time side effect."""
        tree = ast.parse("print(\"unexpected\")")
        self.assertTrue(compare_identity_surfaces._namespace_risk_signatures(tree))

    def test_namespace_gate_rejects_side_effectful_decorator(self) -> None:
        """Decorator calls execute while a facade is imported."""
        tree = ast.parse("@subprocess.run()\ndef entry():\n    pass")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("subprocess" in item and "run" in item for item in risks))

    def test_namespace_gate_rejects_import_time_lambda_call(self) -> None:
        """Import-time calls through lambda aliases remain visible to the gate."""
        tree = ast.parse("callback = lambda: print(\"unexpected\")\ncallback()")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("print" in item for item in risks))

    def test_namespace_gate_rejects_import_time_function_alias_call(self) -> None:
        """Import-time calls through function aliases remain visible to the gate."""
        tree = ast.parse(
            "def callback():\n    print(\"unexpected\")\n"
            "alias = callback\n"
            "alias()"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("print" in item for item in risks))

    def test_namespace_gate_ignores_uninvoked_nested_function(self) -> None:
        """Nested functions that are never called do not execute at import time."""
        tree = ast.parse(
            "def outer():\n"
            "    def hidden():\n"
            "        print(\"unexpected\")\n"
            "    return 1\n"
            "outer()"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("print" in item for item in risks))

    def test_namespace_gate_rejects_aliased_subprocess_call(self) -> None:
        """Import aliases for subprocess expose the same process risk as direct calls."""
        tree = ast.parse("import subprocess as sp\nsp.run([\"bad\"])")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("run" in item for item in risks))

    def test_namespace_gate_merges_branch_alias_bindings(self) -> None:
        """Bindings created in a conditional remain visible after the branch."""
        tree = ast.parse(
            "if flag:\n"
            "    import subprocess as sp\n"
            "sp.run([\"bad\"])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("run" in item for item in risks))

    def test_namespace_gate_snapshots_alias_assignment(self) -> None:
        """An alias keeps the object captured at assignment time."""
        tree = ast.parse(
            "import subprocess\n"
            "callback = subprocess.run\n"
            "import os\n"
            "subprocess = os\n"
            "callback(\"bad\")\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(risks)

    def test_namespace_gate_reanalyzes_callable_for_changed_bindings(self) -> None:
        """A callable is rescanned when its caller environment changes."""
        tree = ast.parse(
            "def helper():\n"
            "    callback(\"bad\")\n"
            "import os\n"
            "callback = os.getenv\n"
            "helper()\n"
            "import subprocess\n"
            "callback = subprocess.run\n"
            "helper()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(risks)

    def test_namespace_gate_binds_default_parameter_aliases(self) -> None:
        """Default parameter objects are available inside the called function."""
        tree = ast.parse(
            "import subprocess\n"
            "def helper(callback=subprocess.run):\n"
            "    callback(\"bad\")\n"
            "helper()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(risks)

    def test_namespace_gate_ignores_deferred_async_and_generator_bodies(self) -> None:
        """Creating a coroutine or generator does not execute its body."""
        tree = ast.parse(
            "import subprocess\n"
            "async def async_helper():\n"
            "    subprocess.run([\"async\"])\n"
            "async_value = async_helper()\n"
            "def generator_helper():\n"
            "    yield 1\n"
            "    subprocess.run([\"generator\"])\n"
            "generator_value = generator_helper()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("subprocess" in item for item in risks))

    def test_namespace_gate_scans_driven_async_and_generator_bodies(self) -> None:
        """Awaiting or iterating a deferred callable executes its body."""
        async_tree = ast.parse(
            "import subprocess\n"
            "async def async_helper():\n"
            "    subprocess.run([\"async\"])\n"
            "await async_helper()\n"
        )
        generator_tree = ast.parse(
            "import subprocess\n"
            "def generator_helper():\n"
            "    yield 1\n"
            "    subprocess.run([\"generator\"])\n"
            "for _ in generator_helper():\n"
            "    pass\n"
        )
        self.assertTrue(
            compare_identity_surfaces._namespace_risk_signatures(async_tree)
        )
        self.assertTrue(
            compare_identity_surfaces._namespace_risk_signatures(generator_tree)
        )

    def test_namespace_gate_ignores_postponed_annotations(self) -> None:
        """Future annotations are stored as strings and are not evaluated at definition time."""
        tree = ast.parse(
            "from __future__ import annotations\n"
            "def helper(annotation: print(\"annotation\") = None):\n"
            "    pass\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("print" in item for item in risks))

    def test_namespace_gate_rejects_local_os_system_call(self) -> None:
        """Import-time calls through a local os.system helper remain visible."""
        tree = ast.parse("import os\ndef helper():\n    os.system(\"bad\")\nhelper()")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("system" in item for item in risks))

    def test_namespace_gate_rejects_dynamic_subprocess_dispatch(self) -> None:
        """Dynamic subprocess dispatch and executable annotations remain visible."""
        sources = (
            'import subprocess\ngetattr(subprocess, "run")(["bad"])\n',
            'import subprocess\nx: subprocess.run(["bad"]) = 1\n',
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(risks)

    def test_phase184_owner_callers_use_the_qualified_import_path(self) -> None:
        """Every Phase184 owner caller uses the single supported qualified import path."""
        qualified = "Scripts.smoke.foxrun.phase184_windows_job_owner"
        found = []
        for path in Path("Scripts").rglob("*.py"):
            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            for node in ast.walk(tree):
                if isinstance(node, ast.ImportFrom) and any(
                    alias.name == "phase184_windows_job_owner" for alias in node.names
                ):
                    found.append((path, node.module, node.level))
                    self.assertEqual("Scripts.smoke.foxrun", node.module, path)
                    self.assertEqual(0, node.level, path)
                if isinstance(node, ast.Import):
                    for alias in node.names:
                        if alias.name.split(".")[-1] == "phase184_windows_job_owner":
                            found.append((path, alias.name, 0))
                            self.assertEqual(qualified, alias.name, path)
                if (
                    isinstance(node, ast.Call)
                    and isinstance(node.func, (ast.Name, ast.Attribute))
                    and (
                        node.func.id == "import_module"
                        if isinstance(node.func, ast.Name)
                        else node.func.attr == "import_module"
                    )
                    and node.args
                    and isinstance(node.args[0], ast.Constant)
                    and isinstance(node.args[0].value, str)
                    and node.args[0].value.split(".")[-1] == "phase184_windows_job_owner"
                ):
                    found.append((path, node.args[0].value, 0))
                    self.assertEqual(qualified, node.args[0].value, path)
        self.assertGreaterEqual(len(found), 2)

    def test_namespace_gate_covers_nested_definition_time_expressions(self) -> None:
        """Nested function defaults and decorators execute when the definition executes."""
        tree = ast.parse(
            "import subprocess\n"
            "def outer():\n"
            "    @subprocess.run([\"decorator\"])\n"
            "    def decorated():\n"
            "        pass\n"
            "    def defaulted(value=subprocess.run([\"default\"])):\n"
            "        pass\n"
            "outer()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertGreaterEqual(sum("subprocess" in item for item in risks), 2)

    def test_namespace_gate_scopes_aliases_and_preserves_statement_order(self) -> None:
        """Local rebinding and later assignments cannot hide earlier module calls."""
        tree = ast.parse(
            "import subprocess as sp\n"
            "def helper():\n"
            "    import os as sp\n"
            "helper()\n"
            "sp.run([\"before-rebind\"])\n"
            "sp = None\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("before-rebind" in item for item in risks))

    def test_namespace_gate_checks_namespace_attributes_in_import_time_helpers(self) -> None:
        """Import-time helper calls retain namespace and dynamic-import fingerprints."""
        tree = ast.parse(
            "import importlib\n"
            "def initialize():\n"
            "    importlib.import_module(\"math\")\n"
            "initialize()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("import_module" in item for item in risks))

    def test_namespace_gate_scans_mixed_callable_candidates(self) -> None:
        """Every callable candidate is scanned when a merged alias is invoked."""
        tree = ast.parse(
            "import subprocess\n"
            "def helper():\n"
            "    subprocess.run([\"helper-only\"])\n"
            "cb = helper\n"
            "if flag:\n"
            "    cb = subprocess.run\n"
            "cb([\"direct\"])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("helper-only" in item for item in risks))

    def test_namespace_gate_scans_alias_of_next(self) -> None:
        """An alias of next drives a generator until its first yield."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    subprocess.run([\"before-yield\"])\n"
            "    yield 1\n"
            "next_fn = next\n"
            "next_fn(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("before-yield" in item for item in risks))

    def test_namespace_gate_scans_immediate_generator_consumers(self) -> None:
        """Immediate iterable consumers scan the complete generator body."""
        consumers = ("list", "tuple", "sum", "set", "frozenset", "sorted", "dict", "bytes", "bytearray")
        for consumer in consumers:
            with self.subTest(consumer=consumer):
                tree = ast.parse(
                    "import subprocess\n"
                    "def generator():\n"
                    "    yield 1\n"
                    f"    subprocess.run([\"{consumer}-consumer\"])\n"
                    f"{consumer}(generator())\n"
                )
                risks = compare_identity_surfaces._namespace_risk_signatures(tree)
                self.assertTrue(any(f"{consumer}-consumer" in item for item in risks))

    def test_namespace_gate_resolves_expression_and_unpack_aliases(self) -> None:
        """Conditional and tuple-unpacked aliases preserve every callable target."""
        sources = (
            "import subprocess\n"
            "def helper():\n"
            "    subprocess.run([\"ifexp-helper\"])\n"
            "cb = helper if flag else subprocess.run\n"
            "cb([\"ifexp-call\"])\n",
            "import subprocess\n"
            "def helper():\n"
            "    subprocess.run([\"unpack-helper\"])\n"
            "a, b = helper, subprocess.run\n"
            "a([\"unpack-call\"])\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("helper" in item and "subprocess" in item for item in risks))

    def test_namespace_gate_scans_bare_open_in_called_helper(self) -> None:
        """A called helper using bare open is an import-time namespace risk."""
        tree = ast.parse(
            "def initialize():\n"
            "    open(\"side-effect\", \"w\")\n"
            "initialize()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("open" in item for item in risks))

    def test_namespace_gate_scans_bare_decorator_helper(self) -> None:
        """A bare decorator alias executes its helper while the definition is created."""
        tree = ast.parse(
            "import subprocess\n"
            "def decorator(function):\n"
            "    subprocess.run([\"bare-decorator\"])\n"
            "    return function\n"
            "@decorator\n"
            "def entry():\n"
            "    pass\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("bare-decorator" in item for item in risks))

    def test_namespace_gate_stops_after_first_yield_for_next(self) -> None:
        """next scans only the prefix executed before the first yielded value."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    yield 1\n"
            "    subprocess.run([\"after-first-yield\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("after-first-yield" in item for item in risks))

    def test_namespace_gate_scans_after_first_yield_for_full_consumer(self) -> None:
        """A full iterable consumer scans generator code after the first yield."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    yield 1\n"
            "    subprocess.run([\"after-full-consumer\"])\n"
            "list(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("after-full-consumer" in item for item in risks))

    def test_namespace_gate_scans_awaited_anext_alias(self) -> None:
        """Awaiting an anext alias scans only the first async-generator step."""
        tree = ast.parse(
            "import subprocess\n"
            "async def generator():\n"
            "    subprocess.run([\"async-before-yield\"])\n"
            "    yield 1\n"
            "    subprocess.run([\"async-after-yield\"])\n"
            "anext_fn = anext\n"
            "await anext_fn(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("async-before-yield" in item for item in risks))
        self.assertFalse(any("async-after-yield" in item for item in risks))

    def test_namespace_gate_ignores_unawaited_anext_alias(self) -> None:
        """Creating an anext awaitable without awaiting it does not execute the body."""
        tree = ast.parse(
            "import subprocess\n"
            "async def generator():\n"
            "    subprocess.run([\"unawaited-anext\"])\n"
            "    yield 1\n"
            "anext_fn = anext\n"
            "anext_fn(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("unawaited-anext" in item for item in risks))

    def test_namespace_gate_scans_path_and_sys_path_mutators(self) -> None:
        """Path.open and sys.path mutation remain visible through called helpers."""
        sources = (
            "from pathlib import Path\n"
            "def initialize():\n"
            "    Path(\"side-effect\").open(\"w\")\n"
            "initialize()\n",
            "import sys\n"
            "def initialize():\n"
            "    sys.path.append(\"side-effect\")\n"
            "initialize()\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(risks)

    def test_namespace_gate_scans_after_partial_generator_branch_yield(self) -> None:
        """A branch-local yield must not hide a later import-time path."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator(flag):\n"
            "    if flag:\n"
            "        yield 1\n"
            "    else:\n"
            "        pass\n"
            "    subprocess.run([\"after-branch-yield\"])\n"
            "next(generator(False))\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("after-branch-yield" in item for item in risks))

    def test_namespace_gate_propagates_first_yield_through_nested_for(self) -> None:
        """A generator consumed by a first-yield loop inherits deferred mode."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"nested-before-yield\"])\n"
            "    yield 1\n"
            "def outer():\n"
            "    for value in inner():\n"
            "        yield value\n"
            "next(outer())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("nested-before-yield" in item for item in risks))

    def test_namespace_gate_propagates_full_mode_through_yield_from(self) -> None:
        """Full consumers must drive yield-from delegates to completion."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    yield 1\n"
            "    subprocess.run([\"yield-from-tail\"])\n"
            "def outer():\n"
            "    yield from inner()\n"
            "list(outer())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("yield-from-tail" in item for item in risks))

    def test_namespace_gate_preserves_first_yield_mode_through_yield_from(self) -> None:
        """First-yield consumers do not scan a delegate after its first yield."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    yield 1\n"
            "    subprocess.run([\"yield-from-next-tail\"])\n"
            "def outer():\n"
            "    yield from inner()\n"
            "next(outer())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("yield-from-next-tail" in item for item in risks))

    def test_namespace_gate_skips_try_continuations_after_first_yield(self) -> None:
        """Try else/finally code after a first yield is deferred."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    try:\n"
            "        yield 1\n"
            "    except Exception:\n"
            "        pass\n"
            "    else:\n"
            "        subprocess.run([\"try-else-tail\"])\n"
            "    finally:\n"
            "        subprocess.run([\"try-finally-tail\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("try-else-tail" in item for item in risks))
        self.assertTrue(any("try-finally-tail" in item for item in risks))

    def test_namespace_gate_skips_loop_continuations_after_first_yield(self) -> None:
        """Loop else and post-loop code are deferred after a yielded value."""
        sources = (
            "import subprocess\n"
            "def generator():\n"
            "    for item in [1]:\n"
            "        yield item\n"
            "    else:\n"
            "        subprocess.run([\"for-else-tail\"])\n"
            "    subprocess.run([\"for-after-tail\"])\n"
            "next(generator())\n",
            "import subprocess\n"
            "def generator():\n"
            "    while True:\n"
            "        yield 1\n"
            "    else:\n"
            "        subprocess.run([\"while-else-tail\"])\n"
            "    subprocess.run([\"while-after-tail\"])\n"
            "next(generator())\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertFalse(any("tail" in item for item in risks))

    def test_namespace_gate_scans_empty_loop_paths_after_possible_yield(self) -> None:
        """Possibly empty loops must retain the path that reaches following code."""
        sources = (
            "import subprocess\n"
            "def generator(items):\n"
            "    for item in items:\n"
            "        yield item\n"
            "    subprocess.run([\"empty-for-after\"])\n"
            "next(generator([]))\n",
            "import subprocess\n"
            "def generator():\n"
            "    for item in []:\n"
            "        yield item\n"
            "    subprocess.run([\"zero-for-after\"])\n"
            "next(generator())\n",
            "import subprocess\n"
            "def generator(flag):\n"
            "    while flag:\n"
            "        yield 1\n"
            "    subprocess.run([\"empty-while-after\"])\n"
            "next(generator(False))\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("after" in item for item in risks))

    def test_namespace_gate_does_not_assume_shadowed_range_is_nonempty(self) -> None:
        """A shadowed range must preserve the loop exit path."""
        tree = ast.parse(
            "import subprocess\n"
            "def range(*args):\n"
            "    return []\n"
            "def generator():\n"
            "    for _ in range(1):\n"
            "        yield 1\n"
            "    subprocess.run([\"range-shadow-after-loop\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("range-shadow-after-loop" in item for item in risks))

    def test_namespace_gate_scans_all_pathlib_mutators(self) -> None:
        """Path link and permission mutators remain import-time filesystem risks."""
        for method in ("symlink_to", "hardlink_to", "chmod", "lchmod"):
            with self.subTest(method=method):
                tree = ast.parse(
                    "from pathlib import Path\n"
                    "def initialize():\n"
                    f"    Path(\"side-effect\").{method}(\"target\")\n"
                    "initialize()\n"
                )
                risks = compare_identity_surfaces._namespace_risk_signatures(tree)
                self.assertTrue(risks)

    def test_namespace_gate_propagates_deferred_mode_through_generator_expression(self) -> None:
        """Generator expression iterators inherit the enclosing consumer mode."""
        for consumer in ("next", "list"):
            with self.subTest(consumer=consumer):
                tree = ast.parse(
                    "import subprocess\n"
                    "def inner():\n"
                    "    subprocess.run([\"generator-expression-before\"])\n"
                    "    yield 1\n"
                    f"{consumer}(item for item in inner())\n"
                )
                risks = compare_identity_surfaces._namespace_risk_signatures(tree)
                self.assertTrue(any("generator-expression-before" in item for item in risks))

    def test_namespace_gate_does_not_drive_generators_nested_in_containers(self) -> None:
        """Constructing a container of generator objects does not execute their bodies."""
        sources = (
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"container-list\"])\n"
            "    yield 1\n"
            "list([inner()])\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"container-tuple\"])\n"
            "    yield 1\n"
            "next((inner(),))\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertFalse(any("container-" in item for item in risks))

    def test_namespace_gate_drives_generator_object_aliases(self) -> None:
        """Eager consumers drive generator objects retained in a simple alias."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"generator-alias\"])\n"
            "    yield 1\n"
            "iterator = inner()\n"
            "list(iterator)\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("generator-alias" in item for item in risks))

    def test_namespace_gate_propagates_mode_through_driving_expression_wrappers(self) -> None:
        """Driving expressions retain deferred mode through wrappers and comprehensions."""
        sources = (
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"ifexp-drive\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next(inner() if True else safe())\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"boolop-drive\"])\n"
            "    yield 1\n"
            "next(inner() or iter(()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"namedexpr-drive\"])\n"
            "    yield 1\n"
            "next((iterator := inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"subscript-drive\"])\n"
            "    yield 1\n"
            "next([inner()][0])\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"starred-drive\"])\n"
            "    yield 1\n"
            "list([*inner()])\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"listcomp-drive\"])\n"
            "    yield 1\n"
            "list([value for value in inner()])\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"setcomp-drive\"])\n"
            "    yield 1\n"
            "set({value for value in inner()})\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"dictcomp-drive\"])\n"
            "    yield 1\n"
            "dict({value: value for value in inner()})\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("-drive" in item for item in risks))


__all__ = ["NamespaceExecutionCoreTests"]
