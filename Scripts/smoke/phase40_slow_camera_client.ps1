param(
    [int]$AdvertiseTimeoutSeconds = 10,
    [int]$FallbackMaxChannelId = 128,
    [int]$HoldSeconds = 0,
    [switch]$NoFallback
)

$ErrorActionPreference = "Stop"

function Read-Exact($stream, [int]$count) {
    $buffer = New-Object byte[] $count
    $offset = 0
    while ($offset -lt $count) {
        $n = $stream.Read($buffer, $offset, $count - $offset)
        if ($n -le 0) {
            throw "Socket closed while reading $count byte(s)."
        }
        $offset += $n
    }
    return $buffer
}

function Read-UInt16BE([byte[]]$bytes) {
    return (($bytes[0] -shl 8) -bor $bytes[1])
}

function Read-UInt64BE([byte[]]$bytes) {
    [UInt64]$value = 0
    foreach ($b in $bytes) {
        $value = ($value -shl 8) -bor [UInt64]$b
    }
    return $value
}

function Read-ServerFrame($stream) {
    $h = Read-Exact $stream 2
    $fin = (($h[0] -band 0x80) -ne 0)
    $opcode = ($h[0] -band 0x0f)
    $masked = (($h[1] -band 0x80) -ne 0)
    [UInt64]$len = ($h[1] -band 0x7f)

    if ($len -eq 126) {
        $len = [UInt64](Read-UInt16BE (Read-Exact $stream 2))
    } elseif ($len -eq 127) {
        $len = Read-UInt64BE (Read-Exact $stream 8)
    }

    if ($len -gt [int]::MaxValue) {
        throw "Frame too large for this smoke script: $len"
    }

    $mask = $null
    if ($masked) {
        $mask = Read-Exact $stream 4
    }

    $payload = Read-Exact $stream ([int]$len)
    if ($masked) {
        for ($i = 0; $i -lt $payload.Length; $i++) {
            $payload[$i] = [byte]($payload[$i] -bxor $mask[$i % 4])
        }
    }

    return [pscustomobject]@{
        Fin = $fin
        Opcode = $opcode
        Payload = $payload
        Text = if ($opcode -eq 1 -or $opcode -eq 0) { [Text.Encoding]::UTF8.GetString($payload) } else { $null }
    }
}

function Find-ChannelIdInAdvertiseText([string]$text, [string]$topic) {
    if ([string]::IsNullOrEmpty($text)) {
        return $null
    }

    $escapedTopic = [regex]::Escape($topic)

    $idBeforeTopic = [regex]::Match(
        $text,
        '\{[^{}]*"id"\s*:\s*(\d+)[^{}]*"topic"\s*:\s*"' + $escapedTopic + '"')
    if ($idBeforeTopic.Success) {
        return [int]$idBeforeTopic.Groups[1].Value
    }

    $topicBeforeId = [regex]::Match(
        $text,
        '\{[^{}]*"topic"\s*:\s*"' + $escapedTopic + '"[^{}]*"id"\s*:\s*(\d+)')
    if ($topicBeforeId.Success) {
        return [int]$topicBeforeId.Groups[1].Value
    }

    return $null
}

function Send-MaskedTextFrame($stream, [string]$text) {
    $payload = [Text.Encoding]::UTF8.GetBytes($text)
    $mask = [byte[]](0x12, 0x34, 0x56, 0x78)
    $header = New-Object 'System.Collections.Generic.List[byte]'
    $header.Add([byte]0x81)

    if ($payload.Length -le 125) {
        $header.Add([byte](0x80 -bor $payload.Length))
    } elseif ($payload.Length -le 65535) {
        $header.Add([byte](0x80 -bor 126))
        $header.Add([byte](($payload.Length -shr 8) -band 0xff))
        $header.Add([byte]($payload.Length -band 0xff))
    } else {
        throw "Subscribe payload is unexpectedly large."
    }

    foreach ($b in $mask) {
        $header.Add($b)
    }

    $masked = New-Object byte[] $payload.Length
    for ($i = 0; $i -lt $payload.Length; $i++) {
        $masked[$i] = [byte]($payload[$i] -bxor $mask[$i % 4])
    }

    $h = $header.ToArray()
    $stream.Write($h, 0, $h.Length)
    $stream.Write($masked, 0, $masked.Length)
}

