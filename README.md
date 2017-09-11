# Certify SSL Manager

Home page for downloads and info : [https://certifytheweb.com/](https://certifytheweb.com/)

The SSL Certificate Management GUI for Windows, powered by [Let's Encrypt](https://letsencrypt.org/), allowing you to generate and install free SSL certificates (with a 90 day expiry).

Features:
- Easy certificate requests & automated SSL bindings
- Auto renewal, with configurable renewal frequency
- SAN support (multi-domain certificates)
- Pre/Post request powershell ![scripting hooks](https://github.com/webprofusion/certify/blob/master/docs/Request%20Script%20Hooks.md) for advanced users

![App Screenshot](https://certifytheweb.com/images/screen3.png)


----------


If you have a bug or feature and you can fix the problem yourself please just:

   1. File a new issue
   2. Fork the repository
   2. Make your changes 
   3. Submit a pull request, detailing the problem being solved and testing steps/evidence
   
If you cannot provide a fix for the problem yourself, please file an issue and describe the fault with steps to reproduce.

----------

> **Build/Run Requirements:**
> 
> - Visual Studio 2017 Community Edition (or higher) 
> - A local instance of IIS installed.
> - To build the current release version use the release branch: https://github.com/webprofusion/certify/tree/release, master is the current work in progress.
> - To build, first build the submodule for ACMESharp under /src/lib/ACMESharp - this will restore the required nuget packages.
> - The app needs to run as Administrator, otherwise it cannot access IIS, write to the IIS website root paths or manage the windows certificate store.

> **Note:**  For testing you will require a publicly accessible IP mapped to the domain/subdomain you want to test with. The Let's Encrypt service will need to be able to access your test site remotely via HTTP in order to complete authorisation challenges.

apps {at} webprofusion.com

