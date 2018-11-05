# Enable certificate for RDP Gateway
# For more script info see https://docs.certifytheweb.com/docs/script-hooks.html

param($result)

Import-Module RemoteDesktopServices

# Apply certificate
Set-Item -Path RDS:\GatewayServer\SSLCertificate\Thumbprint -Value  $result.ManagedItem.CertificateThumbprintHash -ErrorAction Stop

# Optionally restart TSGateway (Note: uncomment to apply)
# Restart-Service TSGateway -Force -ErrorAction Stop