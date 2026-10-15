#requires -Version 5.1
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot 'MediaMonkeyZidooSync'
$checks = 0
function Assert-Contract([bool]$Condition, [string]$Message) {
    $script:checks++
    if (-not $Condition) { throw "WPF contract failure: $Message" }
    Write-Host "PASS: $Message"
}
try {
    $files = @(Get-ChildItem -LiteralPath $root -Filter '*.xaml' -File)
    Assert-Contract ($files.Count -ge 4) 'Expected WPF XAML files are present'
    foreach ($file in $files) {
        $xml = New-Object System.Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($file.FullName)
        Assert-Contract ($null -ne $xml.DocumentElement) ("XAML XML parses: " + $file.Name)
        $className = $xml.DocumentElement.GetAttribute('Class','http://schemas.microsoft.com/winfx/2006/xaml')
        Assert-Contract (-not [string]::IsNullOrWhiteSpace($className)) ("x:Class exists: " + $file.Name)
        $codeFile = $file.FullName + '.cs'
        Assert-Contract (Test-Path -LiteralPath $codeFile) ("Code-behind exists: " + $file.Name)
        $code = [System.IO.File]::ReadAllText($codeFile)
        $classShort = $className.Split('.')[-1]
        Assert-Contract ([regex]::IsMatch($code, '\bpartial\s+class\s+' + [regex]::Escape($classShort) + '\b')) ("Code-behind class matches: " + $file.Name)
        $eventNames = @('Click','Checked','Unchecked','SelectionChanged','TextChanged','Closing','Loaded','Unloaded','Initialized','MouseDoubleClick','MouseDown','MouseUp','PreviewKeyDown','KeyDown','KeyUp','SizeChanged','ValueChanged','LostFocus','GotFocus','PreviewMouseWheel','PreviewMouseDown','PreviewMouseUp')
        $nodes = @($xml.SelectNodes('//*'))
        foreach ($node in $nodes) {
            foreach ($attr in $node.Attributes) {
                if ($eventNames -contains $attr.LocalName -and $attr.Value -match '^[A-Za-z_][A-Za-z_0-9]*$') {
                    $handler = [regex]::Escape($attr.Value)
                    Assert-Contract ([regex]::IsMatch($code, '\b' + $handler + '\s*\(')) ("Handler $($attr.Value) exists: " + $file.Name)
                }
            }
        }
    }
    $runner = [System.IO.File]::ReadAllText((Join-Path $root 'Services\SyncRunner.cs'))
    Assert-Contract ([regex]::IsMatch($runner, 'throw\s+new\s+NotSupportedException\s*\(')) 'Production SyncRunner still rejects synchronization'
    Write-Host "WPF static contracts: $checks passed, 0 failed"
    exit 0
} catch {
    Write-Host $_.Exception.ToString() -ForegroundColor Red
    exit 1
}
