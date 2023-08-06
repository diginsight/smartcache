[CmdletBinding()]
param (
    [string] $masterProjectBaseName,
    [string] $projectBaseNames
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

if ([string]::IsNullOrEmpty($masterProjectBaseName)) { $masterProjectBaseName = "Common.Diagnostics" }
if ([string]::IsNullOrEmpty($projectBaseNames)) { $projectBaseNames = "Common.Diagnostics" }

$assemblyInfoFile = ".\$masterProjectBaseName\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"

$version = IncrementVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$projectBaseNamesArray = $projectBaseNames.Split('|')
foreach($projectBaseName in $projectBaseNamesArray) 
{
    $assemblyInfoFile = ".\$projectBaseName\Properties\AssemblyInfo.cs";
    Write-Host "assemblyInfoFile: $assemblyInfoFile"
    SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
    SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version
}

Write-Host "version: $version"
Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

# git --version
# git config user.email buildagent@microsoft.com
# git config user.name "Build Agent" 
# Write-Host "before git commit"
# git commit -a -m "Build version update"
# Write-Host "after git commit"
# try {
#   Write-Host "before git push origin HEAD:$($env:BUILD_SOURCEBRANCHNAME)"
#   git push origin HEAD:$($env:BUILD_SOURCEBRANCHNAME) 
#   Write-Host "after git push"
# } catch {
#   Write-Host "##vso[task.logissue type=warning]failure on git push command."
# }


Stop-Transcript

return 0;