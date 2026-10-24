// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tools/foxglove-extensions/unity-cursor-bridge
// Purpose: Minimal Foxglove panel entrypoint and compatibility facade.

import type { ExtensionContext } from "@foxglove/extension";
import { initPanel } from "./panel";

export {
  buildPayload,
  cloneTimeIfChanged,
  escapeHtml,
  formatReplayTimeUtc,
  isBeforeTime,
  readPanelState,
  sendCursor,
  shouldSendCursor,
  summarizeResponseText,
  tokenEndpointWarningMessage,
} from "./helpers";
export { initPanel };

export function activate(extensionContext: ExtensionContext): void {
  extensionContext.registerPanel({
    name: "Unity Replay Sync",
    initPanel,
  });
}
