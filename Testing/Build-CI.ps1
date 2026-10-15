#requires -Version 5.1
[CmdletBinding()]
param([string]$ResultsRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-CI'),[switch]$NoPublish)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$out=Join-Path $ResultsRoot ('Build-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force $out | Out-Null
$checks=@()
function Step($Name,[scriptblock]$Body){
 try{& $Body *> (Join-Path $out ($Name+'.log'));$script:checks+=[pscustomobject]@{Name=$Name;Status='PASS'}}catch{$script:checks+=[pscustomobject]@{Name=$Name;Status='FAIL';Detail=$_.Exception.Message};throw}finally{$script:checks | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'summary.json') -Encoding UTF8}
 Write-Host ($Name+': PASS')
}
Step 'Application-Build' {& dotnet build (Join-Path $root 'MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj') -c Release --nologo;if($LASTEXITCODE){throw 'Application build failed.'}}
foreach($name in @('NativeOrchestrationTests','RecoveryTests','RunHistoryDurabilityTests','IsolatedSyncTests')){Step $name {& dotnet run --project (Join-Path $root ($name+'\'+$name+'.csproj')) -c Release --nologo;if($LASTEXITCODE){throw 'Offline test failed.'}}}
Step 'WPF-Static-Contracts' {& (Join-Path $root 'Test-WpfContracts.ps1');if($LASTEXITCODE){throw 'Static contracts failed.'}}
if(!$NoPublish){Step 'Unsigned-Windows-Publish' {& dotnet publish (Join-Path $root 'MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $out 'publish') --nologo;if($LASTEXITCODE){throw 'Publish failed.'}}}
Write-Host ('Results: '+$out)
Write-Host 'No signing, release upload, production sync or live device test was performed.'
