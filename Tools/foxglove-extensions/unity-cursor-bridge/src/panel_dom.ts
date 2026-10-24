// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

import type { PanelExtensionContext } from "@foxglove/extension";
import { PanelState, tokenEndpointWarningMessage } from "./helpers";

export function buildPanelDom(state: PanelState, canFollow: boolean): {
  root: HTMLDivElement;
  enabledInput: HTMLInputElement;
  endpointInput: HTMLInputElement;
  tokenInput: HTMLInputElement;
  maxHzInput: HTMLInputElement;
  followInput: HTMLInputElement | undefined;
  tokenWarning: HTMLSpanElement;
  replayTime: HTMLSpanElement;
  unityStatus: HTMLSpanElement;
} {
  const root = document.createElement("div");
  root.innerHTML = `
    <style>
      .bridge-panel {
        box-sizing: border-box;
        color: #f3f4f6;
        display: grid;
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
        gap: 14px;
        line-height: 1.35;
        padding: 14px;
      }

      .bridge-sync {
        align-items: center;
        border: 1px solid #343942;
        border-radius: 6px;
        display: grid;
        gap: 10px;
        grid-template-columns: auto 1fr;
        padding: 10px;
      }

      .bridge-sync input {
        height: 16px;
        margin: 0;
        width: 16px;
      }

      .bridge-sync span {
        font-size: 14px;
        font-weight: 600;
      }

      .bridge-field {
        display: grid;
        gap: 6px;
      }

      .bridge-field label {
        color: #d1d5db;
        font-size: 12px;
        font-weight: 600;
      }

      .bridge-field input {
        background: #111318;
        border: 1px solid #3a404a;
        border-radius: 4px;
        box-sizing: border-box;
        color: #f9fafb;
        font: 13px ui-monospace, "SFMono-Regular", Consolas, monospace;
        min-width: 0;
        padding: 7px 8px;
        width: 100%;
      }

      .bridge-readout {
        display: grid;
        gap: 8px;
      }

      .bridge-row {
        align-items: start;
        display: grid;
        gap: 8px;
        grid-template-columns: 88px 1fr;
      }

      .bridge-label {
        color: #aeb4bd;
        font-size: 12px;
        font-weight: 600;
      }

      .bridge-value {
        color: #f3f4f6;
        font: 12px ui-monospace, "SFMono-Regular", Consolas, monospace;
        min-width: 0;
        overflow-wrap: anywhere;
      }

      .bridge-status {
        border-radius: 4px;
        display: inline;
        font: 12px ui-monospace, "SFMono-Regular", Consolas, monospace;
        overflow-wrap: anywhere;
      }

      .bridge-status.ok {
        color: #22c55e;
      }

      .bridge-status.error {
        color: #f87171;
      }

      .bridge-warning {
        color: #fbbf24;
        font-size: 12px;
        min-height: 16px;
        overflow-wrap: anywhere;
      }
    </style>
    <div class="bridge-panel">
      <label class="bridge-sync">
        <input id="enabled" type="checkbox" />
        <span>Sync Foxglove timeline to Unity</span>
      </label>
      <div class="bridge-field">
        <label for="endpoint">Unity endpoint</label>
        <input id="endpoint" />
      </div>
      <div class="bridge-field">
        <label for="token">Access token (optional)</label>
        <input id="token" type="password" />
        <span id="token-warning" class="bridge-warning"></span>
      </div>
      <div class="bridge-field">
        <label for="maxhz">Cursor rate (Hz)</label>
        <input id="maxhz" type="number" min="1" max="120" step="1" />
      </div>
      ${canFollow ? `
      <label class="bridge-sync">
        <input id="follow" type="checkbox" />
        <span>Follow Unity replay</span>
      </label>` : ""}
      <div class="bridge-readout">
        <div class="bridge-row">
          <span class="bridge-label">Replay time (UTC)</span>
          <span id="replay-time" class="bridge-value"></span>
        </div>
        <div class="bridge-row">
          <span class="bridge-label">Unity status</span>
          <span id="unity-status" class="bridge-status"></span>
        </div>
      </div>
    </div>
  `;

  const enabledInput = root.querySelector<HTMLInputElement>("#enabled");
  const endpointInput = root.querySelector<HTMLInputElement>("#endpoint");
  const tokenInput = root.querySelector<HTMLInputElement>("#token");
  const maxHzInput = root.querySelector<HTMLInputElement>("#maxhz");
  const followInput = canFollow ? root.querySelector<HTMLInputElement>("#follow") ?? undefined : undefined;
  const tokenWarning = root.querySelector<HTMLSpanElement>("#token-warning");
  const replayTime = root.querySelector<HTMLSpanElement>("#replay-time");
  const unityStatus = root.querySelector<HTMLSpanElement>("#unity-status");
  if (
    enabledInput == undefined ||
    endpointInput == undefined ||
    tokenInput == undefined ||
    maxHzInput == undefined ||
    tokenWarning == undefined ||
    replayTime == undefined ||
    unityStatus == undefined ||
    (canFollow && followInput == undefined)
  ) {
    throw new Error("Unity Replay Sync panel template is missing required elements.");
  }

  enabledInput.checked = state.enabled;
  endpointInput.value = state.endpoint;
  tokenInput.value = state.token;
  maxHzInput.value = String(state.maxHz);
  if (followInput != undefined) {
    followInput.checked = state.followUnity;
  }
  tokenWarning.textContent = tokenEndpointWarningMessage(state.endpoint, state.token);
  return { root, enabledInput, endpointInput, tokenInput, maxHzInput, followInput, tokenWarning, replayTime, unityStatus };
}
