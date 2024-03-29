﻿# Get List of Managed Certificates available

`curl https://localhost:44331/api/v1/certificate`

```
[
 {
    "id": "ba05f0ef-f43b-46d3-9227-a4e8e1b864aa:7",
    "title": "www.dependencymanager.com",
    "domains": [
      "dependencymanager.com"
    ],
    "primaryDomain": "dependencymanager.com",
    "dateRenewed": "2020-06-03T09:48:18.1771936+08:00",
    "dateExpiry": "2020-12-01T06:59:00+08:00",
    "status": null
  },
  {
    "id": "1f5abdee-61e1-4516-9b92-70bd2d08567e:",
    "title": "acme-test.dependencymanager.com",
    "domains": [
      "acme-test.dependencymanager.com",
      "acme-test2.dependencymanager.com",
      "acme-test3.dependencymanager.com",
      "acme-test4.dependencymanager.com"
    ],
    "primaryDomain": "acme-test.dependencymanager.com",
    "dateRenewed": "2020-06-02T04:04:30.6779826Z",
    "dateExpiry": "2020-08-31T03:04:29Z",
    "status": null
  }
]
```


# Get certificate as PFX
`curl https://localhost:44331/api/v1/certificate/ba05f0ef-f43b-46d3-9227-a4e8e1b864aa:7/download/pfx`

# Get unlocked stored credential as a JSON key-value dictionary (if permitted for token role)
`curl https://localhost:44331/api/v1/credential/1eb2c3d4`

# Get http-01 challenges waiting to be answered

`curl https://localhost:44331/api/v1/validation/http-01`

# Get specific http-01 challenge waiting to be answered, by key

`curl https://localhost:44331/api/v1/validation/http-01/abcd1345`

Any http listener on the domain host can then respond with content of challenge at url: `http://<domain>/.well-known/acme-challenge/abcd1345`

e.g.  `https://certify.devops.projectbids.co.uk/api/v1/validation/http-01/abcd1345`


# Get system version (heartbeat)
`curl https://localhost:44331/api/v1/system/version`

# Check token
`curl https://localhost:44331/api/v1/auth/status`

# Get new token
`curl https://localhost:44331/api/v1/auth/token`

```
curl https://localhost:44331/api/v1/auth/status -H "Accept: application/json"  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJuYW1laWQiOiIxMjM0NSIsIm5iZiI6MTU5MTY5ODQwOSwiZXhwIjoxNTkxNzg0ODA5LCJpYXQiOjE1OTE2OTg0MDl9.As92l3EHGAMGkrhfXzCLSpUFRpEAyMTwmpMp16-XjhY"

```

Architecture
-----------------

## Internal Service 
Runs by default on `localhost:9696`. 

Expected clients: Desktop UI (WPF), Certify CLI, Certify Server API.

The internal service provides the internal certificate management system. The API it exposes is not intended for general use and can change entirely between system updates.

## Certify Server API 
This service provides a general API for use by custom clients. It must be configured to connect to internal service and may be optionally hosted on same instance as primary service.

Expected clients: any (including Certify Server App UI).

## API Security
Exposing either service on the actual public internet should be strictly avoided (there are no external security assurances). Only the Certify Server API is for general client consumption and it should remain internal to an organisation.

