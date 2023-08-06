[CmdletBinding()]
param (
    [string] $path,
    [string] $projects,
    [string] $projectsInclude = $null,
    [string] $projectsExclude = $null,
    $depth = $null
)

Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 
 
Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$res = 0

$buildSourcesDirectory = $env:BUILD_SOURCESDIRECTORY
if ([string]::IsNullOrEmpty($buildSourcesDirectory)) { $buildSourcesDirectory = ".." }
Set-Location "$buildSourcesDirectory"

if ([string]::IsNullOrEmpty($path)) { $path = ".." }
# if ($path.IndexOf('|') -ge 0) { $path = $path.Split('|') }
if ([string]::IsNullOrEmpty($projects)) { $projects = "ClientApp" }

Write-Host "projects: $projects"
Write-Host "projectsInclude: $projectsInclude"
Write-Host "projectsExclude: $projectsExclude"

$locationSave = Get-Location 
Write-Host "locationSave: $locationSave"

Write-Host "projectPaths:"
$projectPaths = GetFolders -path $path -filter $projects -include $projectsInclude -exclude $projectsExclude -depth $depth
foreach($projectPath in $projectPaths)  {
    Write-Host ""
    Write-Host "------------------------------------------------------"
    Write-Host "npm install '$($projectPath.FullName)' START ---------"
    Set-Location "$($projectPath.FullName)"; Write-Host "Set-Location '$($projectPath.FullName)' completed"

    try {
        Write-Host "before npm install"
        npm install; Write-Host "npm install completed"
    } catch {
        Write-Host "##vso[task.logissue type=warning]failure on npm install '$($projectPath.FullName)'."
        # Clear-Variable error -Force
        $error.Clear()
        $res = 0;
    }
    Write-Host "npm install '$($projectPath.FullName)' STOP"
    Write-Host "------------------------------------------------------"
}

Set-Location $locationSave
Write-Host "Set-Location '$locationSave' completed - res: $res"

# Write-Host "$version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

# return $res
Exit($res)