
# Logs results to the given path (modify as required)

param($result, $message, [bool]$flag, $executionpolicy)  
$logfile = "TestPSOutput.txt"
$logdir = "C:\Temp\Certify\TestOutput\"
$logpath = $logdir+$logfile

if (!(Test-Path $logdir)) {
  New-Item -ItemType "directory" -Path $logdir
}

if (!(Test-Path $logpath)) {
  Write-Warning "$logpath does not exist, creating file"
  New-Item -ItemType "file" -Path $logdir -Name $logfile
}

$date = Get-Date

Add-Content $logpath ("-------------------------------------------------");
Add-Content $logpath ("Script Run Date: " + $date)
Add-Content $logpath ($result | ConvertTo-Json)
Add-Content $logpath ("Message: " + $message)
Add-Content $logpath ("Flag: " + $flag)
Add-Content $logpath ("ExecutionPolicyParameter: " + $executionpolicy)
Add-Content $logpath ("PowerShellVersion: " + $PSVersionTable.PSVersion.ToString())

