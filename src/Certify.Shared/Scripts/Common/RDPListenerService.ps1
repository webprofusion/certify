# Enable certificate for RDP Listener Service
# For more script info see https://github.com/webprofusion/certify/blob/master/docs/Request%20Script%20Hooks.md

param($result)

# Apply certificate
wmic /namespace:\\root\cimv2\TerminalServices PATH Win32_TSGeneralSetting Set SSLCertificateSHA1Hash="$($result.ManagedItem.CertificateThumbprintHash)"