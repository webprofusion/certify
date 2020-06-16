# $PoshACMERoot = "\Posh-ACME"
$Public  = @( Get-ChildItem -Path $PoshACMERoot\Public\*.ps1 -ErrorAction Ignore )
$Private = @( Get-ChildItem -Path $PoshACMERoot\Private\*.ps1 -ErrorAction Ignore )


Add-Type -Path "$($PoshACMERoot)\..\..\..\BouncyCastle.Crypto.dll"

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
function Export-PluginVar {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory,Position=0)]
        [string]$VarName,
        [Parameter(Mandatory,Position=1)]
        [object]$VarValue
    )
 }


$script:UseBasic = @{} 
if ('UseBasicParsing' -in (Get-Command Invoke-WebRequest).Parameters.Keys) {  $script:UseBasic.UseBasicParsing = $true } 