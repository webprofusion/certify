# Certify The Web - Certificate Manager UI for Windows

- Home page for downloads, info and support : [https://certifytheweb.com/](https://certifytheweb.com/)
- Documentation can be found at: [https://docs.certifytheweb.com](https://docs.certifytheweb.com)
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
- Support for BuyPass Go SSL.
- Http or DNS challenge validation.
	- Built-in Http Challenge Server for easier configuration of challenge responses
	- DNS Validation via over 26 supported APIs (including Azure DNS, Alibaba Cloud, AWS Route53, Cloudflare, DnsMadeEasy, GoDaddy, OVH, SimpleDNSPlus)
- Stored Credentials (API access keys etc. protected by the Windows Data Protection API)
- Optional Pre/Post request deployment tasks and scripting for advanced deployment (Exchange, RDS, multi-server, CCS, Apache, nginx, export, webhooks, Azure KeyVault etc)

The Community edition is free, Professional and Enterprise license keys are available for commercial organisations, users who wish to help fund development or users who require support.

----------
Quick Start (IIS users)
----------
1. Download from [https://certifytheweb.com/](https://certifytheweb.com/) and install it. Chocolatey users can alternatively `choco install certifytheweb`.
2. Click 'New Certificate', optionally choose your IIS site (binding hostnames will be auto detected, or just enter them manually). Save your settings and click 'Request Certificate'
3. All done! The certificate will renew automatically.

Users with more complex requirements can explore the different validation modes, deployment modes and other advanced options.

https://docs.certifytheweb.com

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
