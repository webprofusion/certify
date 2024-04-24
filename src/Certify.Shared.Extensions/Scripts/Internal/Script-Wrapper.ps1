# Wrapper script to call user scripts with result paramater marshalled from JSON
param(
    $scriptFile, 
    $resultJsonFile,  
    [Parameter(ValueFromRemainingArguments)]
    $additionalParams
)

$wrappedArguments = @{}

$result = $null

# load results object from json, if supplied
if ($null -ne $resultJsonFile)
{
    $result = Get-Content -Raw -Path $resultJsonFile | ConvertFrom-Json

    $wrappedArguments.Add("result",$result)
}

if ($additionalParams.Count -gt 0)
{
    # https://stackoverflow.com/questions/27764394/get-valuefromremainingarguments-as-an-hashtable
    $additionalParams | ForEach-Object {
        if($_ -match '^-') {
            # add a new parameter with default value True, discarding the -prefix
            $lastvar = $_ -replace '^-'
            $wrappedArguments[$lastvar] = $true
        } else {
            # set a specific value for the last parameter added
            $wrappedArguments[$lastvar] = $_
        }
    }
}

# invoke wrapped script, with all optional arguments as a splatted hashtable
& $scriptFile @wrappedArguments
