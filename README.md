# Certify The Web - SSL Manager

Home page for downloads and info : [https://certifytheweb.com/](https://certifytheweb.com/)

The SSL/TLS Certificate Management GUI for Windows, powered by [Let's Encrypt](https://letsencrypt.org/), allowing you to generate and install free SSL certificates for IIS (with a 90 day expiry and automated renewal).

Features:
- Easy certificate requests & automated SSL bindings
- Auto renewal, with configurable renewal frequency
- SAN support (multi-domain certificates)
- Pre/Post request powershell and web hook support ![scripting hooks](https://github.com/webprofusion/certify/blob/master/docs/Request%20Script%20Hooks.md) for advanced users (feature contributed by [Marcus-L](https://github.com/Marcus-L))

![App Screenshot](https://certifytheweb.com/images/screen3.png)


----------
Quick Start
----------
1. Download from [https://certifytheweb.com/](https://certifytheweb.com/) and install it.
2. Click 'New Certificate', choose your IIS site (which must have 1 or more hostname bindings set). Save your settings and click 'Request Certificate'
3. All done! Click 'Configure Auto Renew' to setup the scheduled task for renewals.

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



Build/Run Requirements:
----------------------

> - Visual Studio 2017 Community Edition (or higher) 
> - A local instance of IIS installed.
> - To build the current release version use the release branch: https://github.com/webprofusion/certify/tree/release, master is the current work in progress.
> - fetch any submodules using:
```
git submodule sync
git submodule update --init --recursive --remote
```

> - To build, first build the submodule for ACMESharp under /src/lib/ACMESharp - this will restore the required nuget packages.
> - The app needs to run as Administrator, otherwise it cannot access IIS, write to the IIS website root paths or manage the windows certificate store.

> **Note:**  For testing you will require a publicly accessible IP mapped to the domain/subdomain you want to test with. The Let's Encrypt service will need to be able to access your test site remotely via HTTP in order to complete authorisation challenges.
> The app consists of a UI and background service. The background service must be running for the UI to operatre. The develop/debug you can configure Visual Studio to launch both the UI and Service - Right Click the Solution > Properties> Startup Project, Set Certify.UI and Certify.Service to 'Start', then debug as normal. 
apps {at} webprofusion.com