$client = New-Object System.Net.Sockets.TcpClient
$client.NoDelay = $true
$client.Connect("127.0.0.1", 8765)
$stream = $client.GetStream()

$key = [Convert]::ToBase64String([Guid]::NewGuid().ToByteArray())
$req = "GET / HTTP/1.1`r`nHost: 127.0.0.1:8765`r`nUpgrade: websocket`r`nConnection: Upgrade`r`nSec-WebSocket-Key: $key`r`nSec-WebSocket-Version: 13`r`nSec-WebSocket-Protocol: foxglove.sdk.v1`r`n`r`n"
$bytes = [Text.Encoding]::ASCII.GetBytes($req)
$stream.Write($bytes, 0, $bytes.Length)

$headerBytes = New-Object 'System.Collections.Generic.List[byte]'
while ($true) {
    $b = $stream.ReadByte()
    if ($b -lt 0) {
        throw "Socket closed during handshake."
    }
    $headerBytes.Add([byte]$b)
    $s = [Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
    if ($s.Contains("`r`n`r`n")) {
        break
    }
}

$response = [Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
if (-not $response.StartsWith("HTTP/1.1 101")) {
    throw "WebSocket handshake failed: $response"
}

Write-Host "Handshake accepted. Waiting for /unity/camera advertise..."

$cameraChannelId = $null
$advertiseText = New-Object System.Text.StringBuilder
$deadline = (Get-Date).AddSeconds($AdvertiseTimeoutSeconds)
while ((Get-Date) -lt $deadline -and $cameraChannelId -eq $null) {
    $frame = Read-ServerFrame $stream
    if (($frame.Opcode -ne 1 -and $frame.Opcode -ne 0) -or [string]::IsNullOrEmpty($frame.Text)) {
        continue
    }

    [void]$advertiseText.Append($frame.Text)
    $foundChannelId = Find-ChannelIdInAdvertiseText $advertiseText.ToString() "/unity/camera"
    if ($foundChannelId -ne $null) {
        $cameraChannelId = $foundChannelId
        break
    }
}

if ($cameraChannelId -eq $null -and $NoFallback) {
    throw "Did not see /unity/camera advertise within $AdvertiseTimeoutSeconds seconds. Confirm the camera publisher is enabled and publishing."
}

if ($cameraChannelId -ne $null) {
    $subscriptions = @(
        @{
            id = 9040
            channelId = $cameraChannelId
        }
    )
    Write-Host "Found /unity/camera on channel $cameraChannelId."
} else {
    $subscriptions = 1..$FallbackMaxChannelId | ForEach-Object {
        @{
            id = 904000 + $_
            channelId = $_
        }
    }
    Write-Host "Did not see /unity/camera advertise within $AdvertiseTimeoutSeconds seconds."
    Write-Host "Falling back to broad subscription for channel IDs 1..$FallbackMaxChannelId."
}

$subscribe = @{
    op = "subscribe"
    subscriptions = $subscriptions
} | ConvertTo-Json -Depth 5 -Compress

Send-MaskedTextFrame $stream $subscribe

if ($cameraChannelId -ne $null) {
    Write-Host "Subscribed to /unity/camera on channel $cameraChannelId."
} else {
    Write-Host "Subscribed to channel range 1..$FallbackMaxChannelId."
}
Write-Host "This client will now stop reading. Leave this window open for 60-120 seconds; press Ctrl+C to stop."

if ($HoldSeconds -gt 0) {
    Start-Sleep -Seconds $HoldSeconds
    Write-Host "HoldSeconds elapsed; closing smoke client."
} else {
    while ($true) {
        Start-Sleep -Seconds 10
    }
}
