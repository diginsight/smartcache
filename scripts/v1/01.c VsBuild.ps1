[CmdletBinding()]
param (
    [string] $path,
    [string] $projects,
    [string] $projectsInclude = $null,
    [string] $projectsExclude = $null,
    [string] $buildConfiguration = "Release",
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

$buildSourcesDirectory = $env:BUILD_SOURCESDIRECTORY
if ([string]::IsNullOrEmpty($buildSourcesDirectory)) { $buildSourcesDirectory = ".." }
Set-Location "$buildSourcesDirectory"

if ([string]::IsNullOrEmpty($path)) { $path = ".." }
if ([string]::IsNullOrEmpty($projects)) { $projects = "ClientApp" }

Write-Host "projects: $projects"
Write-Host "projectsInclude: $projectsInclude"
Write-Host "projectsExclude: $projectsExclude"

$locationSave = Get-Location 

Write-Host ""
Write-Host "Get Visual Studio path."
$vsCommonTools = $env:VS140COMNTOOLS; Write-Host -ForegroundColor Green "vsCommonTools: $vsCommonTools"
$parentPath = Resolve-Path -Path "$vsCommonTools\..\..\.."; Write-Host -ForegroundColor Green "parentPath: $parentPath"
$vsBaseLookupPath = "$parentPath\Microsoft Visual Studio\2019\Enterprise"; Write-Host -ForegroundColor Green "vsBaseLookupPath: $vsBaseLookupPath"
$vsPath = gci -Recurse -Path $vsBaseLookupPath -Filter devenv.com | select -f 1 -ExpandProperty FullName
if ($null -eq $vsPath) {
    Write-Error "Cannot locate devenv.exe under $vsBaseLookupPath"
    Exit(-1)
}
Write-Host -ForegroundColor Green "vsPath:$vsPath"

Write-Host "Locate DisableOutOfProcBuild to allow installer compilation from console."
$outOfProcBuildToolExe = "DisableOutOfProcBuild.exe"
$outOfProcBuildTool = gci -Recurse -Path $vsBaseLookupPath -Filter $outOfProcBuildToolExe | select -f 1 -Property Directory,FullName; Write-Host -ForegroundColor Green "outOfProcBuildTool: $outOfProcBuildTool"
if ($null -eq $outOfProcBuildTool) {
    Write-Warning "Cannot locate $outOfProcBuildToolExe. Setup compilation might fail while building from command line"
} else {
    Write-Host -ForegroundColor White "Running $outOfProcBuildToolExe to allow building setup project from command line.."
    $curLoc = Get-Location
    Set-Location $outOfProcBuildTool.Directory.FullName
    & "$($outOfProcBuildTool.FullName)"
    Set-Location $curLoc
}

Write-Host "projectPaths:"
$projectPaths = GetFiles -path $path -filter $projects -include $projectsInclude -exclude $projectsExclude -depth $depth
foreach($projectPath in $projectPaths)  {

    Write-Host ""
    Write-Host "------------------------------------------------------"
    Write-Host "build '$($projectPath.FullName)' START ---------------" 
    # Set-Location "$($projectPath.FullName)"; Write-Host "Set-Location '$($projectPath.FullName)' completed"

    # Write-Host -ForegroundColor White "Building:`r`n- solution: $BuildSolution`r`n- project: $BuildProject`r`n- configuration: $buildConfiguration`r`n"
    Write-Host "&""$vsPath"" ""$($projectPath.FullName)"" /Build $buildConfiguration"
    Invoke-Expression "&""$vsPath"" ""$($projectPath.FullName)"" /Build $buildConfiguration"   #/Project $BuildProject

    Write-Host "build '$($projectPath.FullName)' END"
    Write-Host "------------------------------------------------------"
}

if ($?) {
    Write-Host -ForegroundColor Green "Build success."
} else {
    Write-Host -ForegroundColor Red "Build failed."
}



Set-Location $locationSave


# Write-Host "$version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

