# Runs before upgrade and uninstall, from the currently installed package version
# (not the incoming one). Used to release file locks on the install folder by
# closing the desktop UI and stopping the background service.
#
# Best effort only - never fail the upgrade/uninstall from here.

$ErrorActionPreference = 'Continue'

# Close the desktop UI if it is running ('Certify.UI' is the pre-v7 process name)
foreach ($processName in @('Certify.UI.Desktop', 'Certify.UI')) {
  foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
    Write-Host "Closing $($process.ProcessName) (PID $($process.Id))..."
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(10000)) {
      Write-Host "$($process.ProcessName) did not exit, stopping it."
      Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
  }
}

# Stop the background service ('Certify.Service' is the pre-v7 service name)
foreach ($serviceName in @('Certify Management Agent', 'Certify.Service')) {
  $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
  if ($service -and $service.Status -ne 'Stopped') {
    Write-Host "Stopping service '$serviceName'..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
  }
}
