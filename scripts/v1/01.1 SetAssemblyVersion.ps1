[CmdletBinding()]
param (
    [string] $projectsFiles,
    [string] $filterPattern,
    [string] $version
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

if ([string]::IsNullOrEmpty($projectsFiles)) { $projectsFiles = "SampleAPI.csproj|SampleAPI.Client.csproj|SampleAPI.Client.Publish.csproj" }
if ([string]::IsNullOrEmpty($filterPattern)) { $filterPattern = "" }
if ([string]::IsNullOrEmpty($version)) { $version = "1.0.0.100" }
Write-Host "projectsFiles: $projectsFiles"
Write-Host "filterPattern: $filterPattern"
Write-Host "version: $version"


$projectsFilesArray = $projectsFiles.Split('|')
foreach($projectFile in $projectsFilesArray) 
{
    $projectFileFullPath = Get-ChildItem $projectFile -Recurse | Where-Object { $_.FullName -match $filterPattern }
    $projectFileFolder = Split-Path -Path $projectFileFullPath

    $assemblyInfoFile = "$projectFileFolder\Properties\AssemblyInfo.cs";
    Write-Host "assemblyInfoFile: $assemblyInfoFile"
    SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
    SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version
}

# Write-Host "version: $version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

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