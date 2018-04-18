Import-Module ACMESharp
 
Get-ACMEIdentifier |  ForEach-Object {
 

    If ($_.Status -eq "pending") {
        Write-Host "s: " $_

        #get latest status of this identifier

        Try
        {
             $result = Update-ACMEIdentifier -IdentifierRef $_.Alias
     
             Submit-ACMEChallenge -IdentifierRef $_.Alias -ChallengeType http-01
        }
        Catch
        {
         Write-Host "Authorization no longer required " $_.Dns
        }
      
   }
}
