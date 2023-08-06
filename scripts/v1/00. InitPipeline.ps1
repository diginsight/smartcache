[CmdletBinding()]
param (
    $tools
)

Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 
 
Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

Write-Host "tools: $tools"
Write-Host "solution: $($env:SOLUTION)"

Set-Location $($env:BUILD_SOURCESDIRECTORY)
nuget restore # $($env:SOLUTION)

Dotnet restore


# Write-Host "$version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

