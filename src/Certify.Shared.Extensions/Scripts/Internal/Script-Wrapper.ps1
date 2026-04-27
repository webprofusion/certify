# Wrapper script to call user scripts with result and parameters marshalled from an encoded payload file
param(
    $scriptFile, 
    $payloadFile,
    [Parameter(ValueFromRemainingArguments)]
    $additionalParams
)

$wrappedArguments = @{}

$result = $null

if ($null -ne $payloadFile -and (Test-Path $payloadFile)) {
    try {
        $payloadJson = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String((Get-Content -Raw -Path $payloadFile)))
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
        else {
            # set a specific value for the last parameter added
            $wrappedArguments[$lastvar] = $_
        }
    }
}

# invoke wrapped script, with all optional arguments as a splatted hashtable
& $scriptFile @wrappedArguments
