# Current Project Roadmap 

The below items are general topics which may or may not be developed. Checked items are implemented but may still be expanded upon.

# Planned (7.x timeline):
## Management Hub 
- [ ] Management Hub Release
- [x] Managed challenges : perform challenges via the hub on behalf of other clients
- [x] Managed ACME : proxy ACME orders to simplify ACME client configuration when challenges are managed by the hub
- [x] Multi-instance management UI : Centralised management of multiple Certify The Web instances
- [x] Instance and item tagging for easier categorization and filtering (e.g. devlopment vs production or organization/dept/project specific)
- [ ] Pull managed certs from hub (optionally from other managed instances) or vauls to agent for deployment 

# (6.x timeline):

- [x] Optional Client API for custom development against the Certify The Web server API (accessible in .net, dotnet core, PowerShell etc)
- [x] Optional Web Admin UI, using the new Client API. Include Users, Roles, Keys.
- [x] Official Linux support for core certificate request/renewal/deployment.
- [x] Cert API for authorized client apps to pull latest certs
- [x] CA fallback. If communication or renewal with the primary CA fails repeatedly, fallback to compatible alternatives (if available)
- [x] Import/Export to migrate configuration between installations

## Remote server support
- [x] Possible to offer support for managing certificates on non-windows servers.

## Reporting
- [ ] Variety of reports/scans to determine current status of sites/services to determine which are currently managed and which are not. Diagnostics scans.

## Domain options
- [ ] Needs performance improvements for many bindings scenario
- [ ] Possibly sort checked items to the top, or scroll to first and sort in reverse domain order

## Deployment 
- [ ] Fetch latest from vault (deploy to bindings)
- [ ] Fetch latest from other Certify instance (deploy to bindings)
- [ ] CCS/Web farm - Simplest/best way to achieve coordinated (or proxied) challenge responses?
- [ ] support for IIS wildcard bindings (*.example.com) - only when cert has matching wildcard?
- [ ] Better general support for non-IIS scenarios
- [ ] Need to warn user when their deployment will target no bindings

## Accessibility
- [ ] Check and fix current accessibility for core functions

## Stored Credentials
- [ ] Make credential encryption scope optional (CurrentUser vs LocalMachine), provide migration between options.
- [x] Provide export/import option

## Improve config checks
- [ ] Possibly use letsdebug.net API for extended diagnostics

## Docs/Portal/API
- [x] Website improvements
- [x] Documentation updates
- [x] Separate dashboard app from main website
- [ ] Move API to AWS/Azure functions

## Misc
- [ ] Prompt user to remove the old scheduled task
- [x] Introduce db schema version for easier migration detection
- [ ] Localization text updates
- [ ] Allow http validation to wait a delay before completing validation (web farm volume sync)
- [ ] Option to cancel config check mid tests
- [ ] Capture config check verbose logs so full details can be viewed
- [ ] Failed validations can be re-tried later? would need to store validation status info. Pending validation clearance.
- [x] check localhost bindings to 127.0.0.1 and not an IP e.g. netsh http show iplisten, netsh http delete, iplisten ipaddress=195.43.64.112 
- [x] Credential export option (backup credentials). Should email the primary contact when it occurs. Should we email the primary contact when their details are changed in the app? Could be a pref controlled in the dashboard.
- [x] Full config backup/restore - needs to be protected if it contains credentials. Option for cloud sync/backup (files can be v. large).
- [x] Proxy support
- [x] Hardened Mode: Required signed scripts, run service as dedicated service user, stricter permissions

# Investigate
- [x] Cross platform UI options for desktop app (mac os & linux support)
- [x] Centralised management options
- [ ] Storage in Hashicorp Vault, Azure etc and retrieval (one instance can renew and store a cert for other instances to consume)

# Implemented in 5.x

## Deployment:
- [x] Deployment Tasks: configurable, deferrable deployment for exports, ccs, ssh/sftp, apache, nginx, exchange etc
- [x] Deploy certs to vault (Hashicorp Vault etc)
- [x] Default password on PFX etc, optional custom per managed cert. Needs data protection.

## DNS Validation
- [x] Certify DNS - Provide custom CNAME redirection service (probably based on a hosted acme-dns solution)
- [x] Additional DNS API support (community provided)

## Upgrading
- [x] Add release notes to update UI

## Docs/Portal/API
- [x] Website improvements
- [x] Documentation updates

## Portability
- [x] .net service running on Linux and can be connected to using desktop UI
- [x] Port of UI to newer .net versions (.net 5 +)

