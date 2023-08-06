Param(
  $templateFile = "azuredeploy.json",
  $templateName = "azuredeploy",
  $parameterFile = "aruredeploy.parameters.json"
)

$templateName = $templateFile -replace ".json", ""
$today = Get-Date -Format "yyyy-MM-dd"
$deploymentName = $templateName + "$today"

New-AzResourceGroupDeployment `
  -Name $deploymentName `
  -TemplateFile $templateFile `
  -TemplateParameterFile $parameterFile



# $templateFile = "azuredeploy.json"
# $parameterFile="azuredeploy.parameters.dev.json"
# $today=Get-Date -Format "MM-dd-yyyy"
# $deploymentName="addParameterFile-"+"$today"
# New-AzResourceGroupDeployment `
#   -Name $deploymentName `
#   -TemplateFile $templateFile `
#   -TemplateParameterFile $parameterFile