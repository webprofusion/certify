# Certify Management Hub API Notes

The Hub API is hosted by **Certify.Server.HubService**.

`Certify.Server.Hub.Api` contains the API controllers, middleware, SignalR hubs, and related contracts used by HubService. It is not intended to be run as an independently hosted service.

## Example Requests

### Get system version

`curl https://localhost:44361/api/v1/system/version`

### Get health status

`curl https://localhost:44361/api/v1/system/health`

### Get list of managed certificates

`curl https://localhost:44361/api/v1/certificate`

### Get certificate as PFX

`curl https://localhost:44361/api/v1/certificate/{instanceId}/download/{managedCertId}/pfx`

### Get unlocked stored credential as JSON (if permitted for token role)

`curl https://localhost:44361/api/v1/credential/{instanceId}/{storageKey}`

### Get waiting http-01 challenges

`curl https://localhost:44361/api/v1/validation/http-01`

### Check token

`curl https://localhost:44361/api/v1/auth/status`

### Get new token

`curl https://localhost:44361/api/v1/auth/token`

## Hosting Model

- **HubService** is the only runtime host for the public Hub API.
- **Hub API** is the API assembly/library loaded by HubService.
- **Server Core** remains integrated in-process within HubService for the primary managed instance.

## Expected Clients

- Blazor management UI
- WPF hybrid desktop host for the Blazor UI
- CLI or custom clients calling the HubService-hosted public API

## Security

The Hub API is intended for controlled internal or organizational use. It should not be exposed directly to the public internet without appropriate network controls and deployment hardening.

