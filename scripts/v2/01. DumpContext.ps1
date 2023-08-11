[CmdletBinding()]
param (
    [string] $context
)

Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$projectBaseName = $($env:PROJECTBASENAME)
if ([string]::IsNullOrEmpty($projectBaseName)) { $projectBaseName = "101.Samples" }

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1"

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append


    Write-Host "$context"

Stop-Transcript

