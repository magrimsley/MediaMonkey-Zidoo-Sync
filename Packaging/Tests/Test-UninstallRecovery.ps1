#requires -Version 5.1
[CmdletBinding()]
param([string]$ResultsRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-Tests'),[Parameter(Mandatory=$true)][string]$PayloadDirectory)
$ErrorActionPreference='Stop'
$tools=Join-Path (Split-Path $PSScriptRoot -Parent) 'Installer'
. (Join-Path $tools 'Common.ps1')
$lab=[IO.Path]::GetFullPath((Join-Path $ResultsRoot ('UninstallRecovery-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))))
New-Item -ItemType Directory -Force $lab | Out-Null
$results=New-Object System.Collections.Generic.List[object]
$children=New-Object System.Collections.Generic.List[object]
$powershell=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Assert($Ok,$Message){if(!$Ok){throw $Message}}
function Check($Name,[scriptblock]$Body){try{& $Body;$results.Add([pscustomobject]@{Name=$Name;Status='PASS'})}catch{$results.Add([pscustomobject]@{Name=$Name;Status='FAIL';Detail=$_.Exception.ToString()})}finally{$results.ToArray() | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $lab 'checks.json') -Encoding UTF8}}
$breakpointScript=Join-Path $lab 'pause-uninstaller.ps1'
@'
param([string]$Target,[string]$Root,[string]$Signal,[string]$Boundary)
$ErrorActionPreference='Stop'
$lines=Get-Content -LiteralPath $Target -Encoding UTF8
$needle=if($Boundary -eq 'payload'){'Remove-Item -LiteralPath $path -Force'}else{'Remove-Item -LiteralPath $metaPath -Force'}
$line=0
for($i=0;$i -lt $lines.Count;$i++){if($lines[$i].Trim() -eq $needle){$line=$i+1;break}}
if(!$line){throw 'Expected uninstall breakpoint location not found'}
$global:MmzsHits=0;$global:MmzsSignal=$Signal;$global:MmzsThreshold=if($Boundary -eq 'payload'){4}else{2}
Set-PSBreakpoint -Script $Target -Line $line -Action {
 $global:MmzsHits++
 if($global:MmzsHits -eq $global:MmzsThreshold){[IO.File]::WriteAllText($global:MmzsSignal,'paused after prior removals');$deadline=(Get-Date).AddSeconds(90);while((Get-Date) -lt $deadline){Start-Sleep -Milliseconds 50};throw 'Test parent did not terminate paused child'}
} | Out-Null
& $Target -InstallRoot $Root
'@ | Set-Content -LiteralPath $breakpointScript -Encoding UTF8
function Start-Child([string]$Root,[string]$Boundary='',[switch]$Install){
 $psi=New-Object Diagnostics.ProcessStartInfo;$psi.FileName=$powershell
 $signal=Join-Path $lab ('signal-'+[guid]::NewGuid().ToString('N')+'.txt')
 if($Boundary){$psi.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+$breakpointScript+'" -Target "'+(Join-Path $tools 'Uninstall.ps1')+'" -Root "'+$Root+'" -Signal "'+$signal+'" -Boundary '+$Boundary}
 elseif($Install){$psi.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $tools 'Install.ps1')+'" -InstallRoot "'+$Root+'" -PayloadDirectory "'+$PayloadDirectory+'" -FileOnly'}
 else{$psi.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $tools 'Uninstall.ps1')+'" -InstallRoot "'+$Root+'"'}
 $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
 $psi.EnvironmentVariables["PSModulePath"]=(Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\Modules')

 $p=New-Object Diagnostics.Process;$p.StartInfo=$psi;[void]$p.Start()
 $child=[pscustomobject]@{Process=$p;Signal=$signal;Stdout=$p.StandardOutput.ReadToEndAsync();Stderr=$p.StandardError.ReadToEndAsync()};$children.Add($child);return $child
}
function Finish($Child){if(!$Child.Process.WaitForExit(90000)){throw 'Child timed out'};$text=$Child.Stdout.GetAwaiter().GetResult()+[Environment]::NewLine+$Child.Stderr.GetAwaiter().GetResult();$text | Set-Content (Join-Path $lab ('process-'+$Child.Process.Id+'.log')) -Encoding UTF8;return [pscustomobject]@{Exit=$Child.Process.ExitCode;Text=$text}}
function Terminate-AtBoundary($Child){$deadline=(Get-Date).AddSeconds(45);while(!(Test-Path $Child.Signal)){if($Child.Process.HasExited){throw ('Child exited before breakpoint: '+(Finish $Child).Text)};if((Get-Date) -gt $deadline){throw 'Breakpoint signal timed out'};Start-Sleep -Milliseconds 25};$Child.Process.Kill();[void](Finish $Child)}
$root=Join-Path $lab 'payload-interruption'
$user=Join-Path $lab 'separate-user-configuration.txt';'keep user configuration' | Set-Content $user;$userHash=(Get-FileHash $user).Hash
try{
 Check 'real-process-kill-after-three-payload-removals' {
  & (Join-Path $tools 'Install.ps1') -PayloadDirectory $PayloadDirectory -InstallRoot $root -FileOnly
  'keep unknown file' | Set-Content (Join-Path $root 'user-note.txt')
  $child=Start-Child $root 'payload';Terminate-AtBoundary $child
  $j=Get-Content (Join-Path $root '.mmzs-uninstall.json') -Encoding UTF8 -Raw | ConvertFrom-Json
  $state=$j.StateJson | ConvertFrom-Json
  $missing=@($state.Versions[0].Files | Where-Object {!(Test-Path (Join-Path $root $_.Path))}).Count
  Assert ($j.Phase -eq 'RemovingFiles' -and $missing -ge 3 -and (Test-Path (Join-Path $root '.mmzs-install.json'))) 'Expected real partial deletion boundary not reached'
 }
 Check 'installer-refuses-pending-uninstall' {
  $pending=Join-Path $root '.mmzs-uninstall.json';$hash=(Get-FileHash $pending).Hash
  $blocked=Finish (Start-Child $root -Install)
  Assert ($blocked.Exit -ne 0 -and $blocked.Text -like '*Uninstallation is pending*' -and (Get-FileHash $pending).Hash -eq $hash) 'Install did not preserve pending uninstallation'
 }
 Check 'resume-refuses-modified-remaining-file' {
  $pending=Join-Path $root '.mmzs-uninstall.json';$j=Get-Content $pending -Encoding UTF8 -Raw | ConvertFrom-Json;$state=$j.StateJson | ConvertFrom-Json
  $entry=$state.Versions[0].Files | Where-Object {Test-Path (Join-Path $root $_.Path)} | Select-Object -Last 1
  $path=Join-Path $root $entry.Path;$bytes=[IO.File]::ReadAllBytes($path);$hash=(Get-FileHash $pending).Hash
  try{[IO.File]::AppendAllText($path,' ');$blocked=Finish (Start-Child $root);Assert ($blocked.Exit -ne 0 -and $blocked.Text -like '*Modified installed file preserved*' -and (Get-FileHash $pending).Hash -eq $hash) 'Modified remaining file was not preserved'}finally{[IO.File]::WriteAllBytes($path,$bytes)}
 }
 Check 'resume-refuses-corrupt-snapshot' {
  $pending=Join-Path $root '.mmzs-uninstall.json';$bytes=[IO.File]::ReadAllBytes($pending);$j=Get-Content $pending -Encoding UTF8 -Raw | ConvertFrom-Json
  try{$j.StateJson+=' ';Write-InstallerJson $pending $j;$hash=(Get-FileHash $pending).Hash;$blocked=Finish (Start-Child $root);Assert ($blocked.Exit -ne 0 -and $blocked.Text -like '*Uninstall journal is invalid*' -and (Get-FileHash $pending).Hash -eq $hash) 'Corrupt snapshot was accepted or modified'}finally{[IO.File]::WriteAllBytes($pending,$bytes)}
 }
 Check 'retry-completes-partial-payload-removal-and-preserves-data' {
  $done=Finish (Start-Child $root);Assert ($done.Exit -eq 0) $done.Text
  Assert (!(Test-Path (Join-Path $root 'Versions')) -and !(Test-Path (Join-Path $root '.mmzs-uninstall.json')) -and !(Test-Path (Join-Path $root '.mmzs-install.json'))) 'Owned payload/active metadata remains'
  Assert ((Get-FileHash $user).Hash -eq $userHash -and (Get-Content (Join-Path $root 'user-note.txt')) -eq 'keep unknown file') 'User or unknown data changed'
  Assert (@(Get-ChildItem (Join-Path $root '.recovery') -Recurse -Filter completed-uninstall.json).Count -eq 1) 'Uninstall recovery receipt missing'
 }
 Check 'retry-after-process-kill-with-original-manifest-already-removed' {
  $second=Join-Path $lab 'metadata-interruption'
  & (Join-Path $tools 'Install.ps1') -PayloadDirectory $PayloadDirectory -InstallRoot $second -FileOnly
  $child=Start-Child $second 'metadata';Terminate-AtBoundary $child
  Assert (!(Test-Path (Join-Path $second '.mmzs-install.json')) -and (Test-Path (Join-Path $second '.mmzs-uninstall.json'))) 'Original state was not removed before termination'
  $done=Finish (Start-Child $second);Assert ($done.Exit -eq 0) $done.Text
  Assert (!(Test-Path (Join-Path $second 'Versions')) -and !(Test-Path (Join-Path $second '.mmzs-uninstall.json')) -and (Get-FileHash $user).Hash -eq $userHash) 'Snapshot-based completion failed'
 }
}finally{
 foreach($child in $children){if(!$child.Process.HasExited){$child.Process.Kill();$child.Process.WaitForExit()};$child.Process.Dispose()}
 [pscustomobject]@{SupportRevision=5;ApplicationVersion='1.1-alpha.60.18.7';Scope='FileOnly; real child termination at debugger-controlled boundaries after actual file deletion; corruption/edit fixtures; no app/device/real playlist writes; not power-loss proof';Checks=$results.ToArray()} | ConvertTo-Json -Depth 9 | Set-Content (Join-Path $lab 'summary.json') -Encoding UTF8
}
$results | Format-Table Name,Status
Write-Host "Evidence: $lab"
if(@($results.ToArray() | Where-Object Status -eq 'FAIL').Count){exit 1}
