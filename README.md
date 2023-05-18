# Certify The Web - Certificate Manager UI for Windows

ACME Certificate Manager for Windows, powered by [Let's Encrypt](https://letsencrypt.org/) and other ACME certificate authorities. This app makes it easy to automatically request, install and continuously renew free certificates for Windows/IIS or for any other services which requires a certificate.  

- Home page for downloads, info and support : [https://certifytheweb.com/](https://certifytheweb.com/)
- Documentation: [https://docs.certifytheweb.com](https://docs.certifytheweb.com)
- Community Discussions: [https://community.certifytheweb.com](https://community.certifytheweb.com)
- Changelog (release notes): https://certifytheweb.com/home/changelog

**Certify The Web is used by hundreds of thousands of organisations to manage millions of certificates each month** and is the perfect solution for administrators who want visibility of certificate management for their domains. Centralised dashboard status reporting is also available.

![Stars](
https://img.shields.io/github/stars/webprofusion/certify.svg)

![Certify App Screenshot](docs/images/app-screenshot.png)

## Features include:
- See more details: https://certifytheweb.com/home/features
- Easy certificate requests & automated SSL bindings (IIS)
- Fetch certificates from ACME Certificate Authorities including **Let's Encrypt, BuyPass Go, ZeroSSL and Martini Security (STIR/SHAKEN)** or use private ACME CA servers including DigiCert, smallstep, Keyon true-Xtender etc.
- Preview mode to see which actions the app will perform (including which bindings will be added/updated)
- Automatic renewals and certificate maintenance using a background service, with configurable renewal frequency.
- Manage certificates for:
	- Single domains, multiple-domains (SAN) and wildcard certificates (*.example.com)
	- Support for STIR/SHAKEN certificates for secure telephone identity.
	- A single instance can be configured to manage thousands of certificates (licensed version).
- Http or DNS challenge validation.
	- Built-in Http Challenge Server for easier configuration of challenge responses
	- DNS Validation via over 30 supported APIs (including Azure DNS, Alibaba Cloud, AWS Route53, Cloudflare, DnsMadeEasy, GoDaddy, OVH, SimpleDNSPlus). Some providers are implemented via the [Posh-ACME project](https://github.com/rmbolger/Posh-ACME/tree/main/Posh-ACME)
	- Support for the *Certify DNS* cloud managed dns challenge validation service, allowing DNS validation via any DNS provider.
	- Multiple authorizations supported, allowing a mix of domain validation settings per managed certificate
- Stored Credentials (API access keys etc. protected by the Windows Data Protection API)
- Pre/post request Deployment Tasks and scripting for advanced deployment (**Exchange, RDS, multi-server, CCS, Apache, nginx, export, webhooks, Hashicorp Vault, Azure KeyVault etc**)

The Community edition is free and supports up to 5 managed certificates, the licensed version supports unlimited managed certificates. License keys are available for commercial organisations, users who wish to help fund development or users who require support.

## Requirements:
- Windows Server 2012 R2 or higher (.Net 4.6.2 or higher), 64-bit
- PowerShell 5.1 or higher (for functionality like Deployment Tasks and some DNS providers).

----------
Quick Start (IIS users)
----------
1. Download from [https://certifytheweb.com/](https://certifytheweb.com/) and install it. Chocolatey users can alternatively `choco install certifytheweb`.
2. Click 'New Certificate', optionally choose your IIS site (binding hostnames will be auto detected, or just enter them manually). Save your settings and click 'Request Certificate'
3. All done! The certificate will renew automatically.

Users with more complex requirements can explore the different validation modes, deployment modes and other advanced options.

https://docs.certifytheweb.com

## Build

Create a directory for the various repos to clone to, e.g. `C:\git\certify_dev` and clone the following repos into this location:
- https://github.com/webprofusion/certify.git
- https://github.com/webprofusion/certify-plugins.git

In addition, create a \libs subdirectory and clone:
- anvil:  https://github.com/webprofusion/anvil.git
- bc-sharp: git clone --branch 2.2-trimmed https://github.com/webprofusion/bc-csharp

Run `dotnet build Certify.Core.Service.sln` and `dotnet build Certify.UI.sln` or open using Visual Studio. The UI needs the service running to connect to for normal operation.

When developing plugins, the plugin and dependencies of the plugin need to be copied to the debug \plugins\ location for the service to load them.
