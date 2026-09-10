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
  url64bit       = 'https://downloads.certifytheweb.com/release/7.2.1.0/certify-ccm-windows-x64-7.2.1.0.exe'
  checksum64     = '2c787e4d21d68612e77579a9c1513319dcee4589db188fe87f1022fcaf5b9f35'
  checksumType64 = 'sha256'
  # matches the Add/Remove Programs display name written by the Inno Setup installer
  softwareName   = 'Certify Certificate Manager*'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

Install-ChocolateyPackage @packageArgs
