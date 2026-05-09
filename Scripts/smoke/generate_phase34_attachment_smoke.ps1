param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not ("Phase34Crc32" -as [type])) {
    Add-Type -TypeDefinition @"
using System;

public static class Phase34Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        for (var i = 0; i < data.Length; i++)
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
"@
}

function Assert-Phase34Smoke {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function New-Bytes {
    param([System.IO.MemoryStream]$Stream)
    return $Stream.ToArray()
}

function Join-Bytes {
    param([byte[]]$First, [byte[]]$Second)
    $joined = New-Object byte[] ($First.Length + $Second.Length)
    [Array]::Copy($First, 0, $joined, 0, $First.Length)
    [Array]::Copy($Second, 0, $joined, $First.Length, $Second.Length)
    return $joined
}

function Write-Bytes {
    param([System.IO.Stream]$Stream, [byte[]]$Bytes)
    if ($null -ne $Bytes -and $Bytes.Length -gt 0) {
        $Stream.Write($Bytes, 0, $Bytes.Length)
    }
}

function Write-U16 {
    param([System.IO.Stream]$Stream, [UInt16]$Value)
    Write-Bytes $Stream ([BitConverter]::GetBytes($Value))
}

function Write-U32 {
    param([System.IO.Stream]$Stream, [UInt32]$Value)
    Write-Bytes $Stream ([BitConverter]::GetBytes($Value))
}

function Write-U64 {
    param([System.IO.Stream]$Stream, [UInt64]$Value)
    Write-Bytes $Stream ([BitConverter]::GetBytes($Value))
}

function Write-StringField {
    param([System.IO.Stream]$Stream, [string]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($(if ($null -eq $Value) { "" } else { $Value }))
    Write-U32 $Stream ([UInt32]$bytes.Length)
    Write-Bytes $Stream $bytes
}

function Write-PrefixedBytes {
    param([System.IO.Stream]$Stream, [byte[]]$Bytes)
    $length = if ($null -eq $Bytes) { 0 } else { $Bytes.Length }
    Write-U32 $Stream ([UInt32]$length)
    Write-Bytes $Stream $Bytes
}

function New-RecordBytes {
    param([byte]$Opcode, [byte[]]$Content)
    $ms = New-Object System.IO.MemoryStream
    $ms.WriteByte($Opcode)
    Write-U64 $ms ([UInt64]$(if ($null -eq $Content) { 0 } else { $Content.Length }))
    Write-Bytes $ms $Content
    return New-Bytes $ms
}

function New-HeaderContent {
    $ms = New-Object System.IO.MemoryStream
    Write-StringField $ms ""
    Write-StringField $ms "unity-foxglove-sdk phase34 smoke"
    return New-Bytes $ms
}

function New-SchemaContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U16 $ms 1
    Write-StringField $ms "phase34.Smoke"
    Write-StringField $ms "jsonschema"
    Write-PrefixedBytes $ms ([Text.Encoding]::UTF8.GetBytes('{"type":"object","properties":{"seq":{"type":"integer"},"message":{"type":"string"}}}'))
    return New-Bytes $ms
}

function New-ChannelContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U16 $ms 1
    Write-U16 $ms 1
    Write-StringField $ms "/phase34/smoke"
    Write-StringField $ms "json"
    Write-U32 $ms 0
    return New-Bytes $ms
}

function New-MessageContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U16 $ms 1
    Write-U32 $ms 1
    Write-U64 $ms 1000000000
    Write-U64 $ms 1000000000
    Write-Bytes $ms ([Text.Encoding]::UTF8.GetBytes('{"seq":1,"message":"phase34 smoke"}'))
    return New-Bytes $ms
}

function New-MessageIndexContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U16 $ms 1
    Write-U32 $ms 16
    Write-U64 $ms 1000000000
    Write-U64 $ms 0
    return New-Bytes $ms
}

function New-ChunkContent {
    param([byte[]]$Records)
    $ms = New-Object System.IO.MemoryStream
    Write-U64 $ms 1000000000
    Write-U64 $ms 1000000000
    Write-U64 $ms ([UInt64]$Records.Length)
    Write-U32 $ms ([Phase34Crc32]::Compute($Records))
    Write-StringField $ms ""
    Write-U64 $ms ([UInt64]$Records.Length)
    Write-Bytes $ms $Records
    return New-Bytes $ms
}

