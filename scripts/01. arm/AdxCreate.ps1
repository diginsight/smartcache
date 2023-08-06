Enable-AzureRmAlias -Scope CurrentUser

# Sign in to your account.
Connect-AzAccount

# Obtain your subscription(s) and their ID(s). The subscription ID is the second column.
Get-AzSubscription

$subscriptionId = '12345678-1234-1234-1234-123412341234'
$resourceGroupName = 'RG01' #Read-Host -Prompt "Enter the Resource Group name"
$location = 'West Europe'
$template_adx = "AdxTemplate.json"
$parameters_adx = "AdxParameters.json"

# Get subscr context
$context = Get-AzSubscription -SubscriptionId $subscriptionId
Set-AzContext $context

# Set Resource Group
# Set-AzDefault -ResourceGroupName $resourceGroupName

New-AzureRmResourceGroup -Location $location -Name $resourceGroupName
New-AzureRmResourceGroupDeployment -ResourceGroupName $resourceGroupName  -TemplateUri $template_adx -TemplateParameterFile $parameters_adx 
#-Debug
