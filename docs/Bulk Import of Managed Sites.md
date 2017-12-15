# Bulk Import of Managed Sites

You can perform a bulk import of managed sites (requires the registered version) from a CSV file using the following method:

## Create a new CSV file

Your file should be in the format IISSiteID, Name, Domain1;Domain2;Domain3

Such as:
```
0, Test, test.com;www.test.com
3, TestSite2(Test again), example.com;subdomain.example.com

````

You may find a powershell command such as the following useful as a starting place:
```PS
Get-WebBinding | % {
    $name = $_.ItemXPath -replace '(?:.*?)name=''([^'']*)(?:.*)', '$1'
    New-Object psobject -Property @{
        Name = $name
        Binding = $_.bindinginformation.Split(":")[-1]
    }
} | Group-Object -Property Name | 
Format-Table Name, @{n="Bindings";e={$_.Group.Binding -join "`n"}} -Wrap
```

## Perform CSV import
- Open a new Command Prompt (Run as Administrator).

```
cd C:\Program Files (x86)\Certify\

certify importcsv c:\temp\sites.csv
```
If you have the main Certify SSL Manager UI open you will see the sites being added as they are imported. Once added you can then modify any required settings.

Performing the same import twice will create duplicates so you should backup your c:\programdata\certify\manageditems.db first in case you need to restore it.