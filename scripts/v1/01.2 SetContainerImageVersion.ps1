[CmdletBinding()]
param (
    [string] $deployFiles,
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
Write-Host "buildSourcesDirectory: $buildSourcesDirectory"

if ([string]::IsNullOrEmpty($deployFiles)) { $deployFiles = "deployment.yaml" }
if ([string]::IsNullOrEmpty($filterPattern)) { $filterPattern = "" }
if ([string]::IsNullOrEmpty($version)) { $version = "1.0.123" }
Write-Host "deployFiles: $deployFiles"
Write-Host "filterPattern: $filterPattern"
Write-Host "version: $version"

$deployFilesArray = $deployFiles.Split('|')
foreach($deployFilesPattern in $deployFilesArray) 
{
    $deployFileFullPaths = Get-ChildItem $deployFilesPattern -Recurse | Where-Object { $_.FullName -match $filterPattern }
    Write-Host "deployFileFullPaths: $deployFileFullPaths"

    foreach($deployFileFullPath in $deployFileFullPaths) {
        Write-Host "deployFileFullPath: $deployFileFullPath"

        SetContainerVersion -filePath $deployFileFullPath -version $version
    }
}

Write-Host "version: $version"
Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

return 0;