# Wrapper script to call user scripts with result and parameters marshalled from an encrypted payload file
param(
    $scriptFile, 
    $payloadFile,
    [Parameter(ValueFromRemainingArguments)]
    $additionalParams
)

# Capture the caller's error preference so it can be restored for the target script, then fail fast on any
# error in the wrapper's own setup (payload decode, argument marshalling). This ensures wrapper problems are
# surfaced as a task failure (non-zero exit code) instead of being written to the error stream and ignored.
$__certifyOriginalErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Stop'

$wrappedArguments = @{}

$result = $null

function Join-ByteArrays {
    param(
        [byte[][]]$values
    )

    $length = 0
    foreach ($value in $values) {
        $length += $value.Length
    }

    $output = New-Object byte[] $length
    $offset = 0

    foreach ($value in $values) {
        [System.Buffer]::BlockCopy($value, 0, $output, $offset, $value.Length)
        $offset += $value.Length
    }

    return $output
}

function Test-ByteArrayEqual {
    param(
        [byte[]]$left,
        [byte[]]$right
    )

    if ($null -eq $left -or $null -eq $right -or $left.Length -ne $right.Length) {
        return $false
    }

    $diff = 0
    for ($i = 0; $i -lt $left.Length; $i++) {
        $diff = $diff -bor ($left[$i] -bxor $right[$i])
    }

    return $diff -eq 0
}

function Get-PayloadJson {
    param(
        [string]$path
    )

    $payloadSecret = [System.Environment]::GetEnvironmentVariable("CERTIFY_POWERSHELL_PAYLOAD_SECRET", "Process")
    if ([string]::IsNullOrWhiteSpace($payloadSecret)) {
        throw "PowerShell payload secret was not provided."
    }

    $payloadBytes = [System.Convert]::FromBase64String((Get-Content -Raw -Path $path))
    $version = [System.Text.Encoding]::ASCII.GetBytes("CPS1")

    if ($payloadBytes.Length -lt ($version.Length + 16 + 32)) {
        throw "PowerShell payload is invalid."
    }

    $actualVersion = New-Object byte[] $version.Length
    [System.Buffer]::BlockCopy($payloadBytes, 0, $actualVersion, 0, $version.Length)

    if (-not (Test-ByteArrayEqual $version $actualVersion)) {
        throw "PowerShell payload version is not supported."
    }

    $iv = New-Object byte[] 16
    [System.Buffer]::BlockCopy($payloadBytes, $version.Length, $iv, 0, $iv.Length)

    $signature = New-Object byte[] 32
    [System.Buffer]::BlockCopy($payloadBytes, $payloadBytes.Length - $signature.Length, $signature, 0, $signature.Length)

    $cipherTextLength = $payloadBytes.Length - $version.Length - $iv.Length - $signature.Length
    if ($cipherTextLength -le 0) {
        throw "PowerShell payload does not contain encrypted content."
    }

    $cipherText = New-Object byte[] $cipherTextLength
    [System.Buffer]::BlockCopy($payloadBytes, $version.Length + $iv.Length, $cipherText, 0, $cipherText.Length)

    $masterKey = [System.Convert]::FromBase64String($payloadSecret)
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($masterKey)

    try {
        $encryptionKey = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("Certify.PowerShell.Payload.Encryption"))
        $authenticationKey = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("Certify.PowerShell.Payload.Authentication"))
    }
    finally {
        $hmac.Dispose()
    }

    $authenticatedData = Join-ByteArrays @($version, $iv, $cipherText)
    $payloadHmac = [System.Security.Cryptography.HMACSHA256]::new($authenticationKey)

    try {
        $expectedSignature = $payloadHmac.ComputeHash($authenticatedData)
    }
    finally {
        $payloadHmac.Dispose()
    }

    if (-not (Test-ByteArrayEqual $signature $expectedSignature)) {
        throw "PowerShell payload authentication failed."
    }

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = $encryptionKey
        $aes.IV = $iv
        $decryptor = $aes.CreateDecryptor()

        try {
            $plainText = $decryptor.TransformFinalBlock($cipherText, 0, $cipherText.Length)
        }
        finally {
            $decryptor.Dispose()
        }
    }
    finally {
        $aes.Dispose()
    }

    return [System.Text.Encoding]::UTF8.GetString($plainText)
}

if ($null -ne $payloadFile -and (Test-Path $payloadFile)) {
    try {
        $payloadJson = Get-PayloadJson $payloadFile
        $payload = $payloadJson | ConvertFrom-Json

        if ($null -ne $payload.result) {
            $wrappedArguments.Add("result", $payload.result)
        }

        if ($null -ne $payload.parameters) {
            foreach ($property in $payload.parameters.PSObject.Properties) {
                $wrappedArguments[$property.Name] = $property.Value
            }
        }
    }
    catch {
        throw "Failed to parse secure PowerShell payload file '$payloadFile'. $($_.Exception.Message)"
    }
}
elseif ($null -ne $payloadFile) {
    throw "Secure PowerShell payload file '$payloadFile' was not found."
}

if ($additionalParams.Count -gt 0) {
    # https://stackoverflow.com/questions/27764394/get-valuefromremainingarguments-as-an-hashtable
    $additionalParams | ForEach-Object {
        if ($_ -match '^-') {
            # add a new parameter with default value True, discarding the -prefix
            $lastvar = $_ -replace '^-'
            $wrappedArguments[$lastvar] = $true
        }
        elseif ($null -ne $lastvar) {
            # set a specific value for the last parameter added
            $wrappedArguments[$lastvar] = $_
        }
        else {
            # a bare value with no preceding -switch cannot be mapped to a named argument; fail with a clear
            # message so the problem surfaces as a task failure instead of a cryptic null array index error
            throw "Script wrapper received the value '$_' without a preceding -parameter name and cannot map it to a named argument. Verify the script path and any arguments are correctly quoted."
        }
    }
}

# restore the caller's error preference so the target script keeps its normal (non-terminating) error semantics
$ErrorActionPreference = $__certifyOriginalErrorActionPreference

# invoke wrapped script, with all optional arguments as a splatted hashtable
try {
    & $scriptFile @wrappedArguments
}
catch {
    if ($_.Exception.Message -notlike '*different language mode*') {
        throw
    }

    $scriptBlock = [scriptblock]::Create((Get-Content -Raw -Path $scriptFile))
    & $scriptBlock @wrappedArguments
}
