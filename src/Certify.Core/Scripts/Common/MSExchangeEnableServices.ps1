# This script enables the use of the newly retrieved and stored certificate with common Exchange services
# For more script info see https://github.com/webprofusion/certify/blob/master/docs/Request%20Script%20Hooks.md

param($result)

# tell Exchange which services to use this certificate for
Enable-ExchangeCertificate -Thumbprint $result.ManagedItem.CertificateThumbprintHash -Services POP,IMAP,SMTP,IIS