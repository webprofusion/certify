# # This is a legacy example script and it will be overwritten when the next update is installed. 
# To use this script copy it to another location and modify as required

param($result)

# script example adapted from https://community.certifytheweb.com/t/filezilla-server-ps-script/141/8

# Alias to your OpenSSL install
set-alias opensslcmd "C:\Tools\OpenSSL\openssl.exe" 

# Set paths to where keys will be saved. 
# This will vary depending on your required configuration. File name are not that important but your config must point to the same filenames.

$keypath = "C:\apache\conf\mydomain.com\"
$key = $keypath + "cert.key"
$rsakey = $keypath + "cert_rsa.key"
$pem = $keypath + "cert.pem"

# Get the latest PFX file path
$sourcepfx = $result.ManagedItem.CertificatePath

# Create the Key, RSA Key, and PEM file. Use the RSA Key & PEM for FileZilla
opensslcmd pkcs12 -in $sourcepfx -out $key -nocerts -nodes -passin pass:
opensslcmd rsa -in $key -out $rsakey
opensslcmd pkcs12 -in $sourcepfx -out $pem -nokeys -clcerts -passin pass:

# optional: restart the Apache service (example)
Restart-Service -Name Apache2.4 -Force
