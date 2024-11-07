# # This is a legacy example and it will be overwritten when the next update is installed. 
# To use this script copy it to another location and modify as required

# Enable certificate for RDP Listener Service
# For more script info see https://docs.certifytheweb.com/docs/script-hooks.html

param($result)

# Apply certificate
if (Get-Command wmic -errorAction SilentlyContinue)
{
    # Beginning with Windows Server 2025, WMIC is available as a feature on demand.
    wmic /namespace:\\root\cimv2\TerminalServices PATH Win32_TSGeneralSetting Set SSLCertificateSHA1Hash="$($result.ManagedItem.CertificateThumbprintHash)"
}
else
{
    # For new development, use the CIM cmdlets instead.
    $instance = Get-CimInstance -ClassName Win32_TSGeneralSetting -Namespace root/cimv2/TerminalServices
    Set-CimInstance -InputObject $instance -Property @{SSLCertificateSHA1Hash=$result.ManagedItem.CertificateThumbprintHash}
}
