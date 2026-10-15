#requires -Version 5.1
# Interactive desktop required. Read-only inspection of a newly launched application window.
[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [int]$TimeoutSeconds = 20
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot ('MediaMonkeyZidooSync\bin\' + $Configuration + '\net9.0-windows\MediaMonkey Zidoo Sync.exe')
if (-not (Test-Path -LiteralPath $exe)) { throw ('Build output missing: ' + $exe) }
if (-not [Environment]::UserInteractive) { throw 'UI smoke test requires an interactive Windows desktop.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = $null
try {
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $handle = [IntPtr]::Zero
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { throw ('Application exited before displaying a window; exit code ' + $p.ExitCode) }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $handle = $p.MainWindowHandle; break }
    }
    if ($handle -eq [IntPtr]::Zero) { throw 'Application main window did not appear within timeout.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    if ($null -eq $window) { throw 'UI Automation could not inspect the main window.' }
    $title = $window.Current.Name
    if ([string]::IsNullOrWhiteSpace($title)) { throw 'Main window has no accessible title.' }
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $buttonCount = 0
    foreach ($control in $buttons) {
        if ($control.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { $buttonCount++ }
    }
    if ($buttonCount -lt 1) { throw 'No accessible button controls found in main window.' }
    Write-Output ('PASS: Main window appeared: ' + $title)
    Write-Output ('PASS: Accessible buttons detected: ' + $buttonCount)
    Write-Output 'SCOPE: Launch, window discovery and read-only UI Automation inspection only; no clicks or device calls.'
    exit 0
} finally {
    if ($null -ne $p) {
        try {
            $p.Refresh()
            if (-not $p.HasExited) {
                if (-not $p.CloseMainWindow()) { $p.Kill() }
                elseif (-not $p.WaitForExit(3000)) { $p.Kill() }
            }
        } catch { Write-Warning ('Could not close smoke-test process: ' + $_.Exception.Message) }
        $p.Dispose()
    }
}
