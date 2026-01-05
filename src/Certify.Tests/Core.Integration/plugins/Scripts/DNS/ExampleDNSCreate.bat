REM This script would be called with the parameters <target domain> <record name> <record value> <zone id (optionally)>

REM The script would be expected to create the correct record name (so for _acme-challenge.www.domain.com it would need to create a "_acme-challenge.www" TXT record in the DNS zone

REM This example just logs the input to a text file.

ECHO "Created TXT Record in the DNS Zone for domain: %1 with Record Name: %2 and Record Value: %3" > dns_create_test.log