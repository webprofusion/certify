$ErrorActionPreference = 'Stop';
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = 'https://certifytheweb.s3.amazonaws.com/downloads/archive/CertifyTheWebSetup_V5.6.3.exe'

$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  fileType      = 'exe'
  url64bit      = $url64
  softwareName  = 'Certify The Web*'
  checksum64    = '2e98ae4bbd1fcd2582b64d7ac274c1409ac0eb2bdd4aa06da06b10c4676f6022'
  checksumType64= 'sha256'
  validExitCodes= @(0)
  silentArgs   = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
}

Install-ChocolateyPackage @packageArgs