function New-ChunkIndexContent {
    param(
        [UInt64]$ChunkOffset,
        [UInt64]$ChunkLength,
        [UInt64]$MessageIndexOffset,
        [UInt64]$MessageIndexLength,
        [UInt64]$CompressedSize,
        [UInt64]$UncompressedSize
    )
    $ms = New-Object System.IO.MemoryStream
    Write-U64 $ms 1000000000
    Write-U64 $ms 1000000000
    Write-U64 $ms $ChunkOffset
    Write-U64 $ms $ChunkLength
    Write-U32 $ms 10
    Write-U16 $ms 1
    Write-U64 $ms $MessageIndexOffset
    Write-U64 $ms $MessageIndexLength
    Write-StringField $ms ""
    Write-U64 $ms $CompressedSize
    Write-U64 $ms $UncompressedSize
    return New-Bytes $ms
}

function New-DataEndContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U32 $ms 0
    return New-Bytes $ms
}

function New-AttachmentContent {
    param([byte[]]$Data)
    $withoutCrc = New-Object System.IO.MemoryStream
    Write-U64 $withoutCrc 1500000000
    Write-U64 $withoutCrc 1500000000
    Write-StringField $withoutCrc "phase34_attachment_smoke.txt"
    Write-StringField $withoutCrc "text/plain"
    Write-U64 $withoutCrc ([UInt64]$Data.Length)
    Write-Bytes $withoutCrc $Data

    $withoutCrcBytes = New-Bytes $withoutCrc
    $withCrc = New-Object System.IO.MemoryStream
    Write-Bytes $withCrc $withoutCrcBytes
    Write-U32 $withCrc ([Phase34Crc32]::Compute($withoutCrcBytes))
    return New-Bytes $withCrc
}

function New-AttachmentIndexContent {
    param([UInt64]$AttachmentOffset, [UInt64]$AttachmentLength, [UInt64]$DataSize)
    $ms = New-Object System.IO.MemoryStream
    Write-U64 $ms $AttachmentOffset
    Write-U64 $ms $AttachmentLength
    Write-U64 $ms 1500000000
    Write-U64 $ms 1500000000
    Write-U64 $ms $DataSize
    Write-StringField $ms "phase34_attachment_smoke.txt"
    Write-StringField $ms "text/plain"
    return New-Bytes $ms
}

function New-StatisticsContent {
    $ms = New-Object System.IO.MemoryStream
    Write-U64 $ms 1
    Write-U16 $ms 1
    Write-U32 $ms 1
    Write-U32 $ms 1
    Write-U32 $ms 0
    Write-U32 $ms 1
    Write-U64 $ms 1000000000
    Write-U64 $ms 1000000000
    Write-U32 $ms 10
    Write-U16 $ms 1
    Write-U64 $ms 1
    return New-Bytes $ms
}

function New-SummaryOffsetContent {
    param([byte]$GroupOpcode, [UInt64]$GroupStart, [UInt64]$GroupLength)
    $ms = New-Object System.IO.MemoryStream
    $ms.WriteByte($GroupOpcode)
    Write-U64 $ms $GroupStart
    Write-U64 $ms $GroupLength
    return New-Bytes $ms
}

function New-FooterPrefix {
    param([UInt64]$SummaryStart, [UInt64]$SummaryOffsetStart)
    $ms = New-Object System.IO.MemoryStream
    $ms.WriteByte(0x02)
    Write-U64 $ms 20
    Write-U64 $ms $SummaryStart
    Write-U64 $ms $SummaryOffsetStart
    return New-Bytes $ms
}

function New-FooterContent {
    param([UInt64]$SummaryStart, [UInt64]$SummaryOffsetStart, [UInt32]$SummaryCrc)
    $ms = New-Object System.IO.MemoryStream
    Write-U64 $ms $SummaryStart
    Write-U64 $ms $SummaryOffsetStart
    Write-U32 $ms $SummaryCrc
    return New-Bytes $ms
}

function Read-U32 {
    param([byte[]]$Bytes, [ref]$Offset)
    $value = [BitConverter]::ToUInt32($Bytes, $Offset.Value)
    $Offset.Value += 4
    return $value
}

function Read-U64 {
    param([byte[]]$Bytes, [ref]$Offset)
    $value = [BitConverter]::ToUInt64($Bytes, $Offset.Value)
    $Offset.Value += 8
    return $value
}

