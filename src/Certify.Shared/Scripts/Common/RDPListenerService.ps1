# This is an example script and it will be overwritten when the next update is installed. 
# To use this script copy it to another location and modify as required

# Enable certificate for RDP Listener Service
# For more script info see https://docs.certifytheweb.com/docs/script-hooks.html

param($result)

# Apply certificate
wmic /namespace:\\root\cimv2\TerminalServices PATH Win32_TSGeneralSetting Set SSLCertificateSHA1Hash="$($result.ManagedItem.CertificateThumbprintHash)"