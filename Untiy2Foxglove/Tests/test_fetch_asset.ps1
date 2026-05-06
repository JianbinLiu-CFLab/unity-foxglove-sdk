# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Manual fetchAsset smoke test — connects to the running Unity
# WebSocket server and requests a file by URI.
# Usage: pwsh -File Untiy2Foxglove/Tests/test_fetch_asset.ps1
# Prerequisites: Unity is in Play Mode with FoxgloveManager and asset roots configured.
# Requires PowerShell 7+ for ClientWebSocket support.

param($Port = 8765)

try {
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $ws.Options.AddSubProtocol("foxglove.sdk.v1")
} catch {
    Write-Host "ERROR: Your PowerShell version does not support ClientWebSocket."
    Write-Host "Use PowerShell 7+ or run in bash with dotnet script."
    return
}

Write-Host "Connecting..."
$ws.ConnectAsync([Uri]"ws://127.0.0.1:${Port}/", [System.Threading.CancellationToken]::None).Wait(5000)
Write-Host "Connected: $($ws.State)"

$buf = [byte[]]::new(131072)
$seg = [System.ArraySegment[byte]]::new($buf)

# Drain initial messages
Write-Host "Draining initial messages..."
for ($i = 0; $i -lt 10; $i++) {
    $task = $ws.ReceiveAsync($seg, [System.Threading.CancellationToken]::None)
    if ($task.Wait(200)) {
        $msg = [System.Text.Encoding]::UTF8.GetString($buf, 0, $task.Result.Count)
        $op = ""
        try { $j = ConvertFrom-Json $msg; $op = $j.op } catch {}
        Write-Host "  Drained: op=$op type=$($task.Result.MessageType)"
        if ($task.Result.MessageType -eq "Close") { Write-Host "Connection closed early"; return }
    } else { Write-Host "  No more messages"; break }
}

# Send fetchAsset request
$req = '{"op":"fetchAsset","requestId":42,"uri":"asset://demo/Scripts/FoxgloveDemoSetup.cs"}'
$reqBytes = [System.Text.Encoding]::UTF8.GetBytes($req)
Write-Host "`nSending fetchAsset: $req"
$ws.SendAsync(([System.ArraySegment[byte]]::new($reqBytes)), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).Wait(2000)
Write-Host "Sent OK"

# Read response
Write-Host "`nWaiting for fetchAsset response..."
for ($attempt = 0; $attempt -lt 5; $attempt++) {
    $task = $ws.ReceiveAsync($seg, [System.Threading.CancellationToken]::None)
    if ($task.Wait(5000)) {
        if ($task.Result.MessageType -eq "Binary") {
            $data = [byte[]]::new($task.Result.Count)
            [Array]::Copy($buf, 0, $data, 0, $task.Result.Count)
            $opcode = $data[0]
            $requestId = [BitConverter]::ToUInt32($data, 1)
            $status = $data[5]
            $errorLen = [BitConverter]::ToUInt32($data, 6)
            Write-Host "Binary: opcode=$opcode requestId=$requestId status=$status errorLen=$errorLen"
            if ($status -eq 0) {
                $plen = $task.Result.Count - 10
                $payload = [System.Text.Encoding]::UTF8.GetString($data, 10, $plen)
                if ($payload -match "FoxgloveDemoSetup") {
                    Write-Host "[PASS] fetchAsset SUCCESS - got $plen bytes, content matches"
                    # Save the fetched file to disk for inspection
                    $outPath = Join-Path $PSScriptRoot "fetched_demo.cs"
                    [IO.File]::WriteAllBytes($outPath, $data[10..($data.Length - 1)])
                    Write-Host "  Saved to: $outPath"
                } else {
                    Write-Host "[CHECK] Got $plen bytes: $($payload.Substring(0, [Math]::Min(120, $payload.Length)))..."
                }
            } else {
                $errMsg = [System.Text.Encoding]::UTF8.GetString($data, 10, $errorLen)
                Write-Host "[FAIL] Server error: $errMsg"
            }
            break
        } elseif ($task.Result.MessageType -eq "Text") {
            $txt = [System.Text.Encoding]::UTF8.GetString($buf, 0, $task.Result.Count)
            Write-Host "Text (draining): $($txt.Substring(0, [Math]::Min(100, $txt.Length)))..."
        } else {
            Write-Host "Received: $($task.Result.MessageType)"
            if ($task.Result.MessageType -eq "Close") { break }
        }
    } else { Write-Host "Timeout waiting for response"; break }
}

$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "", [System.Threading.CancellationToken]::None).Wait(1000)
Write-Host "Done"
