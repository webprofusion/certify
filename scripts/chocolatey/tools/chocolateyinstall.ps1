$ErrorActionPreference = 'Stop';
$toolsDir   = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$url64      = 'https://certifytheweb.s3.amazonaws.com/downloads/CertifyTheWebSetup.exe'

$packageArgs = @{
  packageName   = $env:ChocolateyPackageName
  unzipLocation = $toolsDir
  fileType      = 'exe'
  url64bit      = $url64
  softwareName  = 'Certify The Web*'
  checksum64    = 'a43113abacf81ffcf725602f1b4cf7c9f239c81ac135fd3a4af4804ddccca84f'
  checksumType64= 'sha256'
  validExitCodes= @(0)
  silentArgs   = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
}

Install-ChocolateyPackage @packageArgs
