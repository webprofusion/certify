# example script to copy the output PFX to a UNC path for Central Certificate Store
# Enabling CCS: https://techcommunity.microsoft.com/t5/iis-support-blog/central-certificate-store-ccs-with-iis/ba-p/377274
$result = Get-Content -Raw -Path C:\temp\ps-test.json | ConvertFrom-Json

$CcsPath="\\wbp-desktop06\ccs"
$CcsServer="wbp-desktop06"

if ($result.IsSuccess -eq $true) {

    # connect network share with credentials
    net use $CcsPath /USER:wbp-desktop06\testuser password1

    # example file copy where cert contains webmail.example.com and autodiscover.example.com domains
    Copy-Item -Path $result.ManagedItem.CertificatePath -Destination $CcsPath\webmail.example.com.pfx -Force
    Copy-Item -Path $result.ManagedItem.CertificatePath -Destination $CcsPath\autodiscover.example.com.pfx -Force

    # disconnect network share
    net use $CcsPath /delete
}