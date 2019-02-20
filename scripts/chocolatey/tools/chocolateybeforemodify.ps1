# This runs in 0.9.10+ before upgrade and uninstall.
# Use this file to do things like stop services prior to upgrade or uninstall.
# NOTE: It is an anti-pattern to call chocolateyUninstall.ps1 from here. If you
#  need to uninstall an MSI prior to upgrade, put the functionality in this
#  file without calling the uninstall script. Make it idempotent in the
#  uninstall script so that it doesn't fail when it is already uninstalled.
# NOTE: For upgrades - like the uninstall script, this script always runs from 
#  the currently installed version, not from the new upgraded package version.

# Close the UI window if currently open
Get-Process | Where-Object {$_.ProcessName -eq 'Certify.UI'} | Foreach-Object { $_.CloseMainWindow() | Out-Null } | stop-process –force

# Stop the Certify.Service background service
Get-Service Certify.Service | Where {$_.status –eq 'Running'} |  Stop-Service
