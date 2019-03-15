

## Changelog

### Latest Release

###### V4.1.5: Released 2019/02/23

*   New NameCheap DNS provider (courtesy of [@impworks](https://github.com/impworks))
*   Preserve UI window size/position between launches (courtesy of [@PromontoryProtean](https://github.com/PromontoryProtean) )
*   Allow custom PowerShell execution policy default via config
*   Fix IIS registry check exception experienced by some users

###### V4.1.4: Released 2019/01/29

*   Fix: Microsoft.Management.Infrastructure exception on Server 2008 R2\. Users on older versions of windows are advised to have the latest version of the [Windows Management Framework](https://docs.microsoft.com/en-us/powershell/wmf/overview) installed unless they have compatibility requirements that prevent that.

###### V4.1.3: Released 2019/01/27

*   Fix: avoid Microsoft.Management.Infrastructure exception on Server 2008 R2

###### V4.1.2: Released 2019/01/25

*   Fix: ensure legacy database schema upgrades complete OK

###### V4.1.1: Released 2019/01/25

*   <span class="text-danger">Important:</span> legacy tns-sni-01 challenges will now fallback to http-01
*   New: New certificate cleanup options under Settings, including daily full cleanup
*   New: support for acme-dns (CNAME redirection service: https://github.com/joohoi/acme-dns) for DNS challenges
*   New: Microsoft DNS API Provider (contributed by [AJ Henderson](https://github.com/AJH16))
*   New: Test results now support copy on click for copy/paste usage
*   Fix for account key encoding in non-english locales
*   Renew All/Auto Renew is now synchronous to reduce issues with larger installations committing many IIS bindings
*   Enhanced error reporting UI for service startup
*   Scheduled Task option removed from default UI, background service has performed all renewals since 3.x
*   Various fixes, updates and UI tweaks

###### V4.0.12: Released 2018/12/04

*   <span class="text-danger">Important:</span> Changed behaviour of Static IP and unassigned hostname binding deployment
*   Fix replacing of previous certificate based on thumbprint matching
*   Fix to ignore stale option selections if Auto deploy/auto-binding selected
*   Various fixes and updates

> **Notes regarding binding behaviour changes:**
> 
> Previously the app could try to enable SNI for a Static IP binding (based on the user's settings) this is no longer attempted.
> 
> Additionally if you had specified settings for the default IP of new bindings but switched back to Auto, the specific binding IP/port etc may still have been used for new bindings, this is no longer the behaviour.
> 
> If you have an existing http binding with a static IP this will be used if no hostname has been specified (all static IP SSL bindings carry a risk of binding conflicts, using SNI and specific hostnames is recommended).
> 
> If you require administrative control of https bindings you should select an option other than Auto under Deployment and Binding Add/Update should be set to Update only.

###### V4.0.11: Released 2018/11/28

*   <span class="text-danger">Important:</span> Fix for an issue with Account Key decoding which causes invalid challenge response validation for some users (affects all 4.x users)
*   Logging updates and additional fixes

###### V4.0.10: Released 2018/10/11

*   <span class="text-danger">Important:</span> Fix issue with binding not being updated to latest certificate (bug from v4.0.9)

###### V4.0.9: Released 2018/10/09

*   Fix wildcard domain binding matches
*   Improve Azure DNS API provider
*   Implement retries for IIS simultaneous binding updates

###### V4.0.8: Released 2018/08/15

*   Improve UI behaviour and avoid exceptions when IIS is not installed

###### V4.0.7: Released 2018/08/14

*   Fix account change after registering new contact

###### V4.0.6: Released 2018/08/13

*   Ensure current account key in use after version upgrades
*   Fix possible service exceptions/service stopping while querying cert bindings during cert cleanup.
*   Logging improvements and add additional logging for exceptions.

###### V4.0.5: Released 2018/07/31

*   Bug fix: re-use existing https port when non-standard port in use.
*   Improvements to background service startup.

###### V4.0.4: Released 2018/07/25

*   New UI changes to support a new wider range of features
*   New deployment modes and Preview feature to see what actions the app plans to perform.
*   Wildcard domain certificate support (*.example.com)
*   Let's Encrypt ACME V2 API compatibility
*   DNS Validation support for a range of DNS providers
*   Credentials manager to store and re-use DNS provider API credentials

###### V3.0.11: Released 2018/01/25

*   Fix for 'ghost' certificate bindings when using specific IP with SNI
*   Fix for installer not updating app files every time
*   tls-sni-01 no longer available as Let's Encrypt challenge type for new certs
*   Minor fixes & text updates

###### V3.0.10: Released 2018/01/06

*   Faster UI changing between managed sites
*   Invalid domains now filtered from new cert bindings
*   Minor fixes, logging updates

###### V3.0.9: Released 2017/12/22

*   Add warning when adding fixed IP SNI bindings (All Unassigned is recommended alternative)
*   Add CertificateThumbprintHash to Powershell output
*   Minor fixes

###### V3.0.7 & 3.0.8: Released 2017/12/16

*   Fix config check logic to allow for proxy API outages
*   V3.0.8: Add optional auto download and checksum/signature verification of updates

###### V3.0.6: Released 2017/12/15

*   Add refresh option for domains in managed site settings (when new bindings added)
*   Experimental [bulk import](https://github.com/webprofusion/certify/blob/master/docs/Bulk%20Import%20of%20Managed%20Sites.md) from CSV

###### V3.0.3 - 3.0.5: Released 2017/12/14

*   Fix feedback submission (previous feedback/crash reports for 3.x will not have been received)
*   v3.0.4: Remove debug exception on create new certificate
*   v3.0.5: Use long timeout for long-running operations
*   Known issues: for some users the installer is not always replacing the current version. After install check the version under About to ensure it has updated.

###### V3.0.2 : Released 2017/12/12

Major update including:

*   New dashboard reporting integration for multi server monitoring and failure notifications
*   Auto Renewal process can now run as a background service
*   Managed sites now scales to many thousands of sites
*   IPV6 binding support
*   IDN Domain Binding Fix
*   Translation updates (new Norwegian translation by Steffen Fridtjofsen)
*   New renewal status information in UI
*   UI updates and fixes
*   Context menu to sort managed sites by name or expiry date

###### V2.1.28 : Released 2017/12/03

*   Workaround for private key issue/'Unspecified logon error'

###### V3.0.0 (beta 1) : Released 2017/11/27

This is a test version and should be used with caution. You should backup your c:\programdata\certify\ folder before proceeding. That said, here are some great new features:

*   New dashboard reporting integration for multi server monitoring and failure notifications
*   Renewal process now runs as a background service
*   Managed sites now scales to many thousands of sites
*   IPV6 binding support
*   Translation updates (new Norwegian translation by Steffen Fridtjofsen)
*   New renewal status information in UI
*   UI updates and fixes
*   Context menu to sort managed sites by name or expiry date (v3.0.1)

###### V2.1.27 : Released 2017/11/17

*   Fix license validation check

###### V2.1.26 : Released 2017/11/17

*   <span class="text-danger">Important</span> Fix issue where cert bindings are not updated if you have specific binding settings (IP, port etc)

###### V2.1.25 : Released 2017/11/06

*   New translation: Spanish (es-ES) contributed by Alejandro Mir
*   UI fixes (grid view scrolling)
*   Fix Update check

###### V2.1.24 : Released 2017/11/04

*   Fix exception trying to save backup of settings if no previous settings backup exists

###### V2.1.23 : Released 2017/11/03

*   Support for load/save of large managed sites configuration (thousands of sites) in low memory conditions
*   Fix for updating registered contact (Let's Encrypt account)
*   CLI updates (list managed sites, vault cleanup)
*   <span class="text-danger">Important</span> fix for auto renewal (process wait/hang)
*   Minor bug fixes

###### V2.1.21 : Released 2017/10/26

*   Minor bug fixes
*   Translation Updates (Simplified Chinese zh-Hans)

###### V2.1.20 : Released 2017/10/23

*   Fix issue identifying if IIS site is running/not running
*   Logging bug fixes
*   New: UI translation for Chinese (Simplified - zh-Hans) contributed by [iccfish](https://github.com/iccfish)

###### V2.1.18 : Released 2017/10/18

*   Only perform DNS checks in test/preview mode
*   Bug fixes

###### V2.1.17 : Released 2017/10/17

*   Fix for settings not being preserved between app version updates. Please review your current app settings after this update.

###### V2.1.16 : Released 2017/10/17

*   New setting to optionally disable DNS checks

###### V2.1.15 : Released 2017/10/17

*   New advanced options ( contributed by [Marcus Lum](https://github.com/Marcus-L))
    *   Post-Request Web Hooks (report success/fail to your own API)
    *   Revoke Certificate option
    *   New tls-sni-01 challenge support
    *   DNS CAA & DNSSEC checks
*   Updated pre-request config checks
*   UI Updates
*   Bug fixes

###### V2.0.13 : Released 2017/10/02

*   Fix bug finding www root folder on new managed sites

###### V2.0.12 : Released 2017/09/30

*   Advanced Users: PowerShell Pre/Post [request hooks](https://github.com/webprofusion/certify/blob/master/docs/Request%20Script%20Hooks.md) for scripting per managed site (contributed by [Marcus Lum](https://github.com/Marcus-L))
*   Fix app crash if user attempts to open log for site with no requests yet
*   Site wwwroot path is now configurable independent of site
*   New option to configure max renewal/requests per session (useful for helping avoid rate limits)
*   Bug fixes

###### V2.0.11 : Released 2017/09/09

*   Fix license validation check

###### V2.0.10 : Released 2017/09/09

*   Add warning if IIS installed instead of crashing on app startup

###### V2.0.9 : Released 2017/09/08

*   UI Updates and improvements
*   Bug fixes, <span class="text-danger">including important fix for cert renewal on SAN certificates</span>. Some users were seeing an issue with renewed certificates not containing all the required domains due to previous validation.

###### V2.0.8-beta : Released 2017/09/06

*   Preview release

###### V2.0.7-beta4 : Released 2017/05/22

*   Bug fixes (import and settings UI)

###### V2.0.6-beta3 : Released 2017/05/22

*   New Feedback Submission UI

###### V2.0.5-beta2 : Released 2017/05/21

*   Make use of EFS for sensitive files optional

###### V2.0.4-beta1 : Released 2017/05/19

*   First 2.0 Beta
*   Bug fixes and UI updates, TLS1.2-only comms now supported
*   New registration options

###### V2.0.3-alpha : Released 2017/05/09

*   Alpha preview of V2.0 released for initial feedback
*   New Managed Sites feature for granular control of requests and renewals
*   New Auto Renew and Renew All features
*   Multi domain/subdomain certificate support using SAN certificates
*   New UI
*   No longer requires PowerShell

### Older Releases

###### V0.9.98

*   Disable identifier re-use. Caused issues for renewals.

###### V0.9.97

*   Enable ACME identifier re-use if identifers not expired and still pending/valid, to avoid rate limits when making repeated requests for same domain. <span class="text-danger">You should upgrade from this version immediately. Renewed certificates will not work due to decryption key issues.</span>

###### V0.9.96

*   Fix powershell version detection sequence to avoid crash initialising vault. You need to be running Powershell 4.0 or higher.

###### V0.9.95

*   Fix issue where generate domain identifier aliases were too long, causing cert requests to fail.

###### V0.9.94

*   Remove default filter on IIS site state (some users not seeing there IIS sites)

###### V0.9.93

*   Update to automated extensionless URL config checks for IIS (including Server 2012)

###### V 0.9.92

*   Removed the dependency on the ACMESharp PowerShell module from Powershell Gallery and bundled our own build
*   Minor fixes and UI Updates: Tree view now expands your domain list by default


