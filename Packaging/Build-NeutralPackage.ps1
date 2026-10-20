#requires -Version 5.1
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$PayloadDirectory,
 [string]$ResultsRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-Tests')
)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $PayloadDirectory).Path
$results=[IO.Path]::GetFullPath($ResultsRoot)
$projectRoot=Split-Path $PSScriptRoot -Parent
if($results -eq $projectRoot -or $results.StartsWith($projectRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Choose a results directory outside the source project.'}
$exe=Join-Path $source 'MediaMonkey Zidoo Sync.exe'
$version=([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion -split '\+')[0]
if($version -ne '1.1-alpha.60.18.7'){throw 'Payload version must be Alpha.60.18.7. This source-built candidate requires separate acceptance; it is not the earlier binary checkpoint.'}
$out=Join-Path $results ('NeutralPackage-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
$package=Join-Path $out 'MediaMonkey-Alpha60.18.7-Neutral'
New-Item -ItemType Directory -Path $package | Out-Null
Copy-Item $source (Join-Path $package 'Payload') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'Installer') (Join-Path $package 'Installer') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'Templates\*.ps1') $package
$seed=Join-Path $package 'Payload\Resources\MediaMonkey-Zidoo-Config-v1.0.0.json'
$config=Get-Content $seed -Encoding UTF8 -Raw | ConvertFrom-Json
foreach($section in @('Application','Zidoo','NAS','MediaMonkey')){foreach($property in $config.$section.PSObject.Properties){if($property.Value -is [string] -and !($section -eq 'Application' -and $property.Name -eq 'Version')){$property.Value=''}}}
[IO.File]::WriteAllText($seed,($config | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
$pdb=Join-Path $package 'Payload\MediaMonkey Zidoo Sync.pdb'
if(Test-Path $pdb){Remove-Item -LiteralPath $pdb}
Copy-Item (Join-Path $PSScriptRoot 'PACKAGE-README.md') (Join-Path $package 'README.md')
$files=@(Get-ChildItem $package -File -Recurse | ForEach-Object {[pscustomobject]@{Path=$_.FullName.Substring($package.Length+1);SHA256=(Get-FileHash $_.FullName).Hash}})
$manifest=[pscustomobject]@{ApplicationVersion='1.1-alpha.60.18.7';SupportRevision=5;Distribution='Unsigned neutral-default rebuild; fresh acceptance pending';Files=$files}
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $package 'package-manifest.json') -Encoding UTF8
& (Join-Path $package 'Verify-Package.ps1')
$archive=Join-Path $out 'MediaMonkey-Alpha60.18.7-Neutral-unsigned.zip'
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive
$hash=(Get-FileHash $archive).Hash
[IO.File]::WriteAllText(($archive+'.sha256'),($hash+'  '+[IO.Path]::GetFileName($archive)+[Environment]::NewLine))
[pscustomobject]@{Package=$package;Archive=$archive;SHA256=$hash;Status='Built and file hashes verified; not a new acceptance result';ProductionSync='Native execution enabled; candidate acceptance pending'} | ConvertTo-Json | Set-Content (Join-Path $out 'build-summary.json') -Encoding UTF8
Write-Host ('Package prepared: '+$archive)
Write-Host 'No application was launched, installed, signed or published.'
