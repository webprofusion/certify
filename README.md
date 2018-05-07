# Certify The Web - SSL Manager UI for Windows

- Home page for downloads and info : [https://certifytheweb.com/](https://certifytheweb.com/)
- Docs can be found at: [https://docs.certifytheweb.com](https://docs.certifytheweb.com)
- Community Discussions: [https://community.certifytheweb.com](https://community.certifytheweb.com)

The SSL/TLS Certificate Management GUI for Windows, powered by [Let's Encrypt](https://letsencrypt.org/), allowing you to generate and install free SSL certificates for IIS (with automated renewal).

Features:
- Easy certificate requests & automated SSL bindings
- Auto renewal, with configurable renewal frequency
- SAN support (multi-domain certificates)
- Pre/Post request powershell and web hook support for advanced users (feature contributed by [Marcus-L](https://github.com/Marcus-L))

From v4 onwards we also support:
- v2 of the Let's Encrypt API
- Wildcard Certificates (*.example.com)
- DNS Validation via supported APIs (currently including Azure DNS, AWS Route53, Cloudflare, DnsMadeEasy, GoDaddy)
- Stored Credentials (API access keys etc)
- Preview mode to see which actions the app will perform
![App Screenshot](https://certifytheweb.com/images/screen3.png)

----------
Quick Start
----------
1. Download from [https://certifytheweb.com/](https://certifytheweb.com/) and install it.
2. Click 'New Certificate', choose your IIS site (which must have 1 or more hostname bindings set). Save your settings and click 'Request Certificate'
3. All done!

Advanced users can explore the different validation modes, deployment modes and other advanced options.

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
> - To build the current release version use the release branch: https://github.com/webprofusion/certify/tree/release, development is the current work in progress.
> - Restoring NuGet packages using "Update-Package -reinstall" can be useful where nuget restore fails.
> - The app needs to run as Administrator, otherwise it cannot access IIS, write to the IIS website root paths or manage the windows certificate store.
> - The UI needs the background service to be running. You can configure Visual Studio to launch both the Certify.UI project and the Certify.Service project via Solution > Properties > Multiple Startup Projects

> **Note:**  For development you will require a publicly accessible IP mapped to the domain/subdomain you want to test with. The Let's Encrypt service will need to be able to access your test site remotely via HTTP in order to complete authorisation challenges.
> The app consists of a UI and background service. The background service must be running for the UI to operate. 

apps {at} webprofusion.com

