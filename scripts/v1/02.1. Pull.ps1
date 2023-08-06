
Get-Module | Remove-Module
$keys = @('PSBoundParameters','PWD','*Preference') + $PSBoundParameters.Keys 
Get-Variable -Exclude $keys | Remove-Variable -EA 0

$scriptFolder = $($PSScriptRoot.TrimEnd('\'));
Import-Module "$scriptFolder\Common.ps1" 

Set-Location $scriptFolder
$scriptName = $MyInvocation.MyCommand.Name
Start-Transcript -Path "\Logs\$scriptName.log" -Append

# $assemblyInfoFile = "..\Common.Diagnostics\Properties\AssemblyInfo.cs";
# Write-Host "assemblyInfoFile: $assemblyInfoFile"

# $version = GetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion"
# if ($null -eq $version) { throw "cannot find AssemblyVersion attribute in file '$assemblyInfoFile'"; }
# $newVersion = "{0}.{1}.{2}.{3}" -f $version.Major, $version.Minor, $version.Build, ($version.Revision + 1)
# SetVersionAttribute -filePath $assemblyInfoFile -versionAttribute "AssemblyVersion" -version $newVersion

git --version
git config user.email buildagent@microsoft.com
git config user.name "Build Agent" 
Write-Host "before git pull"
git pull -f
Write-Host "after git pull"

Stop-Transcript

return 0;