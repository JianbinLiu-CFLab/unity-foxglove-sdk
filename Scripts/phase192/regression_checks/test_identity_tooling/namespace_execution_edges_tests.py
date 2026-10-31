from .foundation import *


class NamespaceExecutionEdgesTests(IdentityToolingBase):
    """Identity tooling regression tests for this contract family."""
    def test_namespace_gate_scans_unbound_yield_exception_handlers(self) -> None:
        """An unbound yield expression can raise before the first value is produced."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    try:\n"
            "        yield missing\n"
            "    except NameError:\n"
            "        subprocess.run([\"unbound-yield-except\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("unbound-yield-except" in item for item in risks))

    def test_namespace_gate_drives_or_and_iterator_wrappers(self) -> None:
        """Driving consumers preserve deferred mode through or, iter, and combinators."""
        sources = (
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"or-generator-expression\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next((value for value in inner()) or safe())\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"iter-wrapper\"])\n"
            "    yield 1\n"
            "next(iter(inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"map-wrapper\"])\n"
            "    yield 1\n"
            "list(map(lambda value: value, inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"filter-wrapper\"])\n"
            "    yield 1\n"
            "list(filter(None, inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"zip-wrapper\"])\n"
            "    yield 1\n"
            "list(zip([1], inner()))\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("wrapper" in item or "or-generator-expression" in item for item in risks))

    def test_namespace_gate_scans_comprehensions_inside_yield_values(self) -> None:
        """A comprehension in a yield value fully drives its input iterator first."""
        sources = (
            "import subprocess\n"
            "def inner():\n"
            "    yield 1\n"
            "    subprocess.run([\"yield-listcomp-after\"])\n"
            "def outer():\n"
            "    yield [value for value in inner()]\n"
            "next(outer())\n",
            "import subprocess\n"
            "def inner():\n"
            "    yield 1\n"
            "    subprocess.run([\"yield-setcomp-after\"])\n"
            "def outer():\n"
            "    yield {value for value in inner()}\n"
            "next(outer())\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("yield-" in item for item in risks))

    def test_namespace_gate_scans_dynamic_subscript_of_returned_sequence(self) -> None:
        """A dynamic subscript still drives a generator returned in a sequence."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"dynamic-return-subscript\"])\n"
            "    yield 1\n"
            "def make():\n"
            "    return [inner()]\n"
            "index = 0\n"
            "next(make()[index])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("dynamic-return-subscript" in item for item in risks))

    def test_namespace_gate_preserves_loop_break_paths(self) -> None:
        """A break before the first yield keeps the loop exit path reachable."""
        sources = (
            "import subprocess\n"
            "def generator():\n"
            "    while True:\n"
            "        break\n"
            "        yield 1\n"
            "    subprocess.run([\"after-break-while\"])\n"
            "next(generator())\n",
            "import subprocess\n"
            "def generator(items):\n"
            "    for item in items:\n"
            "        break\n"
            "        yield item\n"
            "    subprocess.run([\"after-break-for\"])\n"
            "next(generator([1]))\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("after-break" in item for item in risks))

    def test_namespace_gate_scans_handlers_for_raising_yield_values(self) -> None:
        """Exceptions while evaluating a yield value can enter an except handler."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    try:\n"
            "        yield iter([]).__next__()\n"
            "    except Exception:\n"
            "        subprocess.run([\"except-before-yield\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("except-before-yield" in item for item in risks))

    def test_namespace_gate_respects_constant_and_short_circuit_branches(self) -> None:
        """Deferred mode follows only branches that are actually evaluated."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"unreachable-branch\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next(safe() if True else inner())\n"
            "next(safe() or inner())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("unreachable-branch" in item for item in risks))

    def test_namespace_gate_drives_all_iterable_arguments(self) -> None:
        """map, filter, and zip drive their iterable arguments under an eager consumer."""
        sources = (
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"map-argument\"])\n"
            "    yield 1\n"
            "list(map(lambda value: value, inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"filter-argument\"])\n"
            "    yield 1\n"
            "list(filter(None, inner()))\n",
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"zip-argument\"])\n"
            "    yield 1\n"
            "list(zip([1], inner()))\n",
        )
        for source in sources:
            with self.subTest(source=source):
                risks = compare_identity_surfaces._namespace_risk_signatures(ast.parse(source))
                self.assertTrue(any("-argument" in item for item in risks))

    def test_namespace_gate_scans_all_merged_subscript_candidates(self) -> None:
        """A merged sequence alias retains every candidate selected by a subscript."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"merged-subscript\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "pair = (inner(), safe()) if flag else (safe(), inner())\n"
            "next(pair[0])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("merged-subscript" in item for item in risks))

    def test_namespace_gate_preserves_loop_continue_paths(self) -> None:
        """A continue before the first yield keeps the loop exit path reachable."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    for item in [1]:\n"
            "        continue\n"
            "        yield item\n"
            "    subprocess.run([\"after-continue\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("after-continue" in item for item in risks))

    def test_namespace_gate_handles_same_line_break_before_yield(self) -> None:
        """Statement columns distinguish same-line break and yield operations."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    while True:\n"
            "        break; yield 1\n"
            "    subprocess.run([\"same-line-break\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("same-line-break" in item for item in risks))

    def test_namespace_gate_drives_dynamic_subscript_candidates(self) -> None:
        """A dynamic subscript conservatively drives every sequence candidate."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"dynamic-subscript\"])\n"
            "    yield 1\n"
            "index = 0\n"
            "next([inner()][index])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("dynamic-subscript" in item for item in risks))

    def test_namespace_gate_scans_constant_subscript_container_side_effects(self) -> None:
        """Constant subscripts still evaluate every container element first."""
        tree = ast.parse(
            "import subprocess\n"
            "def dangerous():\n"
            "    subprocess.run([\"constant-subscript-dangerous\"])\n"
            "    return 1\n"
            "def safe():\n"
            "    yield 1\n"
            "next([dangerous(), safe()][1])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("constant-subscript-dangerous" in item for item in risks))

    def test_namespace_gate_does_not_drive_unselected_constant_subscript_generators(self) -> None:
        """An unselected generator element is only constructed, not consumed."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"constant-subscript-unselected\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next([inner(), safe()][1])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("constant-subscript-unselected" in item for item in risks))

    def test_namespace_gate_scans_starred_constant_subscript_iteration(self) -> None:
        """Starred elements are fully consumed while a literal container is built."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"starred-subscript-dangerous\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next([*inner(), safe()][1])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("starred-subscript-dangerous" in item for item in risks))

    def test_namespace_gate_does_not_drive_nonfinal_and_operands(self) -> None:
        """Operands evaluated only for an and-condition are not consumed generators."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"and-left\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next(inner() and safe())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("and-left" in item for item in risks))

    def test_namespace_gate_scans_unbound_yield_handlers(self) -> None:
        """An unbound yield value can enter a matching exception handler."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    try:\n"
            "        yield missing\n"
            "    except NameError:\n"
            "        subprocess.run([\"unbound-yield-handler\"])\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("unbound-yield-handler" in item for item in risks))

    def test_namespace_gate_drives_or_left_generator_expression(self) -> None:
        """An or expression can return its left generator to an eager consumer."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"or-generator-left\"])\n"
            "    yield 1\n"
            "def safe():\n"
            "    yield 2\n"
            "next((value for value in inner()) or safe())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("or-generator-left" in item for item in risks))

    def test_namespace_gate_drives_yielded_comprehension_iterators(self) -> None:
        """A comprehension in a yielded value fully consumes its iterator."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    yield 1\n"
            "    subprocess.run([\"yielded-comprehension-tail\"])\n"
            "def generator():\n"
            "    yield [value for value in inner()]\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("yielded-comprehension-tail" in item for item in risks))

    def test_namespace_gate_drives_generator_in_dynamic_return_subscript(self) -> None:
        """A dynamically indexed returned sequence can contain a generator."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"dynamic-return-subscript\"])\n"
            "    yield 1\n"
            "def make():\n"
            "    return [inner()]\n"
            "index = 0\n"
            "next(make()[index])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("dynamic-return-subscript" in item for item in risks))

    def test_namespace_gate_drives_lambda_returned_generator(self) -> None:
        """A lambda returning a generator is driven by next."""
        tree = ast.parse(
            "import subprocess\n"
            "def inner():\n"
            "    subprocess.run([\"lambda-returned-generator\"])\n"
            "    yield 1\n"
            "factory = lambda: (value for value in inner())\n"
            "next(factory())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("lambda-returned-generator" in item for item in risks))

    def test_namespace_gate_scans_with_enter_before_first_yield(self) -> None:
        """A context manager enter hook executes before a yielded body."""
        tree = ast.parse(
            "import subprocess\n"
            "class Context:\n"
            "    def __enter__(self):\n"
            "        subprocess.run([\"with-enter-before-yield\"])\n"
            "        return self\n"
            "    def __exit__(self, *args):\n"
            "        return False\n"
            "def generator():\n"
            "    with Context():\n"
            "        yield 1\n"
            "next(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("with-enter-before-yield" in item for item in risks))

    def test_namespace_gate_executes_bare_decorator_parameters(self) -> None:
        """Bare decorators are calls and their parameter-driven bodies execute."""
        tree = ast.parse(
            "import subprocess\n"
            "def decorator(function):\n"
            "    function()\n"
            "def decorated():\n"
            "    subprocess.run([\"bare-decorator-body\"])\n"
            "@decorator\n"
            "def entry():\n"
            "    decorated()\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("bare-decorator-body" in item for item in risks))

    def test_namespace_gate_executes_bare_class_decorator_parameters(self) -> None:
        """Bare class decorators are also calls with the decorated object argument."""
        tree = ast.parse(
            "import subprocess\n"
            "def decorator(cls):\n"
            "    subprocess.run([\"bare-class-decorator\"])\n"
            "    return cls\n"
            "@decorator\n"
            "class Entry:\n"
            "    pass\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("bare-class-decorator" in item for item in risks))

    def test_namespace_gate_scans_path_mutators_inside_called_helpers(self) -> None:
        """Path mutators beyond open remain import-time filesystem risks."""
        for method in ("touch", "write_text", "write_bytes", "unlink", "mkdir"):
            with self.subTest(method=method):
                tree = ast.parse(
                    "from pathlib import Path\n"
                    "def initialize():\n"
                    f"    Path(\"side-effect\").{method}()\n"
                    "initialize()\n"
                )
                risks = compare_identity_surfaces._namespace_risk_signatures(tree)
                self.assertTrue(risks)

    def test_namespace_gate_scans_all_eager_generator_consumers(self) -> None:
        """Eager built-in consumers must include min, max, any and all."""
        for consumer in ("min", "max", "any", "all"):
            with self.subTest(consumer=consumer):
                tree = ast.parse(
                    "import subprocess\n"
                    "def generator():\n"
                    "    yield 1\n"
                    f"    subprocess.run([\"{consumer}-tail\"])\n"
                    f"{consumer}(generator())\n"
                )
                risks = compare_identity_surfaces._namespace_risk_signatures(tree)
                self.assertTrue(any(f"{consumer}-tail" in item for item in risks))

    def test_namespace_gate_preserves_conditional_sequence_candidates(self) -> None:
        """Conditional tuple destructuring retains every callable candidate."""
        tree = ast.parse(
            "import subprocess\n"
            "def helper():\n"
            "    subprocess.run([\"sequence-helper\"])\n"
            "pair = (helper, subprocess.run) if flag else (subprocess.run, helper)\n"
            "a, b = pair\n"
            "a([\"sequence-call\"])\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("sequence-helper" in item for item in risks))

    def test_generated_section_rejects_resolved_import_time_risks(self) -> None:
        """Generated sections reject aliases that resolve to import-time side effects."""
        sources = (
            "import subprocess\n"
            "def helper():\n"
            "    subprocess.run([\"generated-helper\"])\n"
            "cb = helper if flag else subprocess.run\n"
            "cb([\"generated-call\"])\n"
            "__all__ = [\"helper\"]\n",
            "def helper():\n"
            "    open(\"generated-open\", \"w\")\n"
            "helper()\n"
            "__all__ = [\"helper\"]\n",
        )
        for source in sources:
            with self.subTest(source=source), self.assertRaises(ValueError):
                compare_identity_surfaces._validate_generated_section(
                    source,
                    Path("Scripts/smoke/fixture.py"),
                    Path("Scripts/smoke"),
                    frozenset({"fixture"}),
                )

    def test_namespace_gate_revisits_generator_for_full_consumer_after_next(self) -> None:
        """A later full consumer is not hidden by an earlier first-yield scan."""
        tree = ast.parse(
            "import subprocess\n"
            "def generator():\n"
            "    yield 1\n"
            "    subprocess.run([\"second-consumer\"])\n"
            "next(generator())\n"
            "list(generator())\n"
        )
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertTrue(any("second-consumer" in item for item in risks))

    def test_namespace_gate_ignores_relative_subprocess_module_aliases(self) -> None:
        """Relative modules are not silently classified as the stdlib subprocess module."""
        tree = ast.parse("from .subprocess import run as launch\nlaunch([\"local\"])\n")
        risks = compare_identity_surfaces._namespace_risk_signatures(tree)
        self.assertFalse(any("subprocess" in item and "run" in item for item in risks))


__all__ = ["NamespaceExecutionEdgesTests"]
