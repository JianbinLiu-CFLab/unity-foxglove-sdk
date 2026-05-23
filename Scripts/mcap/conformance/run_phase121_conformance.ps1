param(
    [switch]$ReleaseBlocking
)

$ErrorActionPreference = "Stop"

$ExpectedObservedCommit = "c3cab6bd3ce79199e362766daec3a4689f3a0335"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$OfficialRoot = Join-Path $RepoRoot "third-party/mcap"
$OfficialConformance = Join-Path $OfficialRoot "tests/conformance"
$BuildRoot = Join-Path $RepoRoot "build/mcap-conformance"
$OverlayRoot = Join-Path $BuildRoot "mcap-overlay"
$DataDir = Join-Path $BuildRoot "data"
$ReportPath = Join-Path $BuildRoot "phase121-conformance-report.json"
$ProjectPath = Join-Path $RepoRoot "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Unity2Foxglove.McapConformance.csproj"
$RunnerSourceRoot = Join-Path $RepoRoot "Scripts/mcap/conformance/csharp-runners"

function Invoke-CommandCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot,
        [hashtable]$Environment = @{}
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " "
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    foreach ($key in $Environment.Keys) {
        $psi.EnvironmentVariables[$key] = [string]$Environment[$key]
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Stdout = $stdout
        Stderr = $stderr
        Command = "$FileName $($Arguments -join ' ')"
    }
}

function ConvertTo-ProcessArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $escaped = $Value -replace '"', '\"'
    return '"' + $escaped + '"'
}

function Get-OfficialCommit {
    if (-not (Test-Path $OfficialRoot)) {
        return $null
    }

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return $null
    }

    $result = Invoke-CommandCapture -FileName "git" -Arguments @("-C", $OfficialRoot, "rev-parse", "HEAD")
    if ($result.ExitCode -ne 0) {
        return $null
    }

    return $result.Stdout.Trim()
}

function New-RunnerReport {
    param(
        [string]$Name,
        [string]$Kind,
        [int]$Passed = 0,
        [int]$Failed = 0,
        [int]$Skipped = 0,
        [object[]]$Failures = @(),
        [object[]]$Skips = @()
    )

    return [ordered]@{
        name = $Name
        kind = $Kind
        passed = $Passed
        failed = $Failed
        skipped = $Skipped
        failures = $Failures
        skips = $Skips
    }
}

