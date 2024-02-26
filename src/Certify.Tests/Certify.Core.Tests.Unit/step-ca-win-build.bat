 mkdir C:\temp
pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://dl.smallstep.com/gh-release/cli/docs-cli-install/v0.24.4/step_windows_0.24.4_amd64.zip' -Outfile 'C:\temp\step_windows_0.24.4_amd64.zip'" && tar -oxzf C:\temp\step_windows_0.24.4_amd64.zip -C "C:\Program Files"
pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://dl.smallstep.com/gh-release/certificates/gh-release-header/v0.24.2/step-ca_windows_0.24.2_amd64.zip' -Outfile 'C:\temp\step-ca_windows_0.24.2_amd64.zip'" && tar -oxzf C:\temp\step-ca_windows_0.24.2_amd64.zip -C "C:\Program Files"
mkdir "C:\Program Files\SDelete" && pwsh -Command "Invoke-WebRequest -Method 'GET' -uri 'https://download.sysinternals.com/files/SDelete.zip' -Outfile 'C:\temp\SDelete.zip'" && tar -oxzf C:\temp\SDelete.zip -C "C:\Program Files\SDelete"
rmdir /s /q C:\temp
