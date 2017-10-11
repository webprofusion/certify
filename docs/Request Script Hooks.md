# Request Script Hooks

Certify is extendable via PowerShell scripts which can be configured to run before or after the Certificate Request. The scripts are provided a parameter `$result` which contains the status and details of the site whose certificate is being requested. You may execute any commands that the user running the test script has access to, including creating new processes, or using other command line tools.

## Script Basics

Here is a sample script which demonstrates a few commonly accessed pieces of information:

```PowerShell
param($result)   # required to access the $result parameter

# either $true or $false
$result.IsSuccess

# object containing all information Certify has about the saved Site
$result.ManagedItem

# the IIS site ID
$result.ManagedItem.GroupId   # ex: 1, 2, 3, ...

# the IIS site directory
$result.ManagedItem.RequestConfig.WebsiteRootPath   # ex: "C:\inetpub\wwwroot"

# the path to the created/renewed certificate PFX file
$result.MangagedItem.CertificatePath   # ex: "C:\ProgramData\ACMESharp\sysVault\99-ASSET\cert_ident8cf8bd9c-all.pfx"

# set to $true in a pre-request hook to prevent the certificate from 
# being requested (has no effect in post-request hooks)
$result.Abort = $false
```

## Pre-Request Script Hooks

Notes: Pre-request scripts are executed immediately before the Certificate Request is about to be made (including the challenge file configuration checks).

* The `$result.IsSuccess` value will always be `$false`. 
* If for some reason your script would like to prevent the Certificate Request from being executed, you may set `$result.Abort` to `$true` and the site your script was executed for will be skipped.

### Example: Update IIS site root directory dynamically
```PowerShell
param($result)
$id = $result.ManagedItem.GroupId
$domain = $result.ManagedItem.RequestConfig.PrimaryDomain
[xml]$xml = . "$env:windir\system32\inetsrv\appcmd" list config /section:system.applicationHost/sites
$path = $xml.SelectSingleNode("//site[@id=$id]/application[@path='/']/virtualDirectory/@physicalPath").Value
$result.ManagedItem.RequestConfig.WebsiteRootPath = $path
write-output "Updated Path [$domain]: $path"
```

## Post-Request Script Hooks

Notes: Post-request scripts are executed immediately after the Certificate Request was completed, whether or not the request was successful and the certificate was automatically installed and configured according to the site configuration within Certify.
* The `$result.IsSuccess` value indicates whether or not the Certificate Request was successfully completed.
* The `$result.Message` value provides a message describing the reason for failure, or a message indicating success

### Example: Send email via Gmail after unsuccessful renewal

```PowerShell
param($result)
if (!$result.IsSuccess) {
   $EmailFrom = "username@gmail.com"
   $EmailTo = "username@gmail.com" 
   $Subject = "Cert Request Failed: " + $result.ManagedItem.RequestConfig.PrimaryDomain
   $Body = "Error: " + $result.Message 
   $SMTPServer = "smtp.gmail.com" 
   $SMTPClient = New-Object Net.Mail.SmtpClient($SmtpServer, 587) 
   $SMTPClient.EnableSsl = $true 
   $SMTPClient.Credentials = New-Object System.Net.NetworkCredential("username@gmail.com", "password"); 
   $SMTPClient.Send($EmailFrom, $EmailTo, $Subject, $Body)
   write-output "Sent notification email"
}
```

### Example: Restart RRAS after successful certificate renewal

```PowerShell
param($result)
if ($result.IsSuccess -and $result.ManagedItem.GroupId -eq 1) {
   write-output "Restarting RRAS..."
   Net Stop RemoteAccess
   Net Start RemoteAccess
   write-output "Done"
}
```

### Example: Convert CNG certificate storage to CSP (for Exchange 2013)

```PowerShell
param($result)
$tempfile = "$env:TEMP\CertifyTemp.pfx"
$pfx = get-pfxcertificate -filepath $result.ManagedItem.CertificatePath
certutil -f -p Certify -exportpfx $pfx.SerialNumber $tempfile
certutil -delstore my $pfx.SerialNumber
certutil -p Certify -csp "Microsoft RSA SChannel Cryptographic Provider" -importpfx $tempfile
remove-item $tempfile
```

### Example: update Remote Desktop Role Certificates
(switches to 64-bit powershell to import the 64-bit RemoteDesktop module)
```PowerShell
param($result)
set-alias ps64 "$env:windir\sysnative\WindowsPowerShell\v1.0\powershell.exe" 
ps64 -args $result -command {
   $result = $args[0]
   $pfxpath = $result.ManagedItem.CertificatePath
   Import-Module RemoteDesktop
   Set-RDCertificate -Role RDPublishing -ImportPath $pfxpath -Force
   Set-RDCertificate -Role RDWebAcces -ImportPath $pfxpath -Force
   Set-RDCertificate -Role RDGateway -ImportPath $pfxpath -Force
   Set-RDCertificate -Role RDRedirector -ImportPath $pfxpath -Force
}
```

## Troubleshooting

<img src="images/testing request script hooks.png">

* In the Certify UI, you may test scripts by clicking the "Test" button after entering the script filename for the hook you would like to test. 
* For a testing pre-request script, the `$result.IsSuccess` value will be `$false`, and for a post-request script the value will be `$true`. 
* The `$result.MangagedItem.CertificatePath` value be set to the filename (including path) of the PFX file containing the requested certificate, unless the site is new and has not had a successful Certificate Request, in which case the value will not be set.

### Using 64-bit modules

If you attempt to import a module with [`Import-Module`](https://docs.microsoft.com/en-us/powershell/module/microsoft.powershell.core/import-module?view=powershell-5.1) that does not run in 32-bit mode (i.e. RemoteDesktop), you may receive an error like `"Import-Module : The specified module 'RemoteDesktop' was not loaded because no valid module file was found in any module directory."`. To load a 64-bit module you can use this snippet to pass the `$result` object to a script running in 64-bit powershell:
```powershell
param($result)
set-alias ps64 "$env:windir\sysnative\WindowsPowerShell\v1.0\powershell.exe" 
ps64 -args $result -command {
   $result = $args[0]
   write-output $result.IsSuccess
   import-module -name RemoteDesktop
   Set-RDCertificate ...
}
```
