#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PayloadDirectory,[string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'Programs\MediaMonkey Zidoo Sync'),[string]$ExpectedVersion='1.1-alpha.60.17',[switch]$FileOnly)
. (Join-Path $PSScriptRoot 'Common.ps1')
$root=Resolve-InstallRoot $InstallRoot
$payload=(Resolve-Path -LiteralPath $PayloadDirectory).Path
Assert-PayloadVersion $payload $ExpectedVersion
if($ExpectedVersion -notmatch '^\d+\.\d+-alpha\.\d+(\.\d+)*$'){throw 'Unsupported alpha version format.'}
$installerLock=Enter-InstallerLock $root
try {
if(Test-Path -LiteralPath (Join-Path $root '.mmzs-uninstall.json')){throw 'Uninstallation is pending. Resume it with the trusted installer package before installing.'}
$ownerPath=Join-Path $root '.mmzs-owner.json'
$bootstrapOwned=$false
if(Test-Path -LiteralPath $ownerPath){$owner=Get-Content $ownerPath -Encoding UTF8 -Raw | ConvertFrom-Json;if($owner.ProductId -ne $script:ProductId -or $owner.InstallRoot -ne $root){throw 'Bootstrap ownership marker does not match'};$bootstrapOwned=$true}
if((Test-Path -LiteralPath (Join-Path $root '.mmzs-pending.json')) -and !$bootstrapOwned){throw 'Interrupted journal lacks bootstrap ownership; files preserved.'}
$recovered=Recover-InterruptedInstall $root -ExpectedFileOnly ([bool]$FileOnly)
if($recovered -and $recovered.Repaired -and $recovered.Version -eq $ExpectedVersion){if($FileOnly){Write-Host "Recovered committed installation $ExpectedVersion. File-only mode; no registration or shortcut changes."}else{Write-Host "Recovered committed installation $ExpectedVersion. Registration and shortcut are complete."};return}
$state=Read-InstallState $root
if(!$state -and !$bootstrapOwned -and (Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count){throw 'Refusing a nonempty directory without an installation ownership marker.'}
if($state -and [bool]$state.FileOnly -ne [bool]$FileOnly){throw 'Use the same FileOnly mode when updating an existing installation.'}
if(!$FileOnly){
 $registration=Get-UninstallKey
 if((Test-Path $registration) -and (Get-ItemProperty $registration).InstallLocation -ne $root){throw 'Another registered installation owns this product key.'}
 $menuPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'MediaMonkey Zidoo Sync.lnk'
 if(Test-Path -LiteralPath $menuPath){$existingShell=New-Object -ComObject WScript.Shell;$existingLink=$existingShell.CreateShortcut($menuPath);if(!$existingLink.TargetPath.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Existing shortcut belongs to another installation.'}}
}
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File)
if(@(Get-ChildItem -LiteralPath $payload -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Payload contains redirected paths.'}
$versionRoot=Resolve-OwnedPath $root ('Versions\'+$ExpectedVersion)
if(Test-Path -LiteralPath $versionRoot){throw 'This version is already installed; uninstall before reinstalling the same version.'}
$rootExisted=Test-Path -LiteralPath $root
$versionsParent=Join-Path $root 'Versions'
$versionsExisted=Test-Path -LiteralPath $versionsParent
$transaction=[guid]::NewGuid().ToString('N')
$stageName='.staging-'+$transaction
$staging=Resolve-OwnedPath $root $stageName
New-Item -ItemType Directory -Force -Path $root,$versionsParent | Out-Null
Write-InstallerJson $ownerPath ([pscustomobject]@{ProductId=$script:ProductId;InstallRoot=$root})
$pending=Join-Path $root '.mmzs-pending.json'
$journal=[pscustomobject]@{ProductId=$script:ProductId;InstallRoot=$root;TransactionId=$transaction;Version=$ExpectedVersion;Stage=$stageName;FileOnly=[bool]$FileOnly;Phase='Copying'}
Write-InstallerJson $pending $journal
New-Item -ItemType Directory -Path $staging | Out-Null
$created=New-Object System.Collections.Generic.List[string]
$entries=@()
try {
foreach($file in $files){
 $relative=$file.FullName.Substring($payload.TrimEnd('\').Length+1)
 $target=Resolve-OwnedPath $staging $relative
 New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
 $created.Add($target)
 Copy-Item -LiteralPath $file.FullName -Destination $target
 $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
 if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash){throw "Copy verification failed: $relative"}
 $entries+=[pscustomobject]@{Path=('Versions\'+$ExpectedVersion+'\'+$relative);SHA256=$hash}
}
 # Both absolute paths were validated within this owned installation root.
 # Publish the version directory only after every file has been copied and hashed.
 $journal.Phase='ReadyToPublish';Write-InstallerJson $pending $journal
 [IO.Directory]::Move($staging,$versionRoot)
 $journal.Phase='PayloadPublished';Write-InstallerJson $pending $journal
} catch {
 $copyError=$_
 foreach($path in $created){[void](Resolve-InstallRoot $path);if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
 if(Test-Path -LiteralPath $staging){
  foreach($dir in @(Get-ChildItem -LiteralPath $staging -Directory -Recurse | Sort-Object {$_.FullName.Length} -Descending)){[void](Resolve-InstallRoot $dir.FullName);if(@(Get-ChildItem -LiteralPath $dir.FullName -Force).Count -eq 0){Remove-Item -LiteralPath $dir.FullName}}
  if(@(Get-ChildItem -LiteralPath $staging -Force).Count -eq 0){Remove-Item -LiteralPath $staging}
 }
 if(!$versionsExisted -and @(Get-ChildItem -LiteralPath $versionsParent -Force).Count -eq 0){Remove-Item -LiteralPath $versionsParent}
 if(!$rootExisted -and @(Get-ChildItem -LiteralPath $root -Force).Count -eq 0){Remove-Item -LiteralPath $root}
 if(!(Test-Path -LiteralPath $versionRoot) -and (Test-Path -LiteralPath $pending)){Remove-Item -LiteralPath $pending}
 throw $copyError
}
$helpers=@()
foreach($name in @('Common.ps1','Uninstall.ps1')){$source=Join-Path $PSScriptRoot $name;Write-InstallerBytes (Join-Path $root $name) ([IO.File]::ReadAllBytes($source));$helpers+=[pscustomobject]@{Path=$name;SHA256=(Get-FileHash -LiteralPath $source).Hash}}
$versions=@();if($state){$versions=@($state.Versions)}
$versions+=[pscustomobject]@{Version=$ExpectedVersion;Files=$entries}
$newState=[pscustomobject]@{ProductId=$script:ProductId;InstallRoot=$root;CurrentVersion=$ExpectedVersion;FileOnly=[bool]$FileOnly;Versions=$versions;SupportRevision=5;Helpers=$helpers}
$statePath=Join-Path $root '.mmzs-install.json'
Write-InstallerJson $statePath $newState
$journal.Phase='StateCommitted';Write-InstallerJson $pending $journal
if(!$FileOnly){Complete-UserRegistration $root $ExpectedVersion}
Remove-Item -LiteralPath $pending
Write-Host "Installed $ExpectedVersion at $versionRoot. Application was not launched. Configuration and archives were not touched."
} finally {$installerLock.ReleaseMutex();$installerLock.Dispose()}
