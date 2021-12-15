
# Logs results to the given path (modify as required)

param($result)  
$logpath = "C:\Temp\Certify\TestOutput\TestPSOutput.txt"

$date = Get-Date

Add-Content $logpath ("-------------------------------------------------");
Add-Content $logpath ("Script Run Date: " + $date)
Add-Content $logpath ($result | ConvertTo-Json)

