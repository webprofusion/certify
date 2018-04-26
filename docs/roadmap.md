Current Project Roadmap (Mid 2018-2019)

# v4 Beta:

## Deployment:

* Cleanup UI for different options and ensure deployment is happening as described (unit tests, preview)
* IIS bindings: should only show IP options for Single Site 
* Child managed cert deployment type
* Cert cleanup: Need way to tell if cert in use for other things (ftp etc) and either update them or inform user. Manually added https bindings lose cert if deleted through another association 
* Auto config tests
* If requesting a cert for test.com but target site is *:443, https binding needs to be Update only? Add would have a risk of binding conflict with other sites (if present).
* Need support for wildcard bindings (*.example.com) - only when cert has matching wildcard?

## Preview
* If binding exists (http & https 4 sets show up for 2 domains)
if any other deployment (cert only etc) chosen preview still says IIS
* Needs to cater for all selected options (deployment modes etc)

## Test results UI:
* Progressively updated UI to show status of the current config tests in progress
* Capture description, result OK and error for each test attempted (so config check can consist of multiple results, stream results via SignalR progress changed?)

## Domain options
* Ensure wildcard can't be entered alongside label for same domain (*.test.com and www.test.com)
* check how current UI responds to many IIS sites or many bindings

## DNS Validation
* DNS providers not working that reliably, need configurable timeouts
* if validation fails - need to check/log LE authorization URL info (so status response can be analysed)
* Need manual and scripted DNS validation options:
    * Need manual DNS option (email contact with details of DNS record required, detect when it changes and proceed with validation, notify them of completion?) if request is manual could show txt record details then proceed to validation, likewise http validation.

## Auto config/config check:
* http-01: Ensure new web.config strategy is working, work back in order of server 2016 hosting, most common issue is auto config fail or config check fail 

## Logging
* Overall app & service logging contexts (updates, account reg, managed cert creation/deletion)
* Log viewer tidy up

## Upgrading
* If upgrading, old scheduled task will not work (linked to 32-bit)
* Need to warn scripting users of change to 64-bit process

## Accessibility
* Check and fix current accessibility for core functions

## Installer
* Install needs to uninstall previous version, stopping service, uninstalling service first
* 32-bit to 64-bit migration

## Misc beta issues
* Internal API errors throw generic JSON serialization error, need proper exception handling (retry support?)
* Make credential encryption scope option (CurrentUser vs LocalMachine), provide migration between options.

# Release (post-beta) issues

## Misc
* Diagnostic check for disk space or repair database i.e System.Data.SQLite.SQLiteException (0x80004005): database or disk is full
* Remove unnecessary text mentions of IIS
* Localization text updates
* Check if primary domain selection toggle bug is fixed
* EFS not implemented yet
* Revoke not implemented yet
* Set http client user agent in external API calls
* Basic/advanced mode for bindings UI so that IP specific bindings etc is hidden by default, Single site auto bindings (existing https hostname bindings updated, new https bindings created if http binding already exists) could be basic mode.

## Improve config checks
* Ensure config check/validation wrote to disk before proceeding.
* Identify if connect to port 80 is failing (timeout?), as well as connecting but getting wrong content. i.e. config check failed: did not get the expected response, perhaps website is redirecting (get status code)

## Blank hostname bindings

* Warn user if no bindings will be created/updated and deployment is not storage only etc

## DNS Provider extensions
* Scripted DNS update provider
* Manual DNS update workflow
* libcloud python provider (extra download)
* list config from providerconfig.json or similar to allows custom providers (credential settings etc)

# Future:

## Authorization
* multi auth (auth type, auth credentials etc) per managed cert to support single certs with multiple domains from different zones/DNS providers, or mixing http auth with DNS.

## Deployment
* Option to export pfx/cert with fixed file name to shared cert store after renewal completed

## Portal/API
* Website looks awful, clean up
* use githb markdown as source for website content
* Add help/guide content to websites
* Separate dashboard from main website
* Move API to AWS/Azure

## Misc
* Allow http validation to wait a delay before completing validation (web farm volume sync)
* Method to cancel config check mid tests
* Capture config check logs so full details can be viewed
* Stream test results as they are performed
* Failed validations can be retried later? would need to store validation status info
* Report on IIS sites/bindings not currently managed
* check localhost bindings to 127.0.0.1 and not an IP e.g. netsh http show iplisten, netsh http delete, iplisten ipaddress=195.43.64.112 
* Web farm: output to shared certificate store, or have secondary servers configured to pickup from a shared location
* Credential export option (backup credentials). Should email the primary contact when it occurs. Should we email the primary contact when their details are changed in the app? Could be a pref controlled in the dashboard.
* Full config backup/restore - needs to be protected if it contains credentials
* Windows Admin Center extension?
