#requires -Version 5.1
<#
.SYNOPSIS
  Build and run the safe, offline MediaMonkey Zidoo Sync regression suite.
.DESCRIPTION
  Runs four offline test executables and static WPF contract checks. Does not launch
  the WPF application, enable synchronization, or contact Zidoo hardware.
  Creates timestamped logs and a Markdown + JSON summary under E:\Temp\MediaMonkey by default.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory = '',
    [switch]$NoBuild,
    [switch]$UiSmoke,
    [switch]$UiNavigation,
    [switch]$UiWorkflows,
    [switch]$UiDeep,
    [string]$ExpectedVersion = '1.1-alpha.60.16.1'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-Tests') ('Regression-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
$ResultsDirectory = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$results = New-Object System.Collections.Generic.List[object]
$started = Get-Date
$failed = $false
$binaryIdentity = $null

function Invoke-RecordedStep {
    param([string]$Name, [string[]]$Arguments)
    $safeName = $Name -replace '[^A-Za-z0-9_-]', '-'
    $log = Join-Path $ResultsDirectory ($safeName + '.log')
    Write-Host ('==> ' + $Name) -ForegroundColor Cyan
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    $exitCode = 1
    try {
        # Capture native stdout and stderr to a file, without allowing stderr
        # text alone to terminate the PowerShell script.
        # Wait only for the dotnet process itself, not for unrelated descendants.
        # The arguments in this runner are trusted fixed command/project paths.
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = 'dotnet'
        $startInfo.WorkingDirectory = $root
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.Arguments = (($Arguments | ForEach-Object {
            '"' + $_.Replace('"', '\\"') + '"'
        }) -join ' ')
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $startInfo
        try {
            if (-not $proc.Start()) { throw 'dotnet failed to start' }
            # Read both pipes concurrently to avoid deadlock on full buffers.
            $stdoutTask = $proc.StandardOutput.ReadToEndAsync()
            $stderrTask = $proc.StandardError.ReadToEndAsync()
            $proc.WaitForExit()
            [System.IO.File]::WriteAllText(($log + '.stdout'), $stdoutTask.GetAwaiter().GetResult())
            [System.IO.File]::WriteAllText(($log + '.stderr'), $stderrTask.GetAwaiter().GetResult())
            $exitCode = $proc.ExitCode
        } finally {
            $proc.Dispose()
        }
        $out = if (Test-Path -LiteralPath ($log + '.stdout')) { Get-Content -LiteralPath ($log + '.stdout') -Raw } else { '' }
        $err = if (Test-Path -LiteralPath ($log + '.stderr')) { Get-Content -LiteralPath ($log + '.stderr') -Raw } else { '' }
        [System.IO.File]::WriteAllText($log, ($out + [Environment]::NewLine + $err))
        if ($out) { Write-Host $out }
        if ($err) { Write-Host $err }
    } catch {
        [System.IO.File]::WriteAllText($log, ('Failed to launch step: ' + $_.Exception.ToString()))
        Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    } finally {
        $timer.Stop()
    }
    $status = if ($exitCode -eq 0) { 'PASS' } else { 'FAIL' }
    $results.Add([pscustomobject]@{ Step=$Name; Status=$status; ExitCode=$exitCode; DurationSeconds=[math]::Round($timer.Elapsed.TotalSeconds,2); Log=$log })
    Write-Host ($status + ': ' + $Name + ' (' + [math]::Round($timer.Elapsed.TotalSeconds,1) + 's)') -ForegroundColor $(if ($exitCode -eq 0) {'Green'} else {'Red'})
    return ($exitCode -eq 0)
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet SDK was not found on PATH. Install .NET 9 SDK and retry.'
    }
    $project = Join-Path $root 'MediaMonkeyZidooSync\\MediaMonkeyZidooSync.csproj'
    if (-not (Test-Path -LiteralPath $project)) { throw ('Missing application project: ' + $project) }
    if (-not $NoBuild) {
        $buildOk = Invoke-RecordedStep -Name 'Application-Build' -Arguments @('build',$project,'-c',$Configuration,'--nologo')
        if (-not $buildOk) { $failed = $true }
    } else {
        Write-Warning 'Application build skipped by request; this is not a complete release gate.'
    }
    if (-not $failed) {
        $binary = Join-Path $root ('MediaMonkeyZidooSync\bin\'+$Configuration+'\net9.0-windows\MediaMonkey Zidoo Sync.dll')
        $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($binary).ProductVersion
        if (($actualVersion -split '\+')[0] -ne $ExpectedVersion) { throw "Binary version mismatch: expected $ExpectedVersion; got $actualVersion" }
        $binaryIdentity = [pscustomobject]@{ExpectedVersion=$ExpectedVersion;ProductVersion=$actualVersion;SHA256=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash}
    }
    if (-not $failed) {
        $testProjects = @(
            @{Name='Native-Orchestration'; Path='NativeOrchestrationTests\\NativeOrchestrationTests.csproj'},
            @{Name='Recovery'; Path='RecoveryTests\\RecoveryTests.csproj'},
            @{Name='Run-History-Durability'; Path='RunHistoryDurabilityTests\\RunHistoryDurabilityTests.csproj'},
            @{Name='Isolated-Sync'; Path='IsolatedSyncTests\\IsolatedSyncTests.csproj'}
        )
        foreach ($test in $testProjects) {
            $testPath = Join-Path $root $test.Path
            if (-not (Test-Path -LiteralPath $testPath)) {
                $results.Add([pscustomobject]@{Step=$test.Name;Status='FAIL';ExitCode=1;DurationSeconds=0;Log='Project missing: ' + $testPath})
                $failed = $true
                continue
            }
            if (-not (Invoke-RecordedStep -Name $test.Name -Arguments @('run','--project',$testPath,'-c',$Configuration,'--nologo'))) { $failed = $true }
        }
    }
    if (-not $failed) {
        $uiLog = Join-Path $ResultsDirectory 'WPF-Static-Contracts.log'
        $uiTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $uiExit = 1
        try {
            $psExe = (Get-Process -Id $PID).Path
            $uiScript = Join-Path $root 'Test-WpfContracts.ps1'
            & $psExe -NoProfile -ExecutionPolicy Bypass -File $uiScript *> $uiLog
            $uiExit = $LASTEXITCODE
        } catch {
            $_.Exception.ToString() | Out-File -LiteralPath $uiLog -Append
        } finally { $uiTimer.Stop() }
        $uiStatus = if ($uiExit -eq 0) { 'PASS' } else { 'FAIL' }
        $results.Add([pscustomobject]@{Step='WPF-Static-Contracts';Status=$uiStatus;ExitCode=$uiExit;DurationSeconds=[math]::Round($uiTimer.Elapsed.TotalSeconds,2);Log=$uiLog})
        Write-Host ($uiStatus + ': WPF-Static-Contracts')
        if ($uiExit -ne 0) { $failed = $true }
    }
    if (($UiSmoke -or $UiNavigation -or $UiWorkflows -or $UiDeep) -and -not $failed) {
        $smokeLog = Join-Path $ResultsDirectory 'WPF-Interactive-Smoke.log'
        $smokeTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $smokeExit = 1
        try {
            $psExe = (Get-Process -Id $PID).Path
            $smokeScript = Join-Path $root 'Test-WpfSmoke.ps1'
            & $psExe -NoProfile -ExecutionPolicy Bypass -File $smokeScript -Configuration $Configuration *> $smokeLog
            $smokeExit = $LASTEXITCODE
        } catch { $_.Exception.ToString() | Out-File -LiteralPath $smokeLog -Append }
        finally { $smokeTimer.Stop() }
        $smokeStatus = if ($smokeExit -eq 0) { 'PASS' } else { 'FAIL' }
        $results.Add([pscustomobject]@{Step='WPF-Interactive-Smoke';Status=$smokeStatus;ExitCode=$smokeExit;DurationSeconds=[math]::Round($smokeTimer.Elapsed.TotalSeconds,2);Log=$smokeLog})
        Write-Host ($smokeStatus + ': WPF-Interactive-Smoke')
        if ($smokeExit -ne 0) { $failed = $true }
    }
    if (($UiNavigation -or $UiWorkflows -or $UiDeep) -and -not $failed) {
        $navLog = Join-Path $ResultsDirectory 'WPF-Interactive-Navigation.log'
        $navTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $navExit = 1
        try {
            $psExe = (Get-Process -Id $PID).Path
            & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Test-WpfNavigation.ps1') -Configuration $Configuration *> $navLog
            $navExit = $LASTEXITCODE
        } catch { $_.Exception.ToString() | Out-File -LiteralPath $navLog -Append }
        finally { $navTimer.Stop() }
        $navStatus = if ($navExit -eq 0) { 'PASS' } else { 'FAIL' }
        $results.Add([pscustomobject]@{Step='WPF-Interactive-Navigation';Status=$navStatus;ExitCode=$navExit;DurationSeconds=[math]::Round($navTimer.Elapsed.TotalSeconds,2);Log=$navLog})
        Write-Host ($navStatus + ': WPF-Interactive-Navigation')
        if ($navExit -ne 0) { $failed = $true }
    }
    if (($UiWorkflows -or $UiDeep) -and -not $failed) {
        $workflowLog = Join-Path $ResultsDirectory 'WPF-Interactive-SafeWorkflows.log'
        $workflowTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $workflowExit = 1
        try {
            $psExe = (Get-Process -Id $PID).Path
            & $psExe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Test-WpfSafeWorkflows.ps1') -Configuration $Configuration *> $workflowLog
            $workflowExit = $LASTEXITCODE
        } catch { $_.Exception.ToString() | Out-File -LiteralPath $workflowLog -Append }
        finally { $workflowTimer.Stop() }
        $workflowStatus = if ($workflowExit -eq 0) { 'PASS' } else { 'FAIL' }
        $results.Add([pscustomobject]@{Step='WPF-Interactive-SafeWorkflows';Status=$workflowStatus;ExitCode=$workflowExit;DurationSeconds=[math]::Round($workflowTimer.Elapsed.TotalSeconds,2);Log=$workflowLog})
        Write-Host ($workflowStatus + ': WPF-Interactive-SafeWorkflows')
        if ($workflowExit -ne 0) { $failed = $true }
    }
    if ($UiDeep -and -not $failed) {
        if (-not (Invoke-RecordedStep -Name 'WPF-Deep-Workflows' -Arguments @('run','--project',(Join-Path $root 'WpfWorkflowTests\WpfWorkflowTests.csproj'),'-c',$Configuration,'--nologo'))) { $failed = $true }
    }
} catch {
    $failed = $true
    $results.Add([pscustomobject]@{Step='Harness';Status='FAIL';ExitCode=1;DurationSeconds=0;Log=$_.Exception.ToString()})
    Write-Host $_.Exception.ToString() -ForegroundColor Red
} finally {
    $status = if ($failed) { 'FAIL' } else { 'PASS' }
    $finished = Get-Date
    $report = [pscustomobject]@{
        OverallStatus=$status
        Started=$started.ToString('o')
        Finished=$finished.ToString('o')
        Configuration=$Configuration
        BuildSkipped=[bool]$NoBuild
        BinaryIdentity=$binaryIdentity
        FullTenStageAcceptance=([bool]$UiDeep -and -not $NoBuild -and -not $failed -and $results.Count -eq 10 -and $null -ne $binaryIdentity)
        Scope=('Build, offline test executables, static WPF contract checks' + $(if ($UiSmoke -or $UiNavigation -or $UiWorkflows -or $UiDeep) { ', opt-in interactive WPF launch/inspection' } else { '' }) + $(if ($UiNavigation) { ', local tab selection only' } else { '' }) + ' ; only opt-in local Help menu clicks; no synchronization or live Zidoo tests')
        Steps=@($results.ToArray())
    }
    $jsonPath = Join-Path $ResultsDirectory 'summary.json'
    $mdPath = Join-Path $ResultsDirectory 'summary.md'
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# MediaMonkey Zidoo Sync regression report')
    $lines.Add('')
    $lines.Add('**Overall: ' + $status + '**')
    $lines.Add('')
    $lines.Add('Build skipped: ' + [bool]$NoBuild)
    $lines.Add('')
    $lines.Add('| Step | Status | Exit code | Duration (s) |')
    $lines.Add('|---|---|---:|---:|')
    foreach ($r in $results) { $lines.Add('| ' + $r.Step + ' | ' + $r.Status + ' | ' + $r.ExitCode + ' | ' + $r.DurationSeconds + ' |') }
    $lines.Add('')
    $lines.Add('Scope: offline build and static WPF contracts; optional UI launch/inspection with -UiSmoke; optional local tab navigation with -UiNavigation; not a production-readiness certification.')
    $lines | Set-Content -LiteralPath $mdPath -Encoding UTF8
    Write-Host ('Report: ' + $mdPath)
    Write-Host ('Overall: ' + $status) -ForegroundColor $(if ($failed) {'Red'} else {'Green'})
}
if ($failed) { exit 1 }
exit 0

