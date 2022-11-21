# Wrapper script to call user scripts with result paramater marshalled from JSON
param(
    $scriptFile, 
    $resultJsonFile,  
    [Parameter(ValueFromRemainingArguments)]
    $ExtraParams
)

$result = $null

if ($null -ne $resultJsonFile)
{
    $result = Get-Content -Raw -Path $resultJsonFile | ConvertFrom-Json
}


if ($null -ne $result)
{
    & $scriptFile $result $ExtraParams
} else {
    & $scriptFile $ExtraParams
}

