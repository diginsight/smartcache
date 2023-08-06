Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$projectBaseName = $($env:PROJECTBASENAME)
if ([string]::IsNullOrEmpty($projectBaseName)) { $projectBaseName = "Common.Diagnostics" }

$scriptFolder = $($PSScriptRoot.TrimEnd('\')); 
Import-Module "$scriptFolder\Common.ps1"

Set-Location $scriptFolder

$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$assemblyInfoFile = "..\$projectBaseName\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
 

$version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
Write-Host "$version"
Write-Host "##vso[task.setvariable variable=version;]$version"
##vso[task.setvariable variable=versionString;isOutput=true]$version

Write-Host "Set environment variable to ($env:VERSION)"

Get-ChildItem Env:

Write-Host "versionString ($versionString)"

Stop-Transcript

