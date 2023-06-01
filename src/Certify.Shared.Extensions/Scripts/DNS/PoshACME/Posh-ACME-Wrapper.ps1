# Posh-ACME Wrapper script to allow direct use of DNS Plugins

# $PoshACMERoot = "\Posh-ACME"
$Public  = @( Get-ChildItem -Path $PoshACMERoot\Public\*.ps1 -ErrorAction Ignore )
$Private = @( Get-ChildItem -Path $PoshACMERoot\Private\*.ps1 -ErrorAction Ignore )

# default to TLS 1.2 and TLS 1.3, but just use TLS 1.2 if this machine doesn't understand TLS 1.3

try {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12 -bor [System.Net.SecurityProtocolType]::Tls13
} catch {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}

# iwr https://tls13.1d.pw # TLS 1.3 test

# Load Assembly without using Add-Type to avoid locking assembly dll
$bcPath = "$($PoshACMERoot)/../../../BouncyCastle.Cryptography.dll"
If (Test-Path -Path $bcPath -PathType Leaf -ne $true)
{
    $bcPath =   "$($PoshACMERoot)\lib\BC.Crypto.1.8.8.2-netstandard2.0.dll"

    If (Test-Path -Path $bcPath -PathType Leaf -ne $true){
        Write-Error "Unable to find BouncyCastle dll at $bcPath"
        Exit 1
    }
}

$assemblyBytes = [System.IO.File]::ReadAllBytes($bcPath)
[System.Reflection.Assembly]::Load($assemblyBytes) | out-null

# Dot source the files (in the same manner as Posh-ACME would)
Foreach($import in @($Public + $Private))
{
    Try { . $import.fullname }
    Catch
    {
        Write-Error -Message "Failed to import function $($import.fullname): $_"
    }
}

# Replace Posh-ACME specific methods which don't apply when we're using them
function Export-PluginVar { param([Parameter(ValueFromRemainingArguments)]$DumpArgs) }
function Import-PluginVar { param([Parameter(ValueFromRemainingArguments)]$DumpArgs) }

$script:UseBasic = @{} 
if ('UseBasicParsing' -in (Get-Command Invoke-WebRequest).Parameters.Keys) {  $script:UseBasic.UseBasicParsing = $true } 
