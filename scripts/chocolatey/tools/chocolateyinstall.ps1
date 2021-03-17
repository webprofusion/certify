$ErrorActionPreference = 'Stop';
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = 'https://certifytheweb.s3.amazonaws.com/downloads/archive/CertifyTheWebSetup_V5.3.5.exe'

$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  fileType      = 'exe'
  url64bit      = $url64
  softwareName  = 'Certify The Web*'
  checksum64    = 'de82bba495864ea58bb1c1c843bab34a6480fad741e7417d34ad810027355bbb'
  checksumType64= 'sha256'
  validExitCodes= @(0)
  silentArgs   = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
}

Install-ChocolateyPackage @packageArgs
