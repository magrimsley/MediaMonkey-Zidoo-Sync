[CmdletBinding()]
param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent),[string]$ResultsRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MediaMonkeyZidooSync-Tests'),[string]$ExpectedVersion='1.1-alpha.60.16.1')
$ErrorActionPreference='Stop'
[xml]$project=Get-Content (Join-Path $ProjectRoot 'MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.VersionPrefix+'-'+[string]$project.Project.PropertyGroup.VersionSuffix
if($version -ne $ExpectedVersion){throw "Version mismatch: expected $ExpectedVersion; got $version"}
$run=Join-Path $ResultsRoot ('Checkpoint-'+$version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force $run | Out-Null
$testRoot=Join-Path $PSScriptRoot 'MigrationTests'
$source=Join-Path $ProjectRoot 'MediaMonkeyZidooSync\Services\ConfigurationService.cs'
('using Environment = TestEnvironment;'+[Environment]::NewLine+[IO.File]::ReadAllText($source)) | Set-Content (Join-Path $testRoot 'ConfigurationService.cs') -Encoding UTF8
Copy-Item (Join-Path $ProjectRoot 'MediaMonkeyZidooSync\Models\AppConfig.cs') $testRoot
Get-FileHash $source,(Join-Path $ProjectRoot 'MediaMonkeyZidooSync\MediaMonkeyZidooSync.csproj') | ConvertTo-Json | Set-Content (Join-Path $run 'source-hashes.json')
& dotnet run --project (Join-Path $testRoot 'MigrationTests.csproj') -- (Join-Path $run 'migration') *> (Join-Path $run 'migration.log')
$migrationExit=$LASTEXITCODE
Get-Content (Join-Path $run 'migration.log')
[pscustomobject]@{Version=$version;MigrationExit=$migrationExit;Results=$run} | ConvertTo-Json | Set-Content (Join-Path $run 'checkpoint.json')
Write-Host "Results: $run"
exit $migrationExit


