from .foundation import *
from .surface_rows_tests import SurfaceRowsTests
from .split_surface_tests import SplitSurfaceTests
from .split_contract_tests import SplitContractTests
from .compatibility_surface_tests import CompatibilitySurfaceTests
from .compatibility_initializer_tests import CompatibilityInitializerTests
from .loader_isolation_tests import LoaderIsolationTests
from .loader_entrypoint_tests import LoaderEntrypointTests
from .loader_mutation_tests import LoaderMutationTests
from .namespace_risk_tests import NamespaceRiskTests
from .namespace_execution_core_tests import NamespaceExecutionCoreTests
from .namespace_execution_edges_tests import NamespaceExecutionEdgesTests


class IdentityToolingTests(
    SurfaceRowsTests,
    SplitSurfaceTests,
    SplitContractTests,
    CompatibilitySurfaceTests,
    CompatibilityInitializerTests,
    LoaderIsolationTests,
    LoaderEntrypointTests,
    LoaderMutationTests,
    NamespaceRiskTests,
    NamespaceExecutionCoreTests,
    NamespaceExecutionEdgesTests,
):
    """Compatibility aggregate preserving the original test class and names."""


__all__ = ["IdentityToolingTests"]
