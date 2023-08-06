$PackageId = "<PackageId>"
$ApiKey = "<ApiKey>"

$json = Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$PackageId/index.json" | ConvertFrom-Json

foreach($version in $json.versions)
{
    Write-Host "Unlisting $PackageId, Ver $version"
    
    Invoke-Expression "dotnet nuget delete $PackageId $version --api-key $ApiKey --source https://www.nuget.org --non-interactive"
    # Invoke-Expression "C:\Tools\Bin\nuget.exe setApiKey $ApiKey"
    # Invoke-Expression ".\nuget.exe delete $PackageId $version $ApiKey -source https://api.nuget.org/v3/index.json -NonInteractive"
    # Invoke-Expression "C:\Tools\Bin\nuget.exe delete $PackageId $version -source https://api.nuget.org/v3/index.json -NonInteractive" # $ApiKey 
}

