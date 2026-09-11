[CmdletBinding()]
param(
    [string]$ContainerName,
    [string]$DatabaseName,
    [string]$OutputPath,
    [switch]$VerifyRestore,
    [switch]$ApplicationStopped,
    [switch]$KeepVerificationDatabase
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

throw "Implementation pending in this branch."
