from __future__ import annotations

import unittest

from .test_identity_gate_hardening import IdentityGateHardeningTests


class DeferredIdentityConsumptionTests(unittest.TestCase):
    """Regression coverage for deferred identity execution and consumption."""

    _compatibility_errors = IdentityGateHardeningTests._compatibility_errors


    def test_compatibility_allows_unexecuted_lambda_and_generator_bodies(self) -> None:
        """Deferred lambda and generator bodies do not execute while objects are created."""
        base = "class Settings:\n    VALUE = 1\n__all__ = ['Settings']\n"
        head = (
            "import os\n"
            "class Settings:\n"
            "    callback = lambda: os.environ.setdefault('FLAG', '1')\n"
            "    values = (os.environ.setdefault('OTHER', '1') for _ in ())\n"
            "__all__ = ['Settings']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_deferred_lambda_passed_to_regular_call(self) -> None:
        """Passing a lambda to an ordinary call does not prove that its body executes."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "register(lambda: os.environ.setdefault('FLAG', '1'))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_deferred_lambda_in_function_default(self) -> None:
        """A lambda body in a function default remains deferred after definition."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "def register(callback=lambda: os.environ.setdefault('FLAG', '1')):\n"
            "    return callback\n"
            "def helper():\n    return 1\n"
            "__all__ = ['helper', 'register']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_called_lambda_body(self) -> None:
        """An immediately called lambda executes its environment write during import."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "(lambda: os.environ.setdefault('FLAG', '1'))()\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_consumed_generator_body(self) -> None:
        """A generator body executes when an import-time consumer drains it."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_generator_unpacking(self) -> None:
        """Tuple unpacking consumes a generator during import."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "first, = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_allows_deferred_callback_inside_consumed_generator(self) -> None:
        """Consuming a generator does not prove that a callback passed from its body runs."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (register(lambda: os.environ.setdefault('FLAG', '1')) for _ in [1])\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_scopes_class_deferred_bindings(self) -> None:
        """Class-local deferred bindings must not change later module-level calls."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "callback = lambda: None\n"
            "class Settings:\n"
            "    callback = lambda: os.environ.setdefault('FLAG', '1')\n"
            "callback()\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_callback_in_ordinary_iterator_factory(self) -> None:
        """Consuming an ordinary factory result does not execute its callback arguments."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "def make_iter(callback):\n"
            "    return [1]\n"
            "for item in make_iter(lambda: os.environ.setdefault('FLAG', '1')):\n"
            "    pass\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_next_consumption(self) -> None:
        """Calling next on a tracked generator executes its body during import."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "next(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_comprehension_consumption(self) -> None:
        """Comprehensions consume tracked generators and execute their bodies."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "items = [value for value in values]\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_consumer_alias(self) -> None:
        """A simple alias to list still consumes a tracked generator."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "collect = list\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "collect(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_allows_function_rebinding_over_old_lambda(self) -> None:
        """A later function definition replaces an earlier deferred lambda binding."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "f = lambda: os.environ.setdefault('FLAG', '1')\n"
            "def f():\n"
            "    return 1\n"
            "f()\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_import_rebinding_over_old_lambda(self) -> None:
        """An import rebinding replaces an earlier deferred lambda binding."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "f = lambda: os.environ.setdefault('FLAG', '1')\n"
            "from math import sqrt as f\n"
            "f(1)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_saved_map_consumption(self) -> None:
        """A saved map iterator executes its lambda when consumed later."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "mapped = map(lambda _: os.environ.setdefault('FLAG', '1'), [1])\n"
            "list(mapped)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_preserves_deferred_binding_across_conditionals(self) -> None:
        """A conditional overwrite must not erase a generator on the other path."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "flag = get_flag()\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "if flag:\n"
            "    values = []\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_attribute_generator_consumption(self) -> None:
        """A generator stored in an attribute remains associated with its consumer."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "class Box:\n"
            "    pass\n"
            "holder = Box()\n"
            "holder.values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(holder.values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_producer_alias_consumption(self) -> None:
        """Aliases of map/filter/zip retain lazy producer semantics."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "make = map\n"
            "values = make(lambda _: os.environ.setdefault('FLAG', '1'), [1])\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_deferred_decorator_alias(self) -> None:
        """An aliased lambda decorator executes while defining the decorated function."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "decorate = lambda fn: os.environ.setdefault('FLAG', '1')\n"
            "@decorate\n"
            "def helper():\n"
            "    return 1\n"
            "__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_allows_non_consumed_consumer_keywords(self) -> None:
        """Consumer defaults and stored keyword callbacks are not invoked implicitly."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "dict(callback=lambda: os.environ.setdefault('A', '1'))\n"
            "next(iter([1]), lambda: os.environ.setdefault('B', '1'))\n"
            "sum([1], lambda: os.environ.setdefault('C', '1'))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_additional_eager_consumers(self) -> None:
        """bytes, bytearray, and frozenset consume generator bodies immediately."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "bytes(os.environ.setdefault('A', '1') for _ in [1])\n"
            "bytearray(os.environ.setdefault('B', '1') for _ in [1])\n"
            "frozenset(os.environ.setdefault('C', '1') for _ in [1])\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_producer_tuple_unpacking(self) -> None:
        """Tuple unpacking consumes a direct lazy producer during import."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "first, = map(lambda _: os.environ.setdefault('FLAG', '1'), [1])\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_producer_alias_tuple_unpacking(self) -> None:
        """Tuple unpacking consumes an aliased lazy producer during import."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "make = map\n"
            "first, = make(lambda _: os.environ.setdefault('FLAG', '1'), [1])\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_enumerate_consumption(self) -> None:
        """Enumerate preserves consumption of a deferred generator."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(enumerate(values))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_itertools_chain_consumption(self) -> None:
        """itertools.chain preserves consumption of a deferred generator."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import itertools\n"
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(itertools.chain(values))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_preserves_binding_through_zero_iteration_while(self) -> None:
        """A while body may not run, so its overwrite cannot erase a deferred value."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "flag = False\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "while flag:\n"
            "    values = []\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_preserves_binding_through_zero_iteration_for(self) -> None:
        """An empty for iterable leaves the prior deferred value available."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "for _ in []:\n"
            "    values = []\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_preserves_binding_through_failed_try_assignment(self) -> None:
        """An assignment that raises before completion leaves the old binding intact."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "try:\n"
            "    values = 1 / 0\n"
            "except ZeroDivisionError:\n"
            "    pass\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_allows_deterministic_if_overwrite(self) -> None:
        """A definitely executed overwrite removes the old deferred binding."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "if True:\n"
            "    values = []\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_consumed_lambda_returning_generator(self) -> None:
        """Consuming a lambda result continues into a generator returned by that lambda."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "list((lambda: (os.environ.setdefault('FLAG', '1') for _ in [1]))())\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_allows_attribute_binding_after_base_rebind(self) -> None:
        """A base-object rebind invalidates deferred attributes from the old object."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "class Box:\n"
            "    values = []\n"
            "generator = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "holder = Box()\n"
            "holder.values = generator\n"
            "holder = Box()\n"
            "list(holder.values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_shadowed_consumer_name(self) -> None:
        """A rebound builtin name is an ordinary callable, not a consumer."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "list = lambda value: value\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_rejects_starred_generator_consumption(self) -> None:
        """A starred tuple passed to list still exposes its deferred generator."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(*(values,))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_comprehension_target_environment_write(self) -> None:
        """Comprehension target assignment can mutate the environment at import time."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "[value for os.environ['FLAG'] in [1]]\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_exported_class_generator_consumption(self) -> None:
        """A deferred class attribute remains consumable through the class object."""
        base = "class Box:\n    values = []\n__all__ = ['Box']\n"
        head = (
            "import os\n"
            "class Box:\n"
            "    values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(Box.values)\n"
            "__all__ = ['Box']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_aliased_itertools_chain_consumption(self) -> None:
        """An aliased itertools module still exposes chain consumption."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import itertools as it\n"
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(it.chain(values))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_same_name_deferred_decorator(self) -> None:
        """A decorator evaluates the previous same-name binding before rebinding."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "helper = lambda fn: os.environ.setdefault('FLAG', '1')\n"
            "@helper\n"
            "def helper():\n"
            "    return 1\n"
            "__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_conditional_generator_consumption(self) -> None:
        """Conditional and boolean wrappers preserve deferred consumption."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "list(values if flag else ())\n"
            "list(values or ())\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual(
            ["NAMESPACE_CONTRACT_CHANGED Scripts/smoke/foxrun/fixture.py"],
            self._compatibility_errors(base, head),
        )

    def test_compatibility_rejects_unsupported_walrus_binding(self) -> None:
        """Unsupported module walrus bindings fail closed before identity comparison."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "list(x := lambda: os.environ.setdefault('FLAG', '1'))\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        with self.assertRaises(ValueError):
            self._compatibility_errors(base, head)

    def test_compatibility_allows_definite_for_overwrite(self) -> None:
        """A non-empty literal loop definitely replaces the deferred binding."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "for _ in [1]:\n"
            "    values = []\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))

    def test_compatibility_allows_definite_try_overwrite(self) -> None:
        """A simple literal assignment in try cannot take the exception path."""
        base = "def helper():\n    return 1\n__all__ = ['helper']\n"
        head = (
            "import os\n"
            "values = (os.environ.setdefault('FLAG', '1') for _ in [1])\n"
            "try:\n"
            "    values = []\n"
            "except Exception:\n"
            "    pass\n"
            "list(values)\n"
            "def helper():\n    return 1\n__all__ = ['helper']\n"
        )
        self.assertEqual([], self._compatibility_errors(base, head))