function Read-StringField {
    param([byte[]]$Bytes, [ref]$Offset)
    $length = [int](Read-U32 $Bytes $Offset)
    $value = [Text.Encoding]::UTF8.GetString($Bytes, $Offset.Value, $length)
    $Offset.Value += $length
    return $value
}

function Test-SmokeFile {
    param([string]$Path, [byte[]]$ExpectedAttachmentData)

    $bytes = [IO.File]::ReadAllBytes($Path)
    $magic = [byte[]](0x89, [byte][char]'M', [byte][char]'C', [byte][char]'A', [byte][char]'P', 0x30, 0x0D, 0x0A)
    Assert-Phase34Smoke ($bytes.Length -gt 64) "Smoke MCAP is too small"
    for ($i = 0; $i -lt $magic.Length; $i++) {
        Assert-Phase34Smoke ($bytes[$i] -eq $magic[$i]) "Leading MCAP magic mismatch"
        Assert-Phase34Smoke ($bytes[$bytes.Length - $magic.Length + $i] -eq $magic[$i]) "Trailing MCAP magic mismatch"
    }

    $footerOffset = $bytes.Length - 8 - 1 - 8 - 20
    Assert-Phase34Smoke ($bytes[$footerOffset] -eq 0x02) "Footer opcode mismatch"
    $footerLength = [BitConverter]::ToUInt64($bytes, $footerOffset + 1)
    Assert-Phase34Smoke ($footerLength -eq 20) "Footer length mismatch"
    $footerContentOffset = [ref]($footerOffset + 9)
    $summaryStart = Read-U64 $bytes $footerContentOffset
    $summaryOffsetStart = Read-U64 $bytes $footerContentOffset
    $summaryCrc = Read-U32 $bytes $footerContentOffset
    Assert-Phase34Smoke ($summaryStart -gt 0) "summary_start is zero"
    Assert-Phase34Smoke ($summaryOffsetStart -gt $summaryStart) "summary_offset_start is not after summary_start"
    Assert-Phase34Smoke ($summaryCrc -ne 0) "summary_crc is zero"

    $summaryLength = $footerOffset - [int]$summaryStart
    $summaryBytes = New-Object byte[] $summaryLength
    [Array]::Copy($bytes, [int]$summaryStart, $summaryBytes, 0, $summaryLength)
    $footerPrefix = New-FooterPrefix $summaryStart $summaryOffsetStart
    $computedSummaryCrc = [Phase34Crc32]::Compute((Join-Bytes $summaryBytes $footerPrefix))
    Assert-Phase34Smoke ($computedSummaryCrc -eq $summaryCrc) "summary_crc mismatch"

    $attachmentIndex = $null
    $chunkIndex = $null
    $pos = [int]$summaryStart
    while ($pos -lt $footerOffset) {
        $op = $bytes[$pos]
        $recordLength = [int][BitConverter]::ToUInt64($bytes, $pos + 1)
        $contentStart = $pos + 9
        if ($op -eq 0x08) {
            $content = New-Object byte[] $recordLength
            [Array]::Copy($bytes, $contentStart, $content, 0, $recordLength)
            $contentOffset = [ref]0
            $chunkIndex = [pscustomobject]@{
                MessageStartTime = Read-U64 $content $contentOffset
                MessageEndTime = Read-U64 $content $contentOffset
                ChunkOffset = Read-U64 $content $contentOffset
                ChunkLength = Read-U64 $content $contentOffset
            }
        }
        elseif ($op -eq 0x0A) {
            $content = New-Object byte[] $recordLength
            [Array]::Copy($bytes, $contentStart, $content, 0, $recordLength)
            $contentOffset = [ref]0
            $attachmentIndex = [pscustomobject]@{
                Offset = Read-U64 $content $contentOffset
                Length = Read-U64 $content $contentOffset
                LogTime = Read-U64 $content $contentOffset
                CreateTime = Read-U64 $content $contentOffset
                DataSize = Read-U64 $content $contentOffset
                Name = Read-StringField $content $contentOffset
                MediaType = Read-StringField $content $contentOffset
            }
        }
        $pos = $contentStart + $recordLength
    }

    Assert-Phase34Smoke ($null -ne $chunkIndex) "ChunkIndex not found in summary"
    Assert-Phase34Smoke ($chunkIndex.MessageStartTime -eq 1000000000) "ChunkIndex start time mismatch"
    Assert-Phase34Smoke ($chunkIndex.MessageEndTime -eq 1000000000) "ChunkIndex end time mismatch"
    Assert-Phase34Smoke ($bytes[[int]$chunkIndex.ChunkOffset] -eq 0x06) "Chunk record not found at ChunkIndex offset"
    Assert-Phase34Smoke ($null -ne $attachmentIndex) "AttachmentIndex not found in summary"
    Assert-Phase34Smoke ($attachmentIndex.Name -eq "phase34_attachment_smoke.txt") "AttachmentIndex name mismatch"
    Assert-Phase34Smoke ($attachmentIndex.MediaType -eq "text/plain") "AttachmentIndex media type mismatch"
    Assert-Phase34Smoke ($attachmentIndex.DataSize -eq [UInt64]$ExpectedAttachmentData.Length) "AttachmentIndex data size mismatch"

    $attachmentOffset = [int]$attachmentIndex.Offset
    Assert-Phase34Smoke ($bytes[$attachmentOffset] -eq 0x09) "Attachment opcode mismatch"
    $attachmentRecordLength = [int][BitConverter]::ToUInt64($bytes, $attachmentOffset + 1)
    Assert-Phase34Smoke (($attachmentRecordLength + 9) -eq [int]$attachmentIndex.Length) "Attachment length mismatch"
    $attachmentContent = New-Object byte[] $attachmentRecordLength
    [Array]::Copy($bytes, $attachmentOffset + 9, $attachmentContent, 0, $attachmentRecordLength)

    $storedAttachmentCrc = [BitConverter]::ToUInt32($attachmentContent, $attachmentContent.Length - 4)
    $attachmentWithoutCrc = New-Object byte[] ($attachmentContent.Length - 4)
    [Array]::Copy($attachmentContent, 0, $attachmentWithoutCrc, 0, $attachmentWithoutCrc.Length)
    Assert-Phase34Smoke ([Phase34Crc32]::Compute($attachmentWithoutCrc) -eq $storedAttachmentCrc) "Attachment CRC mismatch"

    $attachmentContentOffset = [ref]0
    [void](Read-U64 $attachmentContent $attachmentContentOffset)
    [void](Read-U64 $attachmentContent $attachmentContentOffset)
    $name = Read-StringField $attachmentContent $attachmentContentOffset
    $mediaType = Read-StringField $attachmentContent $attachmentContentOffset
    $dataSize = [int](Read-U64 $attachmentContent $attachmentContentOffset)
    $actualData = New-Object byte[] $dataSize
    [Array]::Copy($attachmentContent, $attachmentContentOffset.Value, $actualData, 0, $dataSize)

    Assert-Phase34Smoke ($name -eq "phase34_attachment_smoke.txt") "Attachment name mismatch"
    Assert-Phase34Smoke ($mediaType -eq "text/plain") "Attachment media type mismatch"
    Assert-Phase34Smoke ([Text.Encoding]::UTF8.GetString($actualData) -eq [Text.Encoding]::UTF8.GetString($ExpectedAttachmentData)) "Attachment payload mismatch"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "build\test_mcap\phase34_attachment_smoke.mcap"
}

