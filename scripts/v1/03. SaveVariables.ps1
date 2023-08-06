[CmdletBinding()]
param (
    $variables,
    $targetFolder
)

Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 
 
Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$buildArtifactStagingDirectory = $($env:BUILD_ARTIFACTSTAGINGDIRECTORY)
if ([string]::IsNullOrEmpty($buildArtifactStagingDirectory)) { $buildArtifactStagingDirectory = "." }
if ([string]::IsNullOrEmpty($targetFolder)) { $targetFolder = $($env:TARGETFOLDER) }
if ([string]::IsNullOrEmpty($targetFolder)) { $targetFolder = "$buildArtifactStagingDirectory\variables" }
# if ([string]::IsNullOrEmpty($variables)) { $variables = "version" }

Write-Host "variables: $variables"
Write-Host "targetFolder: $targetFolder"

if (!(Test-Path $targetFolder -PathType Container)) { New-Item -ItemType Directory -Path $targetFolder -Force }

$variablesNames = $variables.Split('|')
 
Write-Host ""
Write-Host "variablesNames:"
$variablesNames | ForEach-Object -Process { Write-Host "variablesName: '$_'" }

foreach($variableName in $variablesNames) {
    $variableName = $variableName.ToUpper() 
    $variableValue = [System.Environment]::GetEnvironmentVariable($variableName)
    $filePath = "$targetFolder\$variableName.txt"
    Set-Content -Path $filePath -Value $variableValue
    Write-Host "created: '$filePath' with value '$variableValue'"
}

# Write-Host "$version"
# Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

