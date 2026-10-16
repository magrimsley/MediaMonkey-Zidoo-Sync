[CmdletBinding()]
param([string]$ResultsRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-Tests'),[Parameter(Mandatory=$true)][string]$NewPayloadDirectory,[Parameter(Mandatory=$true)][string]$OldPayloadDirectory)
$ErrorActionPreference='Stop'
$tools=Join-Path (Split-Path $PSScriptRoot -Parent) 'Installer'
$test=Join-Path $ResultsRoot ('InstallerLab-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force $test | Out-Null
$results=@()
function Check([string]$Name,[scriptblock]$Body){try{& $Body;$script:results+=[pscustomobject]@{Name=$Name;Status='PASS'}}catch{$script:results+=[pscustomobject]@{Name=$Name;Status='FAIL';Detail=$_.Exception.ToString()}}}
function Assert($Value,$Message){if(!$Value){throw $Message}}
$newPayload=$NewPayloadDirectory
$oldPayload=$OldPayloadDirectory
$root=Join-Path $test 'User Profile é\Programs\MediaMonkey Zidoo Sync'
$config=Join-Path $test 'User Profile é\ApplicationData\MediaMonkey Zidoo Sync\Configuration\Current\settings.json'
New-Item -ItemType Directory -Force (Split-Path $config) | Out-Null
'user-settings-retention-sentinel' | Set-Content -LiteralPath $config
$sentinelHash=(Get-FileHash $config).Hash
Check 'fresh-file-install-with-spaces-and-Unicode' {
 & (Join-Path $tools 'Install.ps1') -PayloadDirectory $oldPayload -InstallRoot $root -ExpectedVersion '1.1-alpha.60.15.1' -FileOnly
 Assert (Test-Path (Join-Path $root 'Versions\1.1-alpha.60.15.1\MediaMonkey Zidoo Sync.exe')) 'Installed executable missing'
}
Check 'interrupted-copy-preserves-installed-version-and-retry' {
 $broken=Join-Path $test 'locked-source'
 New-Item -ItemType Directory -Force (Join-Path $broken 'Resources') | Out-Null
 Copy-Item -LiteralPath (Join-Path $newPayload 'MediaMonkey Zidoo Sync.exe') -Destination $broken
 $locked=Join-Path $broken 'Resources\MediaMonkey-Zidoo-Config-v1.0.0.json'
 Copy-Item -LiteralPath (Join-Path $newPayload 'Resources\MediaMonkey-Zidoo-Config-v1.0.0.json') -Destination $locked
 $manifest=Join-Path $root '.mmzs-install.json';$before=(Get-FileHash $manifest).Hash
 $held=[IO.File]::Open($locked,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
 $refused=$false;try{& (Join-Path $tools 'Install.ps1') -PayloadDirectory $broken -InstallRoot $root -FileOnly}catch{$refused=$true}finally{$held.Dispose()}
 Assert ($refused -and (Get-FileHash $manifest).Hash -eq $before) 'Failed copy changed active installation state'
 Assert (!(Test-Path (Join-Path $root 'Versions\1.1-alpha.60.17.1'))) 'Partial version published'
 Assert (@(Get-ChildItem $root -Directory -Filter '.staging-*').Count -eq 0) 'Staging debris remains'
}
Check 'upgrade-to-Alpha60.17.1-retains-prior-payload' {
 & (Join-Path $tools 'Install.ps1') -PayloadDirectory $newPayload -InstallRoot $root -FileOnly
 $state=Get-Content (Join-Path $root '.mmzs-install.json') -Raw | ConvertFrom-Json
 Assert ($state.CurrentVersion -eq '1.1-alpha.60.17.1' -and @($state.Versions).Count -eq 2) 'Upgrade state incorrect'
 Assert ((Get-FileHash $config).Hash -eq $sentinelHash) 'Configuration changed'
}
Check 'wrong-version-refused-before-install' {
 $badRoot=Join-Path $test 'wrong-version'
 $refused=$false;try{& (Join-Path $tools 'Install.ps1') -PayloadDirectory $newPayload -InstallRoot $badRoot -ExpectedVersion '1.1-alpha.60.14' -FileOnly}catch{$refused=$true}
 Assert ($refused -and !(Test-Path $badRoot)) 'Mismatched payload not rejected'
}
Check 'unowned-directory-preserved' {
 $unowned=Join-Path $test 'unowned';New-Item -ItemType Directory -Force $unowned | Out-Null;'keep' | Set-Content (Join-Path $unowned 'keep.txt')
 $refused=$false;try{& (Join-Path $tools 'Install.ps1') -PayloadDirectory $newPayload -InstallRoot $unowned -FileOnly}catch{$refused=$true}
 Assert ($refused -and (Get-Content (Join-Path $unowned 'keep.txt')) -eq 'keep') 'Unowned directory changed'
}
Check 'locked-installed-file-stops-uninstall' {
 $exe=Join-Path $root 'Versions\1.1-alpha.60.17.1\MediaMonkey Zidoo Sync.exe'
 $handle=[IO.File]::Open($exe,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
 $refused=$false;try{& (Join-Path $root 'Uninstall.ps1') -InstallRoot $root}catch{$refused=$true}finally{$handle.Dispose()}
 Assert ($refused -and (Test-Path $exe) -and (Test-Path (Join-Path $root '.mmzs-install.json'))) 'Locked uninstall changed ownership or removed locked binary'
}
Check 'modified-installed-file-preserved' {
 $file=Join-Path $root 'Versions\1.1-alpha.60.17.1\Resources\MediaMonkey-Zidoo-Config-v1.0.0.json'
 $bytes=[IO.File]::ReadAllBytes($file)
 try{[IO.File]::AppendAllText($file,' ');$refused=$false;try{& (Join-Path $root 'Uninstall.ps1') -InstallRoot $root}catch{$refused=$true};Assert ($refused -and (Test-Path $file) -and (Test-Path (Join-Path $root '.mmzs-install.json'))) 'Modified installation was not preserved'}finally{[IO.File]::WriteAllBytes($file,$bytes)}
}
Check 'manifest-traversal-refused' {
 $manifest=Join-Path $root '.mmzs-install.json';$bytes=[IO.File]::ReadAllBytes($manifest)
 try{$state=Get-Content $manifest -Raw | ConvertFrom-Json;$state.Versions[0].Files[0].Path='Versions\..\..\outside.txt';$state | ConvertTo-Json -Depth 8 | Set-Content $manifest;$refused=$false;try{& (Join-Path $root 'Uninstall.ps1') -InstallRoot $root}catch{$refused=$true};Assert ($refused -and (Test-Path (Join-Path $root 'Versions\1.1-alpha.60.17.1\MediaMonkey Zidoo Sync.exe'))) 'Unsafe manifest not rejected before removal'}finally{[IO.File]::WriteAllBytes($manifest,$bytes)}
}
Check 'uninstall-preserves-user-configuration-and-unknown-files' {
 'keep-unknown' | Set-Content (Join-Path $root 'user-note.txt')
 & (Join-Path $root 'Uninstall.ps1') -InstallRoot $root
 Assert ((Get-FileHash $config).Hash -eq $sentinelHash) 'User configuration changed'
 Assert ((Get-Content (Join-Path $root 'user-note.txt')) -eq 'keep-unknown') 'Unknown file removed'
 Assert (!(Test-Path (Join-Path $root '.mmzs-install.json')) -and !(Test-Path (Join-Path $root 'Versions'))) 'Program payload remains'
}
$results | Format-Table Name,Status
$out=Join-Path $test 'installer-file-tests.json'
[pscustomobject]@{Scope='FileOnly installer tests in Codex workspace. No registry, Start menu, actual user profiles, or normal application launch.';TestRoot=$test;Checks=$results} | ConvertTo-Json -Depth 6 | Set-Content $out
if(@($results | Where-Object Status -eq 'FAIL').Count){exit 1}

