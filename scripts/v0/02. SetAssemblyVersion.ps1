
Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

$assemblyInfoFile = "..\Common.Diagnostics\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"

# $version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
# if ($null -eq $version) { throw "cannot find AssemblyVersion attribute in file '$assemblyInfoFile'"; }
# $newVersion = "{0}.{1}.{2}.{3}" -f $version.Major, $version.Minor, $version.Build, ($version.Revision + 1)
# SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $newVersion

$version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
if ($null -eq $version) { throw "cannot find AssemblyVersion attribute in file '$assemblyInfoFile'"; }
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.v2\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Full\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Core\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Win\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Log4net\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.Serilog\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

$assemblyInfoFile = "..\Common.Diagnostics.AppInsights\Properties\AssemblyInfo.cs";
Write-Host "assemblyInfoFile: $assemblyInfoFile"
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $version
SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyFileVersion" -version $version

Write-Host "version: $version"
Write-Host "##vso[task.setvariable variable=version;isOutput=true]$version"

Stop-Transcript

