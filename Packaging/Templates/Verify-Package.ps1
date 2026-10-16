#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$manifest=Get-Content (Join-Path $PSScriptRoot 'package-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($manifest.ApplicationVersion -ne '1.1-alpha.60.17.1' -or $manifest.SupportRevision -ne 5){throw 'Unexpected package version.'}
$seen=@{}
foreach($entry in $manifest.Files){
 $relative=[string]$entry.Path
 if([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)' -or $relative.Contains(':') -or $seen.ContainsKey($relative)){throw 'Invalid or duplicate package path.'}
 $seen[$relative]=$true
 $path=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
 if(!$path.StartsWith($PSScriptRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escapes root.'}
 $probe=$path
 while($probe -ne $PSScriptRoot){if((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Package reparse points are refused.'};$probe=Split-Path $probe -Parent}
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.SHA256){throw "Package file differs: $relative"}
}
$actual=@(Get-ChildItem (Join-Path $PSScriptRoot 'Payload'),(Join-Path $PSScriptRoot 'Installer') -File -Recurse)
foreach($file in $actual){$relative=$file.FullName.Substring($PSScriptRoot.Length+1);if(!$seen.ContainsKey($relative)){throw "Unlisted install content: $relative"}}
Write-Host ('Verified '+$manifest.Files.Count+' package files. Hashes detect changes; this unsigned manifest does not authenticate a publisher.')