# Certify

Home page for downloads and info : http://certify.webprofusion.com/

Certify is an SSL Certificate Manager GUI for Windows, powered by Let's Encrypt [https://letsencrypt.org/], allowing you to generate and install free SSL certificates (with a 90 day expiry). Certify is a wrapper for the ACMESharp powershell scripts [https://github.com/ebekker/ACMESharp].

My time to spend on developing Certify is extremely limited. If you have a bug or feature and you can fix the problem yourself please just:
   1. File a new issue
   2. Fork the repository
   2. Make your changes 
   3. Submit a pull request, detailing the problem being solved and testing steps/evidence
   
If you cannot provide a fix for the problem yourself please file an issue and describe the fault in great detail with exact steps to reproduce. General issues which cannot be easily reproduced are likely to be ignored, sorry!

Build/run requirements:
  - Visual Studio 2015 Community Edition (or higher)
  - A clone of ACMESharp [https://github.com/ebekker/ACMESharp] - you will need to update the referenced project paths in order to build.
  - IIS installed locally. For testing you will require a publicaly accessible IP mapped to the domain/subdomain you want to test with. The Let's Encrypt service will need to be able to access your test site remotely via http in order to complete authorisation challenges.
