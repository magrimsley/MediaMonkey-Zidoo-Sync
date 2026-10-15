Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:ProductId='MediaMonkeyZidooSync.Alpha'
function Resolve-InstallRoot([string]$Path){
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
 if($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')){throw 'A drive root cannot be an installation directory.'}
 $probe=$full
 while($probe){if(Test-Path -LiteralPath $probe){if((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Redirected installation paths are unsupported: $probe"}};$parent=Split-Path $probe -Parent;if($parent -eq $probe){break};$probe=$parent}
 return $full
}
function Resolve-OwnedPath([string]$Root,[string]$Relative){
 if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.\.([\\/]|$)'){throw 'Unsafe manifest path.'}
 $path=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
 if(!$path.StartsWith($Root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Manifest path escapes installation.'}
 [void](Resolve-InstallRoot $path)
 return $path
}
function Read-InstallState([string]$Root){
 $path=Join-Path $Root '.mmzs-install.json'
 if(!(Test-Path -LiteralPath $path)){return $null}
 $state=Get-Content -LiteralPath $path -Encoding UTF8 -Raw | ConvertFrom-Json
 if($state.ProductId -ne $script:ProductId -or $state.InstallRoot -ne $Root){throw 'Installation ownership marker does not match.'}
 return $state
}
function Assert-PayloadVersion([string]$Payload,[string]$Expected){
 $exe=Join-Path $Payload 'MediaMonkey Zidoo Sync.exe'
 if(!(Test-Path -LiteralPath $exe)){throw 'Published application executable missing.'}
 $version=([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -split '\+')[0]
 if($version -ne $Expected){throw "Payload version mismatch: expected $Expected; got $version"}
 if(!(Test-Path -LiteralPath (Join-Path $Payload 'Resources\MediaMonkey-Zidoo-Config-v1.0.0.json'))){throw 'Packaged configuration missing.'}
}
function Get-UninstallKey {return 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MediaMonkeyZidooSync.Alpha'}
function Get-TextSHA256([string]$Text){
 $sha=[Security.Cryptography.SHA256]::Create()
 try{return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-','')}finally{$sha.Dispose()}
}
function Assert-UninstallSnapshot([string]$Root,$State){
 if($State.ProductId -ne $script:ProductId -or $State.InstallRoot -ne $Root -or $State.FileOnly -isnot [bool] -or $State.SupportRevision -notin @(4,5)){throw 'Uninstall snapshot ownership or format is invalid.'}
 if(@($State.Versions).Count -eq 0 -or @($State.Versions | Where-Object Version -eq $State.CurrentVersion).Count -ne 1){throw 'Uninstall snapshot version identity is invalid.'}
 $seen=@{};$versions=@{}
 foreach($version in @($State.Versions)){
  if($version.Version -notmatch '^\d+\.\d+-alpha\.\d+(\.\d+)*$' -or $versions.ContainsKey($version.Version) -or @($version.Files).Count -eq 0){throw 'Invalid/duplicate uninstall version metadata.'}
  $versions[$version.Version]=$true
  foreach($entry in @($version.Files)){
   if($entry.Path -notlike ('Versions\'+$version.Version+'\*') -or $entry.SHA256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Uninstall file entry is outside its version or lacks a valid digest.'}
   $path=Resolve-OwnedPath $Root $entry.Path
   if($seen.ContainsKey($path.ToUpperInvariant())){throw 'Duplicate uninstall file entry.'};$seen[$path.ToUpperInvariant()]=$true
  }
 }
}
function Assert-UninstallRegistration([string]$Root,[switch]$ResumingCleanup){
 $key=Get-UninstallKey
 if(Test-Path $key){
  $item=Get-Item $key;$names=@($item.GetValueNames())
  if($names -contains 'InstallLocation'){
   if([string]$item.GetValue('InstallLocation') -ne $Root){throw 'Uninstall registration now belongs to another installation.'}
  }else{
   $owned=@('DisplayName','DisplayVersion','UninstallString','QuietUninstallString','NoModify','NoRepair')
   if(!$ResumingCleanup -or @($owned | Where-Object {$names -contains $_}).Count){throw 'Uninstall registration has lost its ownership anchor; evidence preserved.'}
  }
 }
 $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'MediaMonkey Zidoo Sync.lnk'
 if(Test-Path -LiteralPath $shortcut){$shell=New-Object -ComObject WScript.Shell;$link=$shell.CreateShortcut($shortcut);if(!$link.TargetPath.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Uninstall shortcut now belongs to another installation.'}}
}

function Enter-InstallerLock([string]$Root){
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$hash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Root.ToUpperInvariant()))).Replace('-','')}finally{$sha.Dispose()}
 $mutex=New-Object Threading.Mutex($false,('Local\MMZS-Installer-'+$hash))
 try{
  try{$acquired=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$acquired=$true}
  if(!$acquired){throw 'Another installer or uninstaller is already active for this installation.'}
  return $mutex
 }catch{$mutex.Dispose();throw}
}
function Write-InstallerBytes([string]$Path,[byte[]]$Bytes){
 $temp=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
 $backup=$temp+'.previous'
 try{
  $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try{$stream.Write($Bytes,0,$Bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
  if(Test-Path -LiteralPath $Path){[IO.File]::Replace($temp,$Path,$backup);Remove-Item -LiteralPath $backup}else{[IO.File]::Move($temp,$Path)}
 }finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
}
function Write-InstallerJson([string]$Path,$Value){
 $json=$Value | ConvertTo-Json -Depth 12
 Write-InstallerBytes $Path ([Text.Encoding]::UTF8.GetBytes($json))
}
function Assert-RegistrationOwnership([string]$Root,[switch]$AllowEmptyKey){
 $key=Get-UninstallKey
 if(Test-Path $key){
  $item=Get-Item $key
  if($item.GetValueNames() -contains 'InstallLocation'){
   if([string]$item.GetValue('InstallLocation') -ne $Root){throw 'Another installation owns the uninstall registration.'}
  }elseif(!$AllowEmptyKey -or @($item.GetValueNames()).Count -ne 0 -or @($item.GetSubKeyNames()).Count -ne 0){throw 'Existing registration has no matching ownership anchor.'}
 }
 $path=Join-Path ([Environment]::GetFolderPath('Programs')) 'MediaMonkey Zidoo Sync.lnk'
 if(Test-Path -LiteralPath $path){
  $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($path)
  if(!$shortcut.TargetPath.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Existing shortcut belongs to another installation.'}
 }
}
function Assert-CommittedInstallation([string]$Root,$State,[string]$Version){
 if($State.CurrentVersion -ne $Version -or $State.PSObject.Properties.Name -notcontains 'SupportRevision' -or $State.SupportRevision -notin @(4,5) -or $State.PSObject.Properties.Name -notcontains 'Helpers'){throw 'Committed state cannot prove supported registration repair readiness.'}
 $versions=@($State.Versions | Where-Object Version -eq $Version)
 if($versions.Count -ne 1 -or @($versions[0].Files).Count -lt 2){throw 'Current-version ownership manifest is incomplete.'}
 $seen=@{}
 foreach($file in @($versions[0].Files)){
  if($file.Path -notlike ('Versions\'+$Version+'\*') -or $file.SHA256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Current-version file metadata is invalid.'}
  $path=Resolve-OwnedPath $Root $file.Path
  if($seen.ContainsKey($path.ToUpperInvariant())){throw 'Duplicate current-version file ownership entry.'};$seen[$path.ToUpperInvariant()]=$true
  if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $file.SHA256){throw "Committed payload missing or modified; repair refused: $path"}
 }
 if(@($State.Helpers).Count -ne 2){throw 'Installer helper ownership metadata is incomplete.'}
 $helperNames=@()
 foreach($helper in @($State.Helpers)){
  if($helper.Path -notin @('Common.ps1','Uninstall.ps1') -or $helperNames -contains $helper.Path -or $helper.SHA256 -notmatch '^[A-Fa-f0-9]{64}$'){throw 'Invalid helper ownership metadata.'}
  $helperNames+=$helper.Path;$path=Resolve-OwnedPath $Root $helper.Path
  if(!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $helper.SHA256){throw 'Installed helper missing or modified; repair refused.'}
 }
 Assert-PayloadVersion (Resolve-OwnedPath $Root ('Versions\'+$Version)) $Version
}
function Complete-UserRegistration([string]$Root,[string]$Version,[switch]$Recovering){
 Assert-RegistrationOwnership $Root -AllowEmptyKey:$Recovering
 $key=Get-UninstallKey
 if(!(Test-Path $key)){New-Item -Path $key | Out-Null}
 # Establish ownership before writing display metadata. A recovery may complete only an owned or empty key.
 New-ItemProperty -Path $key -Name InstallLocation -Value $Root -PropertyType String -Force | Out-Null
 $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
 $command='"'+$ps+'" -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $Root 'Uninstall.ps1')+'" -InstallRoot "'+$Root+'"'
 foreach($pair in @{DisplayName='MediaMonkey Zidoo Sync (Alpha)';DisplayVersion=$Version;UninstallString=$command;QuietUninstallString=$command}.GetEnumerator()){New-ItemProperty -Path $key -Name $pair.Key -Value $pair.Value -PropertyType String -Force | Out-Null}
 foreach($name in @('NoModify','NoRepair')){New-ItemProperty -Path $key -Name $name -Value 1 -PropertyType DWord -Force | Out-Null}
 $shortcutPath=Join-Path ([Environment]::GetFolderPath('Programs')) 'MediaMonkey Zidoo Sync.lnk'
 $temporary=$shortcutPath+'.'+[guid]::NewGuid().ToString('N')+'.lnk'
 try{
  $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($temporary)
  $shortcut.TargetPath=Resolve-OwnedPath $Root ('Versions\'+$Version+'\MediaMonkey Zidoo Sync.exe')
  $shortcut.WorkingDirectory=Resolve-OwnedPath $Root ('Versions\'+$Version)
  $shortcut.Save()
  Write-InstallerBytes $shortcutPath ([IO.File]::ReadAllBytes($temporary))
 }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
}
function Recover-InterruptedInstall([string]$Root,$ExpectedFileOnly=$null){
 $pending=Join-Path $Root '.mmzs-pending.json'
 if(!(Test-Path -LiteralPath $pending)){return}
 $j=Get-Content -LiteralPath $pending -Encoding UTF8 -Raw | ConvertFrom-Json
 $id=[guid]::Empty
 if($j.ProductId -ne $script:ProductId -or $j.InstallRoot -ne $Root -or ![guid]::TryParseExact($j.TransactionId,'N',[ref]$id) -or $j.Version -notmatch '^\d+\.\d+-alpha\.\d+(\.\d+)*$' -or $j.Stage -ne ('.staging-'+$j.TransactionId) -or $j.Phase -notin @('Copying','ReadyToPublish','PayloadPublished','StateCommitted') -or $j.FileOnly -isnot [bool]){throw 'Interrupted-install journal is invalid; files preserved for review.'}
 if($null -ne $ExpectedFileOnly -and $j.FileOnly -ne [bool]$ExpectedFileOnly){throw 'Requested installation mode differs from interrupted transaction; evidence preserved.'}
 $state=Read-InstallState $Root
 $committed=$false
 if($state){$count=@($state.Versions | Where-Object Version -eq $j.Version).Count;if($count -gt 1){throw 'Duplicate committed versions; evidence preserved.'};$committed=$count -eq 1}
 if($committed){
  if($state.FileOnly -ne $j.FileOnly -or $j.Phase -notin @('PayloadPublished','StateCommitted')){throw 'Committed state and journal are inconsistent; evidence preserved.'}
  Assert-CommittedInstallation $Root $state $j.Version
  if(!$j.FileOnly){Complete-UserRegistration $Root $j.Version -Recovering}
 }
 if(!$committed){
  $recovery=Resolve-OwnedPath $Root ('.recovery\'+$j.TransactionId)
  foreach($item in @(@{Source=$j.Stage;Suffix='staging'},@{Source=('Versions\'+$j.Version);Suffix='uncommitted-payload'})){
   $source=Resolve-OwnedPath $Root $item.Source
   $target=Resolve-OwnedPath $Root ('.recovery\'+$j.TransactionId+'\'+$item.Suffix)
   if(Test-Path -LiteralPath $source){
    New-Item -ItemType Directory -Force -Path $recovery | Out-Null
    if(Test-Path -LiteralPath $target){throw 'Recovery destination already exists; evidence preserved.'}
    # Both resolved absolute move targets are within this installation root.
    [IO.Directory]::Move($source,$target)
   }
  }
  New-Item -ItemType Directory -Force -Path $recovery | Out-Null
  Copy-Item -LiteralPath $pending -Destination (Join-Path $recovery 'interrupted-install.json')
 }
 $receipt=Resolve-OwnedPath $Root ('.recovery\'+$j.TransactionId+'\completed-recovery.json')
 New-Item -ItemType Directory -Force -Path (Split-Path $receipt) | Out-Null
 if($committed){Copy-Item -LiteralPath $pending -Destination (Join-Path (Split-Path $receipt) 'interrupted-install.json')}
 Write-InstallerJson $receipt ([pscustomobject]@{TransactionId=$j.TransactionId;Version=$j.Version;Committed=$committed;Registered=(!$j.FileOnly);Timestamp=(Get-Date).ToString('o')})
 Remove-Item -LiteralPath $pending
 if($committed){return [pscustomobject]@{Repaired=$true;Version=$j.Version;FileOnly=$j.FileOnly}}
}
