from .foundation import *

class LocalEntrypointValidatorTests(unittest.TestCase):
    """Regression coverage for machine-local path detection boundaries."""

    def setUp(self) -> None:
        """Load a fresh local-entrypoint validator for each test."""
        self.validator = load_module(
            "validate_local_entrypoints_under_test",
            LOCAL_ENTRYPOINT_VALIDATOR_PATH,
        )

    def test_release_asset_rule_allows_host_allowlists_but_rejects_concrete_urls(self) -> None:
        """A trusted host constant is not itself a temporary signed asset URL."""
        pattern = next(
            pattern
            for label, pattern in self.validator.FORBIDDEN_PATTERNS
            if label == "temporary GitHub signed release asset URL"
        )

        self.assertIsNone(pattern.search('"release-assets.githubusercontent.com",'))
        self.assertIsNotNone(
            pattern.search(
                "https://release-assets.githubusercontent.com/"
                "github-production-release-asset/431693744/object?sig=opaque"
            )
        )

    def test_git_grep_excludes_regression_fixtures(self) -> None:
        """Intentional invalid-path fixtures must not be treated as production defaults."""
        process = mock.Mock()
        process.returncode = 1
        process.communicate.return_value = ("", "")
        with mock.patch.object(
            self.validator.subprocess,
            "Popen",
            return_value=process,
        ) as popen:
            self.assertEqual(
                [],
                self.validator.git_grep_failures(
                    "temporary GitHub signed release asset URL",
                    self.validator.FORBIDDEN_PATTERNS[-1][1],
                ),
            )

        command = popen.call_args.args[0]
        self.assertIn(
            ":(exclude,glob)Scripts/**/regression_checks/**/*.py",
            command,
        )

LocalEntrypointValidatorTests.__module__ = 'Scripts.release.regression_checks.test_release_tooling'

__all__ = ["LocalEntrypointValidatorTests"]
