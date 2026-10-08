from __future__ import annotations

from .foundation import *


class RemoteGatewayEnvironmentTests:
    """Exercise device-token inheritance boundaries"""

    def test_token_is_trimmed_before_it_is_inherited(self) -> None:
        """Whitespace used for validation cannot survive into Unity's token."""
        with mock.patch.dict(
            os.environ,
            {self.acceptance.TOKEN_ENV: "  device-token  "},
            clear=False,
        ):
            self.acceptance.ensure_token_env()
            self.assertEqual(
                "device-token",
                os.environ[self.acceptance.TOKEN_ENV],
            )

    def test_native_acceptance_build_does_not_inherit_device_token(self) -> None:
        """The Cargo build child must not receive the Unity Cloud credential."""
        sentinel = "phase187-d05-nonsecret"
        with mock.patch.dict(
            os.environ,
            {self.acceptance.TOKEN_ENV: sentinel},
            clear=False,
        ):
            with mock.patch.object(self.acceptance.subprocess, "run") as run:
                self.acceptance.build_and_copy_native()

            child_environment = run.call_args.kwargs["env"]
            self.assertNotIn(
                self.acceptance.TOKEN_ENV,
                child_environment,
                "native build child environment must not contain the device token",
            )
            self.assertEqual(sentinel, os.environ[self.acceptance.TOKEN_ENV])

    def test_direct_native_build_environment_does_not_inherit_device_token(self) -> None:
        """Direct invocations of the native build helper apply the same boundary."""
        sentinel = "phase187-d05-direct-build"
        arguments = SimpleNamespace(libclang_path=None, target_dir="phase187-target")
        with mock.patch.dict(
            os.environ,
            {"FOXGLOVE_DEVICE_TOKEN": sentinel},
            clear=False,
        ):
            child_environment = self.build.build_environment(arguments)

        self.assertNotIn(
            "FOXGLOVE_DEVICE_TOKEN",
            child_environment,
            "direct native build child environment must not contain the device token",
        )


__all__ = ["RemoteGatewayEnvironmentTests"]
