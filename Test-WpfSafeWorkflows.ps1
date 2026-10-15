#requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Debug',[int]$TimeoutSeconds=20)
$ErrorActionPreference='Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'Testing\NeutralSafeWorkflowTests\NeutralSafeWorkflowTests.csproj') -c $Configuration --nologo
if($LASTEXITCODE -ne 0){throw 'In-process WPF safe-workflow regression failed.'}