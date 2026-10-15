#requires -Version 5.1
# Opt-in interactive test: selects only local tabs; no device/network actions.
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [int]$TimeoutSeconds = 20)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot ('MediaMonkeyZidooSync\bin\' + $Configuration + '\net9.0-windows\MediaMonkey Zidoo Sync.exe')
if (-not (Test-Path -LiteralPath $exe)) { throw ('Build output missing: ' + $exe) }
if (-not [Environment]::UserInteractive) { throw 'Interactive desktop required.' }
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
        if ($p.HasExited) { throw ('Application exited before displaying window: ' + $p.ExitCode) }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $handle = $p.MainWindowHandle; break }
    }
    if ($handle -eq [IntPtr]::Zero) { throw 'Main window did not appear.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    if ($null -eq $window) { throw 'Cannot inspect main window.' }
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $tabs = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $required = @('Synchronization', 'Configuration', 'Run History / Logs')
    foreach ($name in $required) {
        $match = $null
        foreach ($tab in $tabs) {
            if ($tab.Current.Name -eq $name) { $match = $tab; break }
        }
        if ($null -eq $match) { throw ('Missing accessible tab: ' + $name) }
        $pattern = $null
        if (-not $match.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            throw ('Tab does not expose SelectionItemPattern: ' + $name)
        }
        $pattern.Select()
        Start-Sleep -Milliseconds 200
        if (-not $pattern.Current.IsSelected) { throw ('Tab selection failed: ' + $name) }
        Write-Output ('PASS: Selected local tab: ' + $name)
    }
    Write-Output 'SCOPE: Only local tab selection; no buttons, assessment, configuration writes, synchronization, or device calls.'
    exit 0
} finally {
    if ($null -ne $p) {
        try {
            $p.Refresh()
            if (-not $p.HasExited) {
                if (-not $p.CloseMainWindow()) { $p.Kill() }
                elseif (-not $p.WaitForExit(3000)) { $p.Kill() }
            }
        } catch { Write-Warning ('Could not close UI-test process: ' + $_.Exception.Message) }
        $p.Dispose()
    }
}
