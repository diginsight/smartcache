[CmdletBinding()]
param (
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
if ([string]::IsNullOrEmpty($targetFolder)) { $targetFolder = "$buildArtifactStagingDirectory\..\variables" }
Write-Host "reading folder: $targetFolder"

if (!(Test-Path $targetFolder -PathType Container)) { return; }

$files = Get-ChildItem -Path $targetFolder | Select-Object Name

Write-Host ""
Write-Host "files available:"
$files | ForEach-Object -Process { Write-Host "file: '$_'" }


foreach($file in $files) {
    $filePath = "$targetFolder\$($file.Name)"
    $variableName = $file.Name.Substring(0, $file.Name.Length - 4);
    $variableValue = Get-Content -Path $filePath 

    Write-Host "$variableName : $variableValue"
    Write-Host "##vso[task.setvariable variable=$variableName;isOutput=true]$variableValue"
}

Stop-Transcript

