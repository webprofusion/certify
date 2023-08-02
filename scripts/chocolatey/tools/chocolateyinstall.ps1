$ErrorActionPreference = 'Stop';
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = 'https://certifytheweb.s3.amazonaws.com/downloads/archive/CertifyTheWebSetup_V6.0.10.exe'

$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  fileType      = 'exe'
  url64bit      = $url64
  softwareName  = 'Certify The Web*'
  checksum64    = 'cebdc7c82e871aca4a0e116c2655ed3f264ab26b54b460d51a0e8d0d3f767e25'
  checksumType64= 'sha256'
  validExitCodes= @(0)
  silentArgs   = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
}

Install-ChocolateyPackage @packageArgs
