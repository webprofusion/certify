# Chocolatey package (certifytheweb)

Package spec for `choco install certifytheweb`, which installs **Certify Certificate
Manager** for Windows.

> These files are **generated**. They are rendered from templates in the
> `certify-internal` repo (`setup/chocolatey`) by `setup/Update-Chocolately.ps1`,
> which fills in the release version, installer download URL and sha256 from the
> published version info. Hand edits here are overwritten at the next release -
> change the templates instead.

## Contents

| File                              | Purpose                                                                        |
| --------------------------------- | ------------------------------------------------------------------------------ |
| `certifytheweb.nuspec`            | Package metadata and version                                                    |
| `tools/chocolateyinstall.ps1`     | Downloads and silently runs the signed Inno Setup installer                      |
| `tools/chocolateybeforemodify.ps1`| Closes the desktop UI and stops the background service before upgrade/uninstall |
| `tools/chocolateyuninstall.ps1`   | Runs the Inno Setup uninstaller                                                  |

## What gets installed

* `Certify Certificate Manager` desktop app - `%ProgramFiles%\CertifyTheWeb\UI.Desktop\Certify.UI.Desktop.exe`
* `Certify Management Agent` Windows service - `%ProgramFiles%\CertifyTheWeb\Service\Certify.Server.Core.exe`
* `Certify` CLI - `%ProgramFiles%\CertifyTheWeb\Certify.exe`

The installer is self-contained, so there is no .NET runtime prerequisite.

## Release process

Maintainers only - see `certify-internal/setup/deployment.md`:

1. Publish the installer and regenerate the version info.
2. From `certify-internal\setup`, run `pwsh .\Update-Chocolately.ps1`.
3. Commit the regenerated files here.
4. From `certify-internal\setup`, run `.\publish-tools\publish-choco.bat` to pack and push.
