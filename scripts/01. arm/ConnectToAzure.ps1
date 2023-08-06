# Sign in to your account.
Connect-AzAccount

# Obtain your subscription(s) and their ID(s). The subscription ID is the second column.
Get-AzSubscription

$subscriptionId = '12341234-1234-1234-1234-123412341234'
$resourceGroupName = 'dev-samples-101a-rg' #Read-Host -Prompt "Enter the Resource Group name"
$location = 'West Europe'
$template_adx = ".\\template.json"
$parameters_adx = ".\\parameters.json"

# Get subscr context
$context = Get-AzSubscription -SubscriptionId $subscriptionId
Set-AzContext $context

# Set Resource Group
Set-AzDefault -ResourceGroupName $resourceGroupName

New-AzureRmResourceGroup -Location $location -Name $resourceGroupName
New-AzureRmResourceGroupDeployment -ResourceGroupName $resourceGroupName  -TemplateUri $template_adx -TemplateParameterFile $parameters_adx 
#-Debug
