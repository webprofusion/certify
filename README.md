# Certify The Web - SSL Manager UI for Windows

- Home page for downloads and info : [https://certifytheweb.com/](https://certifytheweb.com/)
- Docs covering v4 onwards can be found at: [https://docs.certifytheweb.com](https://docs.certifytheweb.com)
- Community Discussions: [https://community.certifytheweb.com](https://community.certifytheweb.com)

The SSL/TLS Certificate Management GUI for Windows, powered by [Let's Encrypt](https://letsencrypt.org/). This app makes it easy to automatically request, install and continuously renew free SSL certificates for Windows/IIS. You can also use these certificates for any other services which requires a domain certificate.  

**Certify The Web is used by many thousands of organisations to manage millions of certificates each month** and is the perfect solution for administrators who want visibility of certificate management for their domains. Centralised dashboard status reporting is also available.

![Stars](
https://img.shields.io/github/stars/webprofusion/certify.svg)

![Certify App Screenshot](docs/images/app-screenshot.png)

Requirements:
- Windows Server 2008 R2 SP1 or higher (.Net 4.6.2 or higher), 64-bit

Features:
- Easy certificate requests & automated SSL bindings (IIS)
- Automatic renewal using a background service, with configurable renewal frequency.
- Preview mode to see which actions the app will perform (including which bindings will be added/updated)
- SAN support (multi-domain certificates)
- Support for v2 of the Let's Encrypt API including Wildcard Certificate support (*.example.com)
- Http or DNS challenge validation.
	- Built-in Http Challenge Server for easier configuration of challenge responses
	- DNS Validation via supported APIs (including Azure DNS, Alibaba Cloud, AWS Route53, Cloudflare, DnsMadeEasy, GoDaddy), OVH, SimpleDNSPlus
- Stored Credentials (API access keys etc. protected by the Windows Data Protection API)
- Optional Pre/Post request powershell scripting for advanced deployment (Exchange, RDS, multi-server etc)
- Web Hook support for custom reporting.

The Community edition is free, Professional and Enterprise license keys are available for commercial organisations or users who wish to help fund development.

----------
Quick Start (IIS users)
----------
1. Download from [https://certifytheweb.com/](https://certifytheweb.com/) and install it. Chocolatey users can alternatively `choco install certifytheweb`.
2. Click 'New Certificate', optionally choose your IIS site (binding hostnames will be auto detected, or just enter them manually). Save your settings and click 'Request Certificate'
3. All done! The certificate will renew automatically.

Users with more complex requirements can explore the different validation modes, deployment modes and other advanced options.

Development & Bug Reporting
-------------

If you have a bug or feature and you can fix the problem yourself please just:

   1. File a new issue
   2. Fork the repository
   2. Make your changes 
   3. Submit a pull request, detailing the problem being solved and testing steps/evidence
   
If you cannot provide a fix for the problem yourself, please file an issue and describe the fault with steps to reproduce.

Translation
------------

You can help translate the app by cloning the repo and installing ResXManager to easily update translation text:
https://marketplace.visualstudio.com/items?itemName=TomEnglert.ResXManager

Developer Build/Run Requirements:
----------------------

> - Visual Studio 2017 Community Edition (or higher) 
> - A local instance of IIS installed (for http validation, not required for DNS validation).
> - Restoring NuGet packages using "Update-Package -reinstall" can be useful where nuget restore fails.
> - The UI needs the background service to be running. You can configure Visual Studio to launch both the Certify.UI project and the Certify.Service project via Solution > Properties > Multiple Startup Projects

> **Note:**  For development you will require a publicly accessible IP mapped to the domain/subdomain you want to test with. The Let's Encrypt service will need to be able to access your test site remotely via HTTP in order to complete authorisation challenges.
> The app consists of a UI and background service. The background service must be running for the UI to operate. 

support {at} certifytheweb.com
