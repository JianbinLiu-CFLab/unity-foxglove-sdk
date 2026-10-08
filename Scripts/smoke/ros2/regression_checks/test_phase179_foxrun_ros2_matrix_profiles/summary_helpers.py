from __future__ import annotations

from .foundation import *


class Phase179SummaryHelpers:
    """Build normalized role summaries used by acceptance tests"""

    @staticmethod
    def _linux_summary(profile, *, surface: str, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Linux half-summary without a ROS2 installation."""

        messages = []
        for name in profile.message_set:
            spec = load_profile_module().inbound.MESSAGE_SPECS[name]
            messages.append(
                {
                    "name": name,
                    "topic": f"{profile.topic_prefix}/{name}",
                    "published": True,
                    "graph": {
                        "messageType": spec.message_type,
                        "subscriptionCount": 1,
                        "qosReliability": spec.qos_reliability,
                        "qosHistory": spec.qos_history,
                        "qosDepth": spec.qos_depth,
                        "qosDurability": spec.qos_durability,
                    },
                }
            )
        summary: dict[str, object] = {
            "phase": 179,
            "role": "linux-ros2-peer",
            "profileId": profile.profile_id,
            "surface": surface,
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "unityLogProvided": False,
            "messageResults": messages,
            "verdict": "PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary

    @staticmethod
    def _player_summary(profile, *, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Windows Player half-summary without launching Unity."""

        summary: dict[str, object] = {
            "phase": 179,
            "role": "windows-player-host",
            "profileId": profile.profile_id,
            "surface": "player",
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "ready": True,
            "allRequiredApplied": True,
            "playerExitCode": 0,
            "verdict": "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary

    @staticmethod
    def _editor_summary(profile, *, token: str, topology_id: str | None = None) -> dict[str, object]:
        """Build one complete synthetic Windows Editor half-summary with bounded copied-value evidence."""

        messages = []
        for name in profile.message_set:
            spec = load_profile_module().inbound.MESSAGE_SPECS[name]
            messages.append(
                {
                    "name": name,
                    "topic": f"{profile.topic_prefix}/{name}",
                    "received": 1,
                    "applied": 1,
                    "replaced": 0,
                    "value": spec.expected_value(token),
                }
            )
        summary: dict[str, object] = {
            "phase": 179,
            "role": "windows-editor-host",
            "profileId": profile.profile_id,
            "surface": "editor",
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "domainId": 0,
            "discoveryRange": "SUBNET",
            "token": token,
            "topicPrefix": profile.topic_prefix,
            "messageSet": list(profile.message_set),
            "ready": True,
            "allRequiredApplied": True,
            "messageResults": messages,
            "verdict": "WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING",
        }
        if topology_id is not None:
            summary["zenohTopologyId"] = topology_id
        return summary


__all__ = ["Phase179SummaryHelpers"]
