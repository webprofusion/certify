param ([int]$Days=7, [switch]$Force)

# Certify The Web - App Updater Script
# Schedule this powershell script task using an Administrator account for automatic update N days after the last official release

$installAfterNDays = $Days
$forceInstall = $Force
$scriptName = "[Certify The Web - App Update Script]"


# default to TLS 1.2

[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

$installedVersion = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* | Where-Object DisplayName -Match "^Certify The Web.*"
$apiUrl = "https://api.certifytheweb.com/v1/update?context=autoupdate&version=" + $installedVersion.DisplayVersion

$updateInfo = Invoke-WebRequest -Uri $apiUrl -UseBasicParsing | ConvertFrom-Json
$versionMajor = $updateInfo.version.major
$versionMinor = $updateInfo.version.minor
$versionPatch = $updateInfo.version.patch

$updateVersionString = "$versionMajor.$versionMinor.$versionPatch"

$releaseDateString = $updateInfo.message.releaseNotes[0].releasedate
$releaseDate = [datetime]::ParseExact($releaseDateString, 'yyyy/MM/dd', $null)

# update is considered stable if release date was more than N days ago
$updateDateIsStable = $releaseDate -lt ((Get-Date).AddDays(-$installAfterNDays))

# got update info, check our installed version
# if the installed version is different from the available stable update (or force install is enabled) proceed with update

if (($installedVersion -and $installedVersion.DisplayVersion -ne $updateVersionString -and $updateDateIsStable) -or $forceInstall ) {
    $installedVersionString = $installedVersion.DisplayVersion
    if ($forceInstall -eq $True) {
        Write-Output "$scriptName : Forced install, update may not be required."
    }
    else {
        Write-Output "$scriptName : Update required. Performing update from  v${installedVersionString} to v${updateVersionString}"
    }

    # download update to current users downloads folder
    $filename = [System.IO.Path]::GetFileName($updateInfo.message.downloadFileURL)

    # create random temp folder
    $userTempFolder = [System.IO.Path]::GetTempPath()
    [string] $randomFolderName = [System.Guid]::NewGuid()
    $randomTempFolder = Join-Path $userTempFolder $randomFolderName
    New-Item -ItemType Directory -Path $randomTempFolder
    
    $setupFile = Join-Path $randomTempFolder $filename
    Write-Output "Downloading to temp path ${setupFile}"
    Invoke-WebRequest -Uri $updateInfo.message.downloadFileURL -OutFile $setupFile

    # computer checksum of downloaded file
    $downloadHash = Get-FileHash $setupFile -Algorithm SHA256

    # if checksum matches, proceed with update install
    if ($downloadHash.Hash -eq $updateInfo.message.sha256) {
    
        # Close the UI window if currently open
        Get-Process | Where-Object { $_.ProcessName -eq 'Certify.UI' } | Foreach-Object { $_.CloseMainWindow() | Out-Null } | stop-process –force

        # Stop the Certify.Service background service
        Get-Service -Name "Certify.Service" | Where-Object { $_.status –eq 'Running' } |  Stop-Service

        # Run installer
        Start-Process -Wait -FilePath $setupFile -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"

        # cleanup
        Remove-Item -Path $setupFile
        Remove-Item -Path $randomTempFolder

        Write-Output "$scriptName : Update completed to v${updateVersionString}"
    }
    else {
        Write-Error "$scriptName : Download checksum does not match published version. Update will not continue."
    }
    
}
else {
    Write-Output "$scriptName : Update not required (current release is v${updateVersionString})"
}
