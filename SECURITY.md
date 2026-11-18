# Security Policy

## Supported Versions

KeyGrid is currently pre-1.0. Security fixes are applied to the latest released version.

## Reporting a Vulnerability

Please report suspected vulnerabilities privately through GitHub Security Advisories:

https://github.com/FunUd/keygrid/security/advisories/new

Do not open a public issue for an unpatched vulnerability. Include the affected version,
reproduction steps, and impact if possible.

## Release Integrity

- Release archives are built by the GitHub Actions workflow from a version tag.
- The workflow publishes `SHA256SUMS.txt` next to the ZIP. Verify it before deployment.
- The release job supports Authenticode signing when the `release` environment
  secrets `WINDOWS_SIGNING_CERT_BASE64` and `WINDOWS_SIGNING_CERT_PASSWORD` are configured.
  Without those secrets it publishes an unsigned development build; the release notes
  mark it explicitly. Authenticode signs the extracted binaries, not the ZIP.
- Release archives include `LICENSE` and exclude PDB files.

Verification commands:

```powershell
$archive = Get-ChildItem .\KeyGrid-*-win-x64.zip | Select-Object -First 1
Get-FileHash $archive.FullName -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
Expand-Archive $archive.FullName -DestinationPath .\KeyGrid -Force
Get-AuthenticodeSignature .\KeyGrid\KeyGrid.exe
```

The checksum file must match the archive. A deployed binary must have an Authenticode
signature whose status is `Valid`; otherwise do not deploy it in a managed environment.

Do not run binaries from a user-writable directory for shared or managed use. Install
under an administrator-protected path such as `%ProgramFiles%\KeyGrid` and grant standard
users read and execute permission only.

## Operational Security

- KeyGrid does not require administrator privileges and must run as a standard user.
- It does not use a global keyboard hook, network access, telemetry, clipboard access,
  or screen capture.
- Logs are stored under `%LOCALAPPDATA%\KeyGrid\logs`, retained for 7 days, capped at
  5 MB per day, and default to INFO. DEBUG logs contain window class names, process IDs,
  window handles, click coordinates, and focus-transition details. Do not publish raw logs
  without reviewing them.
- For managed endpoints, follow the organization's application control policy. Do not
  disable security controls or add broad path exclusions to work around a false positive.

## Repository Settings (Maintainers)

- Restrict who can create release tags, and protect existing release tags.
- Configure the `release` environment with required reviewers and restrict who can
  approve deployments.
- Keep GitHub Actions pinned to commit SHAs. Dependabot updates are configured in
  `.github/dependabot.yml`.
- Never store the signing certificate or password in the repository. Configure them as
  environment secrets for the `release` environment.
