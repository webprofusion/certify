# This is an example script and it will be overwritten when the next update is installed. 
# To use this script copy it to another location and modify as required

# Set certificate for Web Management Service in port 8172 (Web Deploy etc)
# For more script info see https://docs.certifytheweb.com/docs/script-hooks.html

param($result)

$thumb = $result.ManagedItem.CertificateThumbprintHash

# get a new guid:
$guid = [guid]::NewGuid()

# remove the previous certificate:
& netsh http delete sslcert ipport=0.0.0.0:8172

# set the current certificate:
& netsh http add sslcert ipport=0.0.0.0:8172 certhash=$thumb appid=`{$guid`}

# TODO: does management service need restarted?