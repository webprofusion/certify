# Guide to using Azure DNS

Created by: Tony Johncock @Tony1044

## Step 1 – Install and configure Azure PowerShell

Follow the instructions here: https://docs.microsoft.com/en-us/powershell/azure/install-azurerm-ps?view=azurermps-5.7.0

## Step 2 – Connect to Azure PS and create the Azure Service Principal and Enterprise Application
From PowerShell:
PS C:\Users\Tony> Connect-AzureRmAccount

This will launch a web dialog to log into your Azure tenant. Ensure you connect with an account with the relevant administrative credentials in the portal.

Pop your password and MFA requirements in as required when prompted.

Note: I found that this wouldn’t authenticate via the ageing proxy server on one site, with the rather esoteric error as below:

```
Connect-AzureRmAccount : An error occurred while sending the request.
At line:1 char:1
+ Connect-AzureRmAccount
+ ~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : CloseError: (:) [Connect-AzureRmAccount], HttpRequestException
    + FullyQualifiedErrorId : Microsoft.Azure.Commands.Profile.ConnectAzureRmAccountCommand
```

Once connected, create the Application and Service Principal
Run the following script:

```powershell
$azureAccountName ="user@domain.com"
$azurePassword = ConvertTo-SecureString "your secure password" -AsPlainText -Force

$psCred = New-Object System.Management.Automation.PSCredential($azureAccountName, $azurePassword) 

New-AzureRmADServicePrincipal -DisplayName LetsEncrypt -Password $psCred
```

Once this has successfully run, you need to retrieve the ApplicationID:

```powershell
Get-AzureRmADApplication | Select-Object displayname, objectid, applicationid
```

It returns something like the following:

```
DisplayName    ObjectId                             ApplicationId                       
-----------    --------                             -------------                       
LetsEncrypt    7f64adcf-xxxx-yyyy-zzzz-aabbccddeeff aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee
```

Make a note of the ApplicationID

This will have created a service principal and an underlying Azure application.

## 3 - Grant the Application rights to update DNS
- Login to portal.azure.com from a web browser
- Click on your DNS Zone
- Click on Access Control (IAM)
- Click on (+) Add
- Select:
    - Role: DNS Zone Contributor
    - Assign access to: Azure AD user, group or application
    - Select: Type in LetsEcnrypt
    - Click Save

## 4 - Create Service Principal Secret

From the Azure portal, click Azure Active Directory:

- Click App Registrations
- Click Show all Applications
- Click LetsEncrypt
- Click Settings
- Click Keys
- Type a key description, choose when it will expire (or never – your choice) and click save.

*IMPORTANT: The secret is only shown at this point. Copy it as once it’s hidden there is NO way to retrieve it*

## 5 – Retrieve Tenant ID
There are any number of ways to get the tenant ID, but since we’re already in PowerShell:

```powershell
Get-AzureRmTenant

Id        : xxxxxxxx-yyyy-zzzz-aaaa-bbbbbbbbbbbb
Directory : somedomain.com
```
 
6 – Configure Credentials in Certify SSL Manager

You now have all the information you require to configure Azure settings in the app. 

You can add this is a new Stored Credential under Settings or while you are editing a Managed Certificate, under Authorization > DNS. 

When using the credential as part of DNS validation in the app you will be prompted for the "Zone Id", for Azure DNS this is the DNS zone name, usually in the form of "yourdomain.com"
