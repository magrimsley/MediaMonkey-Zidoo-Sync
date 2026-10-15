#requires -Version 5.1
[CmdletBinding()]
param([string]$InstallRoot=(Join-Path $env:LOCALAPPDATA 'Programs\MediaMonkey Zidoo Sync'),[switch]$FileOnly)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Verify-Package.ps1')
& (Join-Path $PSScriptRoot 'Installer\Install.ps1') -PayloadDirectory (Join-Path $PSScriptRoot 'Payload') -InstallRoot $InstallRoot -ExpectedVersion '1.1-alpha.60.16.1' -FileOnly:$FileOnly