function Write-Phase121Report {
    param(
        [string]$ExternalToolingStatus,
        [string]$Verdict,
        [int]$GeneratedVariantCount,
        [object[]]$Runners,
        [object[]]$Tooling,
        [string]$NodeVersion = $null,
        [string]$PackageManagerVersion = $null
    )

    New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null
    $report = [ordered]@{
        officialMcapCommit = Get-OfficialCommit
        officialMcapPath = $OfficialRoot
        expectedObservedOfficialMcapCommit = $ExpectedObservedCommit
        externalToolingStatus = $ExternalToolingStatus
        nodeVersion = $NodeVersion
        packageManagerVersion = $PackageManagerVersion
        generatedVariantCount = $GeneratedVariantCount
        runners = $Runners
        tooling = $Tooling
        verdict = $Verdict
        generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

function Write-SkippedReport {
    param([string]$Reason)

    Write-Phase121Report `
        -ExternalToolingStatus "skipped" `
        -Verdict "SKIPPED EXTERNAL TOOLING" `
        -GeneratedVariantCount 0 `
        -Runners @(
            (New-RunnerReport -Name "csharp-streamed-reader" -Kind "streamed-reader" -Skipped 0 -Skips @(@{ reason = $Reason })),
            (New-RunnerReport -Name "csharp-indexed-reader" -Kind "indexed-reader" -Skipped 0 -Skips @(@{ reason = $Reason })),
            (New-RunnerReport -Name "csharp-writer" -Kind "writer" -Skipped 0 -Skips @(@{ reason = "Writer option parity is deferred to Phase 122." }))
        ) `
        -Tooling @(@{ name = "external-tooling"; status = "skipped"; details = $Reason })
    Write-Host "Phase 121 conformance skipped: $Reason"
}

function Copy-DirectoryWithoutLocalState {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (Test-Path $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        if ($_.Name -in @(".git", "node_modules")) {
            return
        }
        $target = Join-Path $Destination $_.Name
        if ($_.PSIsContainer) {
            Copy-DirectoryWithoutLocalState -Source $_.FullName -Destination $target
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }
}

function Add-CsharpRunnerOverlay {
    $runnerDest = Join-Path $OverlayRoot "tests/conformance/scripts/run-tests/runners"
    Copy-Item -LiteralPath (Join-Path $RunnerSourceRoot "CsharpStreamedReaderTestRunner.ts") -Destination $runnerDest -Force
    Copy-Item -LiteralPath (Join-Path $RunnerSourceRoot "CsharpIndexedReaderTestRunner.ts") -Destination $runnerDest -Force
    Copy-Item -LiteralPath (Join-Path $RunnerSourceRoot "CsharpWriterTestRunner.ts") -Destination $runnerDest -Force

    $indexPath = Join-Path $runnerDest "index.ts"
    $index = Get-Content -Raw -LiteralPath $indexPath
    $imports = @"
import CsharpIndexedReaderTestRunner from "./CsharpIndexedReaderTestRunner.ts";
import CsharpStreamedReaderTestRunner from "./CsharpStreamedReaderTestRunner.ts";
import CsharpWriterTestRunner from "./CsharpWriterTestRunner.ts";
"@
    $index = $imports + "`n" + $index
    $index = $index -replace "const runners: readonly \(IndexedReadTestRunner \| StreamedReadTestRunner \| WriteTestRunner\)\[\] = \[", "const runners: readonly (IndexedReadTestRunner | StreamedReadTestRunner | WriteTestRunner)[] = [`n  new CsharpIndexedReaderTestRunner(),`n  new CsharpStreamedReaderTestRunner(),`n  new CsharpWriterTestRunner(),"
    Set-Content -LiteralPath $indexPath -Value $index -Encoding UTF8
}

function Measure-RunnerOutput {
    param(
        [string]$Name,
        [string]$Kind,
        [object]$Result
    )

    $text = ($Result.Stdout + "`n" + $Result.Stderr)
    $tested = ([regex]::Matches($text, "(?m)^\s*testing\s+")).Count
    $skipped = ([regex]::Matches($text, "(?m)^\s*(not supported|unsupported)\s+")).Count
    $errors = ([regex]::Matches($text, "(?m)^(Error:|fail\s+|\w+Error:)")).Count
    if ($Result.ExitCode -ne 0 -and $errors -eq 0) {
        $errors = 1
    }
    $passed = [Math]::Max(0, $tested - $errors)
    $failures = @()
    if ($errors -gt 0) {
        $failures = @(@{
            exitCode = $Result.ExitCode
            details = ($text.Trim() -split "`r?`n" | Select-Object -First 20) -join "`n"
        })
    }
    return New-RunnerReport -Name $Name -Kind $Kind -Passed $passed -Failed $errors -Skipped $skipped -Failures $failures
}

New-Item -ItemType Directory -Force -Path $BuildRoot | Out-Null

if (-not (Test-Path $OfficialConformance)) {
    Write-SkippedReport "third-party/mcap/tests/conformance was not found."
    if ($ReleaseBlocking) { exit 1 }
    exit 0
}

if ($null -eq (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-SkippedReport "dotnet was not found."
    if ($ReleaseBlocking) { exit 1 }
    exit 0
}

$build = Invoke-CommandCapture -FileName "dotnet" -Arguments @("build", $ProjectPath, "-c", "Release")
if ($build.ExitCode -ne 0) {
    Write-Error ("C# conformance console build failed.`n" + $build.Stdout + $build.Stderr)
}

$nodeCommand = Get-Command node -ErrorAction SilentlyContinue
$yarnCommand = Get-Command yarn -ErrorAction SilentlyContinue
if ($null -eq $nodeCommand -or $null -eq $yarnCommand) {
    Write-SkippedReport "Node and Yarn are required for the official foxglove/mcap conformance harness."
    if ($ReleaseBlocking) { exit 1 }
    exit 0
}

$nodeVersion = (Invoke-CommandCapture -FileName "node" -Arguments @("--version")).Stdout.Trim()
$yarnVersion = (Invoke-CommandCapture -FileName "yarn" -Arguments @("--version")).Stdout.Trim()

Copy-DirectoryWithoutLocalState -Source $OfficialRoot -Destination $OverlayRoot
Add-CsharpRunnerOverlay

$envMap = @{
    U2F_MCAP_CONFORMANCE_DLL = (Join-Path $RepoRoot "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/bin/Release/net9.0/Unity2Foxglove.McapConformance.dll")
}

$install = Invoke-CommandCapture -FileName "yarn" -Arguments @("install", "--immutable") -WorkingDirectory $OverlayRoot
if ($install.ExitCode -ne 0) {
    Write-SkippedReport ("Yarn dependencies are unavailable: " + (($install.Stdout + $install.Stderr).Trim() -split "`r?`n" | Select-Object -First 5) -join " ")
    if ($ReleaseBlocking) { exit 1 }
    exit 0
}

$generate = Invoke-CommandCapture `
    -FileName "yarn" `
    -Arguments @("workspace", "@foxglove/mcap-conformance", "generate-inputs", "--data-dir", $DataDir) `
    -WorkingDirectory $OverlayRoot
if ($generate.ExitCode -ne 0) {
    Write-SkippedReport ("Official fixture generation failed: " + (($generate.Stdout + $generate.Stderr).Trim() -split "`r?`n" | Select-Object -First 5) -join " ")
    if ($ReleaseBlocking) { exit 1 }
    exit 0
}

$variantCount = (Get-ChildItem -LiteralPath $DataDir -Recurse -Filter "*.mcap" | Measure-Object).Count
$runnerReports = @()

$streamed = Invoke-CommandCapture `
    -FileName "yarn" `
    -Arguments @("workspace", "@foxglove/mcap-conformance", "run-tests", "--data-dir", $DataDir, "--runner", "csharp-streamed-reader") `
    -WorkingDirectory $OverlayRoot `
    -Environment $envMap
$runnerReports += Measure-RunnerOutput -Name "csharp-streamed-reader" -Kind "streamed-reader" -Result $streamed

$indexed = Invoke-CommandCapture `
    -FileName "yarn" `
    -Arguments @("workspace", "@foxglove/mcap-conformance", "run-tests", "--data-dir", $DataDir, "--runner", "csharp-indexed-reader") `
    -WorkingDirectory $OverlayRoot `
    -Environment $envMap
$runnerReports += Measure-RunnerOutput -Name "csharp-indexed-reader" -Kind "indexed-reader" -Result $indexed

$writer = Invoke-CommandCapture `
    -FileName "yarn" `
    -Arguments @("workspace", "@foxglove/mcap-conformance", "run-tests", "--data-dir", $DataDir, "--runner", "csharp-writer") `
    -WorkingDirectory $OverlayRoot `
    -Environment $envMap
$runnerReports += Measure-RunnerOutput -Name "csharp-writer" -Kind "writer" -Result $writer

$failed = 0
foreach ($runnerReport in $runnerReports) {
    $failed += [int]$runnerReport.failed
}
$verdict = if ($failed -gt 0) { "MEASURED BASELINE WITH FAILURES" } else { "PASS WITH MEASURED BASELINE" }

Write-Phase121Report `
    -ExternalToolingStatus "available" `
    -Verdict $verdict `
    -GeneratedVariantCount $variantCount `
    -Runners $runnerReports `
    -Tooling @(
        @{ name = "dotnet-build"; status = "passed"; details = $build.Stdout.Trim() },
        @{ name = "fixture-generation"; status = "passed"; details = $generate.Stdout.Trim() }
    ) `
    -NodeVersion $nodeVersion `
    -PackageManagerVersion ("yarn " + $yarnVersion)

Write-Host "Phase 121 conformance report: $ReportPath"
if ($ReleaseBlocking -and $failed -gt 0) {
    exit 1
}
exit 0
