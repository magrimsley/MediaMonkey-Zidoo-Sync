#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallRoot=$PSScriptRoot)
. (Join-Path $PSScriptRoot 'Common.ps1')
$root=Resolve-InstallRoot $InstallRoot
$installerLock=Enter-InstallerLock $root
try {
 $journalPath=Join-Path $root '.mmzs-uninstall.json'
 if(Test-Path -LiteralPath $journalPath){
  if(Test-Path -LiteralPath (Join-Path $root '.mmzs-pending.json')){throw 'Install and uninstall journals coexist; manual review required.'}
  $journal=Get-Content -LiteralPath $journalPath -Encoding UTF8 -Raw | ConvertFrom-Json
  $id=[guid]::Empty
  if($journal.ProductId -ne $script:ProductId -or $journal.InstallRoot -ne $root -or ![guid]::TryParseExact($journal.TransactionId,'N',[ref]$id) -or $journal.Phase -notin @('Prepared','RemovingFiles','RemovingRegistration','RemovingMetadata','Completed') -or $journal.StateSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-TextSHA256 $journal.StateJson) -ne $journal.StateSHA256){throw 'Uninstall journal is invalid; no files changed.'}
  $state=$journal.StateJson | ConvertFrom-Json
 } else {
  [void](Recover-InterruptedInstall $root)
  $state=Read-InstallState $root
  if(!$state){throw 'No owned installation manifest or uninstall recovery journal found.'}
  Assert-UninstallSnapshot $root $state
  $stateJson=[IO.File]::ReadAllText((Join-Path $root '.mmzs-install.json'))
  $metadata=@()
  foreach($name in @('.mmzs-install.json','Common.ps1','Uninstall.ps1','.mmzs-owner.json')){
   $file=Resolve-OwnedPath $root $name
   if(Test-Path -LiteralPath $file){$metadata+=[pscustomobject]@{Path=$name;SHA256=(Get-FileHash -LiteralPath $file).Hash}}
  }
  foreach($helper in @($state.Helpers)){
   $ownedHelper=@($metadata | Where-Object Path -eq $helper.Path)
   if($helper.Path -notin @('Common.ps1','Uninstall.ps1') -or $ownedHelper.Count -ne 1 -or $ownedHelper[0].SHA256 -ne $helper.SHA256){throw 'Installed helper differs from committed ownership metadata.'}
  }
  $journal=[pscustomobject]@{ProductId=$script:ProductId;InstallRoot=$root;TransactionId=[guid]::NewGuid().ToString('N');Phase='Prepared';StateJson=$stateJson;StateSHA256=(Get-TextSHA256 $stateJson);Metadata=$metadata}
 }
 Assert-UninstallSnapshot $root $state
 $metadataNames=@()
 foreach($meta in @($journal.Metadata)){
  if($meta.Path -notin @('.mmzs-install.json','Common.ps1','Uninstall.ps1','.mmzs-owner.json') -or $metadataNames -contains $meta.Path -or $meta.SHA256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid uninstall helper/metadata ownership.'}
  $metadataNames+=$meta.Path
  $metaPath=Resolve-OwnedPath $root $meta.Path
  if((Test-Path -LiteralPath $metaPath) -and (Get-FileHash -LiteralPath $metaPath).Hash -ne $meta.SHA256){throw 'Installer metadata changed; resume refused before deletion.'}
 }
 if($metadataNames -notcontains '.mmzs-install.json' -or $metadataNames -notcontains '.mmzs-owner.json'){throw 'Incomplete uninstall metadata snapshot.'}
 $owned=New-Object System.Collections.Generic.List[string]
 $expectedHashes=@{}
 foreach($version in @($state.Versions)){
  foreach($entry in @($version.Files)){
   $path=Resolve-OwnedPath $root $entry.Path
   $expectedHashes[$path]=$entry.SHA256
   if(Test-Path -LiteralPath $path){
    if((Get-FileHash -LiteralPath $path).Hash -ne $entry.SHA256){throw "Modified installed file preserved; uninstall stopped: $path"}
    $probe=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$probe.Dispose()
   }
   $owned.Add($path)
  }
 }
 if(!$state.FileOnly){Assert-UninstallRegistration $root -ResumingCleanup:($journal.Phase -in @('RemovingRegistration','RemovingMetadata','Completed'))}
 if($journal.Phase -ne 'Completed'){
  if($journal.Phase -in @('Prepared','RemovingFiles')){
  $journal.Phase='RemovingFiles';Write-InstallerJson $journalPath $journal
  # Already removed payload files are allowed; changed remaining files are refused above.
  foreach($path in $owned){
   if(Test-Path -LiteralPath $path){
    if((Get-FileHash -LiteralPath $path).Hash -ne $expectedHashes[$path]){throw 'Remaining payload changed during uninstall; file preserved.'}
    Remove-Item -LiteralPath $path -Force
   }
  }
  $journal.Phase='RemovingRegistration';Write-InstallerJson $journalPath $journal
  }
  if($journal.Phase -eq 'RemovingRegistration'){
  if(!$state.FileOnly){
   Assert-UninstallRegistration $root -ResumingCleanup
   $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'MediaMonkey Zidoo Sync.lnk'
   if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut -Force}
   $key=Get-UninstallKey
   if(Test-Path $key){
    # Keep InstallLocation until the other owned fields are gone, preserving the ownership anchor on retry.
    foreach($name in @('DisplayName','DisplayVersion','QuietUninstallString','NoModify','NoRepair','UninstallString','InstallLocation')){if((Get-Item $key).GetValueNames() -contains $name){Remove-ItemProperty -Path $key -Name $name -ErrorAction Stop}}
    if(@((Get-Item $key).GetValueNames()).Count -eq 0 -and @((Get-Item $key).GetSubKeyNames()).Count -eq 0){Remove-Item -Path $key}
   }
  }
  $journal.Phase='RemovingMetadata';Write-InstallerJson $journalPath $journal
  }
  foreach($meta in @($journal.Metadata | Where-Object Path -ne '.mmzs-owner.json')){
   $metaPath=Resolve-OwnedPath $root $meta.Path
   if(Test-Path -LiteralPath $metaPath){
    if((Get-FileHash -LiteralPath $metaPath).Hash -ne $meta.SHA256){throw 'Metadata changed during uninstall; file preserved.'}
    Remove-Item -LiteralPath $metaPath -Force
   }
  }
  $receipt=Resolve-OwnedPath $root ('.recovery\'+$journal.TransactionId+'\completed-uninstall.json')
  New-Item -ItemType Directory -Force -Path (Split-Path $receipt) | Out-Null
  Write-InstallerJson $receipt ([pscustomobject]@{ProductId=$script:ProductId;InstallRoot=$root;TransactionId=$journal.TransactionId;CompletedAt=(Get-Date).ToString('o');Snapshot=$journal})
  $journal.Phase='Completed';Write-InstallerJson $journalPath $journal
 }
 # Clean only known payload parent directories, never recursively remove a directory tree.
 $parents=@{}
 foreach($path in $owned){$dir=Split-Path $path -Parent;while($dir -ne $root -and $dir.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){$parents[$dir]=$true;$dir=Split-Path $dir -Parent}}
 foreach($dir in @($parents.Keys | Sort-Object Length -Descending)){[void](Resolve-InstallRoot $dir);if((Test-Path -LiteralPath $dir) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0){Remove-Item -LiteralPath $dir}}
 Remove-Item -LiteralPath $journalPath
 Write-Host 'Owned program files uninstalled. Configuration, archives, unknown files, recovery evidence and the bootstrap ownership marker were preserved.'
} finally {$installerLock.ReleaseMutex();$installerLock.Dispose()}