if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$outputDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}

$attachmentData = [Text.Encoding]::UTF8.GetBytes(
    "Phase 34 attachment smoke file`n" +
    "Purpose: verify MCAP Attachment, AttachmentIndex, attachment CRC, and summary CRC interop.`n")
$schemaRecord = New-RecordBytes 0x03 (New-SchemaContent)
$channelRecord = New-RecordBytes 0x04 (New-ChannelContent)
$chunkRecords = New-RecordBytes 0x05 (New-MessageContent)
$chunkRecord = New-RecordBytes 0x06 (New-ChunkContent $chunkRecords)
$messageIndexRecord = New-RecordBytes 0x07 (New-MessageIndexContent)
$dataEndRecord = New-RecordBytes 0x0F (New-DataEndContent)
$attachmentContent = New-AttachmentContent $attachmentData
$attachmentRecord = New-RecordBytes 0x09 $attachmentContent
$statisticsRecord = New-RecordBytes 0x0B (New-StatisticsContent)

$magic = [byte[]](0x89, [byte][char]'M', [byte][char]'C', [byte][char]'A', [byte][char]'P', 0x30, 0x0D, 0x0A)
$file = New-Object System.IO.MemoryStream
Write-Bytes $file $magic
Write-Bytes $file (New-RecordBytes 0x01 (New-HeaderContent))
Write-Bytes $file $schemaRecord
Write-Bytes $file $channelRecord

