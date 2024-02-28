# This is a legacy example script and it will be overwritten when the next update is installed. 
# To use this script copy it to another location and modify as required

# Logs results to the given path (modify as required)

param($result)  
$logpath = "c:\temp\ps-test.txt"

$date = Get-Date

Add-Content $logpath ("-------------------------------------------------");
Add-Content $logpath ("Script Run Date: " + $date)
Add-Content $logpath ($result | ConvertTo-Json)

