About This Config
-------------------------

When Let's Encrypt performs validation over http (known as an http-01 challenge) 
they ask for a randomly named text file to be created in the /.well-known/acme-challenge
 path of your website. So they should be able to retrieve it at 
http://<yourdomain>/.well-known/acme-challenge/<filename>

On IIS this presents a few challenges:
* The file does not have an extension (like .txt etc), so a static file handler usually needs to be configured to handle extension-less files
* Existing handlers for extension-less content may intercept the request and prevent access to the file
* If authentication (basic, forms etc) is enabled the access to the file will be restricted so this needs to be disabled
* Due to the above, ASP.net (and an app-pool) is generally required so that web.config can be supplied to override the configuration.
* Other customizations or app requirements for the parent website may affect configuration

For Certify The Web, we attempt to auto-configure the required configuration without modifying the configuration of the parent web application, 
this avoids app restarts for the parent application. We create a file called 'configcheck' in the /acme-challenge folder and
we cycle through a number of alternative web.config options and test each one. The test involves making a local http request to
http://<yourdomain>/.well-known/acme-challenge/configcheck

If the local request fails (perhaps because the local server can't resolve itself via DNS etc) and if proxy API support is enabled, the app asks
the https://api.certifytheweb.com server if it can access the resource.