$chunkOffset = [UInt64]$file.Position
Write-Bytes $file $chunkRecord
$messageIndexOffset = [UInt64]$file.Position
Write-Bytes $file $messageIndexRecord

$attachmentOffset = [UInt64]$file.Position
Write-Bytes $file $attachmentRecord
Write-Bytes $file $dataEndRecord

$summaryStart = [UInt64]$file.Position
$summary = New-Object System.IO.MemoryStream

$schemaGroupRelStart = [UInt64]$summary.Position
Write-Bytes $summary $schemaRecord
$schemaGroupLength = [UInt64]$summary.Position - $schemaGroupRelStart

$channelGroupRelStart = [UInt64]$summary.Position
Write-Bytes $summary $channelRecord
$channelGroupLength = [UInt64]$summary.Position - $channelGroupRelStart

$statisticsGroupRelStart = [UInt64]$summary.Position
Write-Bytes $summary $statisticsRecord
$statisticsGroupLength = [UInt64]$summary.Position - $statisticsGroupRelStart

$chunkIndexGroupRelStart = [UInt64]$summary.Position
$chunkIndexContent = New-ChunkIndexContent $chunkOffset ([UInt64]$chunkRecord.Length) $messageIndexOffset ([UInt64]$messageIndexRecord.Length) ([UInt64]$chunkRecords.Length) ([UInt64]$chunkRecords.Length)
Write-Bytes $summary (New-RecordBytes 0x08 $chunkIndexContent)
$chunkIndexGroupLength = [UInt64]$summary.Position - $chunkIndexGroupRelStart

$attachmentIndexGroupRelStart = [UInt64]$summary.Position
$attachmentIndexContent = New-AttachmentIndexContent $attachmentOffset ([UInt64]$attachmentRecord.Length) ([UInt64]$attachmentData.Length)
Write-Bytes $summary (New-RecordBytes 0x0A $attachmentIndexContent)
$attachmentIndexGroupLength = [UInt64]$summary.Position - $attachmentIndexGroupRelStart

$summaryOffsetStart = $summaryStart + [UInt64]$summary.Position
Write-Bytes $summary (New-RecordBytes 0x0E (New-SummaryOffsetContent 0x03 ($summaryStart + $schemaGroupRelStart) $schemaGroupLength))
Write-Bytes $summary (New-RecordBytes 0x0E (New-SummaryOffsetContent 0x04 ($summaryStart + $channelGroupRelStart) $channelGroupLength))
Write-Bytes $summary (New-RecordBytes 0x0E (New-SummaryOffsetContent 0x0B ($summaryStart + $statisticsGroupRelStart) $statisticsGroupLength))
Write-Bytes $summary (New-RecordBytes 0x0E (New-SummaryOffsetContent 0x08 ($summaryStart + $chunkIndexGroupRelStart) $chunkIndexGroupLength))
Write-Bytes $summary (New-RecordBytes 0x0E (New-SummaryOffsetContent 0x0A ($summaryStart + $attachmentIndexGroupRelStart) $attachmentIndexGroupLength))

$summaryBytes = New-Bytes $summary
$footerPrefix = New-FooterPrefix $summaryStart $summaryOffsetStart
$summaryCrc = [Phase34Crc32]::Compute((Join-Bytes $summaryBytes $footerPrefix))
$footerRecord = New-RecordBytes 0x02 (New-FooterContent $summaryStart $summaryOffsetStart $summaryCrc)

Write-Bytes $file $summaryBytes
Write-Bytes $file $footerRecord
Write-Bytes $file $magic

[IO.File]::WriteAllBytes($OutputPath, (New-Bytes $file))
Test-SmokeFile $OutputPath $attachmentData

$size = (Get-Item -LiteralPath $OutputPath).Length
Write-Host "[phase34-smoke] Generated: $OutputPath"
Write-Host "[phase34-smoke] Size:      $size bytes"
Write-Host "[phase34-smoke] Self-check: leading/trailing magic, summary_crc, ChunkIndex, AttachmentIndex, payload, and attachment CRC passed."
