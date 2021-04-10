Testing Configuration
-------------------------

- In Visual Studio (or other Test UI such as AxoCover), set execution environment to 64-bit to ensure tests load.
- Units tests are for discreet function testing or limited component dependency tests.
- Integration tests exercise multiple components and may interact with ACME services etc. Required elements include:
	- IIS Installed on local machine
    - The debug version of the app must be configured with a contact against staging Let's Encrypt servers
	- Completing HTTP challenges requires that the machine can respond to port 80 requests from the internet (such as the Let's Encrypt staging server checks)
	- DNS API Credentials test and DNS Challenges require the respective DNS credentials by configured as saved credentials in the UI (see config below)
	- Some tests will require at least one existing certificate in the (personal) computer certificate store
	
	Integration test config settings can be stored at C:\Temp\TestConfigSettings.json

	## Example Config
	```json
	{
        "HttpPort":"80",
        "TestCredentialsKey_Route53": "a04878d7-6c32-4f17-9f8e-c332669bd9fb",
        "TestCredentialsKey_Cloudflare": "c8760c11-b5c1-432415a-9add-defa534",
        "TestCredentialsKey_Azure": "5252-7255-4ff0-523524-737393",
        "AWS_ZoneId": "ABCD123456",
        "AWS_TestDomain": "mytestdomain.co.uk",
        "Azure_ZoneId": "mytestdomain.io",
        "Azure_TestDomain": "myothertest.io",
        "Cloudflare_ZoneId": "5265262gdd562s4x6xd64zxczxcv",
        "Cloudflare_TestDomain": "anothertest.com"
    }
```
In addition, the test domain for some tests can be set using the CERTIFYSSLDOMAIN environment variable.
		


