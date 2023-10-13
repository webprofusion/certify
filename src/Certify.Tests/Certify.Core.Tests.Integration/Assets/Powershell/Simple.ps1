
# Logs results to the given path (modify as required)

param($result)  
$logfile = "TestPSOutput.txt"
$logdir = "C:\Temp\Certify\TestOutput\"
$logpath = $logdir+$logfile

if (!(Test-Path $logpath)) {
  Write-Warning "$logpath does not exist, creating directories/file"
  New-Item -ItemType "directory" -Path $logdir
  New-Item -ItemType "file" -Path $logdir -Name $logfile
}

$date = Get-Date

Add-Content $logpath ("-------------------------------------------------");
Add-Content $logpath ("Script Run Date: " + $date)
Add-Content $logpath ($result | ConvertTo-Json)

