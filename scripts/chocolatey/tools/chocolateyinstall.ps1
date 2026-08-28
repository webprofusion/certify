$ErrorActionPreference = 'Stop'

# Installs Certify Certificate Manager from the signed Inno Setup installer on the
# official release channel.
#
# Generated: rendered from certify-internal\setup\chocolatey by
# Update-Chocolately.ps1, which fills in the installer download URL and checksum
# for the published release. Edit the template, not this copy.

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  fileType       = 'exe'
  url64bit       = 'https://downloads.certifytheweb.com/release/7.2.0.0/certify-ccm-windows-x64-7.2.0.0.exe'
  checksum64     = 'c2566a368338fb0b28bcbb5d76ad99f7d715218e7e5670ddff63e0c8f03c642e'
  checksumType64 = 'sha256'
  # matches the Add/Remove Programs display name written by the Inno Setup installer
  softwareName   = 'Certify Certificate Manager*'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
