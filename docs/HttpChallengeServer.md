
# Http Challenge Server
The app features an optional internal Http Challenge Server implemented as an http.sys aware http listener, sitting in front of IIS in the http.sys http request pipeline.

This service runs during http validation to answer http challenges only, all other request types are handled by IIS (or the port 80 webserver) as normal. After validation checks have completed the challenge server process will automatically stop.

In the event that a non-http.sys enabled web server is installed and using port 80, the http challenge server will not operate and conventional http validation will be served via the normal server on port 80 (IIS, Apache, nginx etc.).

## Configuration
To run the challenge server on an alternative port e.g. 81:

create/update `c:\programdata\certify\serviceconfig.json` (`serviceconfig.debug.json` if running in debug mode):
```json
{
 httpChallengeServerPort:81
}
```