Current Project Roadmap (Late 2018- Mid 2019)

# v4.x:

## Deployment:
* Better support for blank hostname handling and static IP bindings
* Configurable cert cleanup strategy (default and per managed cert. immediate or when expired)
* Default password on PFX etc, optional custom per managed cert. Needs data protection.
* Child managed cert deployment type (e.g. request a wildcard from one managed cert then deploy in any number of ways using child managed certs)
* Cert cleanup: Need way to tell if cert in use for other things (ftp etc) and either update them or inform user. Note: Manually added https bindings lose cert if deleted/modified through another association 
* Need support for wildcard bindings (*.example.com) - only when cert has matching wildcard?
* If using 'Auto' deployment and blank hostnames present on target site, need to prompt user to create hostname bindings or use advanced deployment.
* Multiple export formats and custom file/path output
* Support for Central Certificate Store (location, cert export and naming). How to achieve centralised challenge response?

## Preview
* Updates as required

## Test results UI:
* Updates as required

## Domain options
* Needs performance improvements for many bindings
* Possibly sort checked items to the top, or scroll to first and sort in reverse domain order

## DNS Validation
* DNS providers not working that reliably, need configurable timeouts
* Implement ACME DNS
* Provider custom CNAME redirection service
* Additional DNS API support

## Auto config/config check:
* Updates as required

## Logging
* Updates as required

## Upgrading
* Prompt user to remove the old scheduled task

## Accessibility
* Check and fix current accessibility for core functions

## Installer
* Updates as required

## Stored Credentials
* Make credential encryption scope optional (CurrentUser vs LocalMachine), provide migration between options.
* Provide export/import option

## Improve config checks
* Possibly use letsdebug.net API for extended diagnostics

## Authorization
* ~~multi auth (auth type, auth credentials etc) per managed cert to support single certs with multiple domains from different zones/DNS providers, or mixing http auth with DNS.~~

## Docs/Portal/API
* Website looks awful, clean up
* Documentation updates
* Separate dashboard app from main website
* Move API to AWS/Azure

## Misc
* Localization text updates
* Check if primary domain selection toggle bug is fixed
* EFS not implemented yet
* Specific date/time renewal: target a specific time and day of week, month etc for a particular managed certificate renewal. Option to wait until next date before re-attempting if renewal fails.
* Allow http validation to wait a delay before completing validation (web farm volume sync)
* Option to cancel config check mid tests
* Capture config check verbose logs so full details can be viewed
* Failed validations can be re-tried later? would need to store validation status info
* Report on IIS sites/bindings not currently managed
* check localhost bindings to 127.0.0.1 and not an IP e.g. netsh http show iplisten, netsh http delete, iplisten ipaddress=195.43.64.112 
* Web farm: output to shared certificate store, or have secondary servers configured to pickup from a shared location
* Credential export option (backup credentials). Should email the primary contact when it occurs. Should we email the primary contact when their details are changed in the app? Could be a pref controlled in the dashboard.
* Full config backup/restore - needs to be protected if it contains credentials. Option for cloud sync/backup (files can be v. large).
* Windows Admin Center extension?
* Proxy support
