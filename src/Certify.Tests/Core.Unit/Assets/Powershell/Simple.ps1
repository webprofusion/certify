
param($result)

Start-Transcript -Path C:\Temp\Certify\TestOutput\TestTranscript.txt

Get-Help Get-Item

Add-Content C:\Temp\Certify\TestOutput\TestPSOutput.txt "Testing 123"
Stop-Transcript
