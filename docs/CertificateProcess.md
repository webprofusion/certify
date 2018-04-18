Certificate Request Process
------------------

When requesting or renewing a domain certificate via LetsEncrypt the following process takes place:

General:
- Register a Contact (if not done already). This gives LetsEncrypt a person to contact if the certificate is approaching expiry and has not been renewed. Certify/ACMESharp only uses the first registered contact, so if domains are managed by multiple people this should ideally be a forwarded alias or shared mailbox.

New/Renewed Domain Certificate
- For a given domain (and associated 'subject alternative names' or additional domain/subdomain names to be represented by the same certificate) a new Identifier is created and registered with LetsEncrypt (associated with the Contact from the original Contact registration step) - this/these registered identifier then becomes key to the domain to be certified with LetsEncrypt.
- Once an Identifier has been registered with LetsEncrypt the user must prove their ownership/administration by answering a 'challenge'. This is commonly done using either a text file with known content (specified by LetsEncrypt in their challenge) or by registering a specific DNS record on the domain.
- For IIS based challenges, via http, LetsEncrypt provides a file it wants to appear at a specific URL, with specific content. We create that file and configure IIS to serve it (the file is extensionless which can provde problematic for IIS config).
- For DNS based challenges (any web server), LetsEncrypt asks for a specific dns record to be created. Once the user has created this they are ready to complete the challenge.
- When we are ready to provide our answer to the LetsEncrypt challenge, we tell LetsEncrypt and they check our answer is in place (either by requesting the file from the pre-arrange URL or checking DNS)
- If we have provided our answer LetsEncrypt will mark our Identifier as 'valid' or 'invalid' (it would previously be 'pending'). If our Identifier is now 'valid' we are ready to ask for a certificate.
- We request a new Certificate for our Identifier, LetsEncrypt will register this request and either issue the certificate immediately or soon after. We can update the Certificate info until LetsEncrypt provides an Issuers Serial No indicating a completed certificate issue.
- Once we have the updated certificate we can export our certificate in the form we need, such as .pfx for install to IIS.
- We then bind the certificate to our domain in our webserver.
- After 90 days, our certificate will expire, so renewal should be completed before then.