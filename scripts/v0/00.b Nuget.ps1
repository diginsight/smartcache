[CmdletBinding()]
param (
    [string] $path,
    [string] $projects,
    [string] $include = $null,
    [string] $exclude = $null
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

if ([string]::IsNullOrEmpty($path)) { $path = "01. Wpf|03. Empty" }
# if ($path.IndexOf('|') -ge 0) { $path = $path.Split('|') }
if ([string]::IsNullOrEmpty($projects)) { $projects = "*.csproj" }

Write-Host "projects: $projects"
Write-Host "include: $include"
Write-Host "exclude: $exclude"

Write-Host "projectPaths:"
$projectPaths = GetFiles -path $path -filter $projects -include $include -exclude $exclude
foreach($projectPath in $projectPaths)  {
    Write-Host ""
    Write-Host "------------------------------------------------------"
    Write-Host "nuget restore '$($projectPath.FullName)' START ------------"
    nuget restore "$($projectPath.FullName)"
    Write-Host "nuget restore '$($projectPath.FullName)' END"
    Write-Host "------------------------------------------------------"
}

# Write-Host "$version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

