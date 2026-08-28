$ErrorActionPreference = 'Stop'

# Uninstalls Certify Certificate Manager using the Inno Setup uninstaller recorded
# in the Windows uninstall registry. That uninstaller stops and removes the
# 'Certify Management Agent' service; configuration and certificate data under
# %ProgramData%\Certify are intentionally left in place.

$softwareName = 'Certify Certificate Manager*'

$packageArgs = @{
  packageName    = $env:ChocolateyPackageName
  softwareName   = $softwareName
  fileType       = 'exe'
  silentArgs     = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-'
  validExitCodes = @(0)
}

[array]$key = Get-UninstallRegistryKey -SoftwareName $softwareName

if ($key.Count -eq 1) {
  $packageArgs['file'] = "$($key[0].UninstallString)"
  Uninstall-ChocolateyPackage @packageArgs
}
elseif ($key.Count -eq 0) {
  Write-Warning "$($packageArgs['packageName']) has already been uninstalled by other means."
}
else {
  Write-Warning "$($key.Count) matches found!"
  Write-Warning "To prevent accidental data loss, no programs will be uninstalled."
  Write-Warning "Please alert the package maintainer that the following keys were matched:"
  $key | ForEach-Object { Write-Warning "- $($_.DisplayName)" }
}
