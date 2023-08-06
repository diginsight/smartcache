[CmdletBinding()]
param (
    [string] $projectFile
)

Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$buildSourcesDirectory = $env:BUILD_SOURCESDIRECTORY
if ([string]::IsNullOrEmpty($buildSourcesDirectory)) { $buildSourcesDirectory = ".." }
Set-Location "$buildSourcesDirectory" 

if ([string]::IsNullOrEmpty($projectFile)) { $projectFile = "SampleAPI.csproj" } 

$projectFileFullPath = Get-ChildItem $projectFile -Recurse
if ([string]::IsNullOrEmpty($projectFileFullPath)) { Write-Host "##vso[task.logissue type=warning]Error: no file found for projectFile: '$projectFile'." }
if ($projectFileFullPath -is [array]) { Write-Host "##vso[task.logissue type=warning]Error: $($projectFileFullPath.Count) files were found for projectFile: '$projectFile'." }
Write-Host "projectFileFullPath: $projectFileFullPath"

$projectFileFolder = Split-Path -Path $projectFileFullPath

$assemblyInfoFile = "$projectFileFolder\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"

$version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"

Write-Host "Set environment variable to ($env:VERSION)"
Write-Host "version: $version"
Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

return 0;