# Certificate Webhook Service (Bugence-Compatible)

This service provides the SSL webhook endpoint expected by Bugence:

- `POST /api/certificates/issue`
- Auth header: `X-API-Key`
- Response payload:
  - `pfxBase64`
  - `pfxPassword`
  - `thumbprint`

## Important

- Configure this **once per server/environment**.
- After that, all projects/domains can reuse it.
- Bugence will call this service when verifying custom domains.

## Quick Start

1. Open terminal in this folder:
   - `cd CertificateWebhookService`

2. Set a real API key in `appsettings.json`:
   - `Webhook:ApiKey`

3. Run service:
   - `dotnet run`

4. Health check:
   - `GET http://localhost:5000/health` (port may vary)

## Production Setup (IIS)

1. Publish:
   - `dotnet publish -c Release -o .\publish`
2. Copy publish output to server folder, for example:
   - `C:\services\bugence-cert-webhook`
3. Run IIS setup script (Administrator PowerShell):
   - `.\scripts\setup-iis-webhook.ps1 -PublishPath "C:\services\bugence-cert-webhook" -HostHeader "cert-webhook.yourdomain.com" -Port 443 -CertificateThumbprint "<thumbprint>"`
4. Restrict inbound access to Bugence server if possible.

## How certificate issuing works

The service supports 2 modes:

1. **Existing cert mode** (default)
   - Finds cert in configured Windows store (`LocalMachine\My` by default).
   - Exports it to PFX in memory and returns base64.

2. **Command mode** (recommended for auto issue)
   - Set `Webhook:IssueCommand` in `appsettings.json`.
   - Service executes command before searching/exporting cert.
   - Command must output JSON like:
     ```json
     { "thumbprint": "ABC123..." }
     ```
   - Optional output variants also supported:
     - `pfxPath` + `pfxPassword`
     - `pfxBase64` + `pfxPassword`

Example command:

```json
"IssueCommand": "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -ExecutionPolicy Bypass -File C:\\services\\cert-webhook\\scripts\\issue-cert-winacme.ps1 -Domain {domain} -Apex {apex}"
```

> A sample script is included at `scripts/issue-cert-winacme.ps1`.

## Connect to Bugence (System Properties)

Create a record in Bugence:

- Category: `CertificateWebhook`
- Host: `https://your-webhook-host`
- Port: `443` (optional)
- Route URL: `/api/certificates/issue`
- Password: your webhook API key
- Username: optional

Then in Domains page:
1. Publish selected project.
2. Refresh domain verification.

## One-time or per project?

- This setup is **one-time per environment/server**.
- Users do **not** need to configure webhook credentials for each project.
- After setup, all projects/domains use the same webhook service.

## Security Checklist

1. Use HTTPS only.
2. Use strong API key (32+ chars).
3. Restrict source IPs.
4. Rotate API key periodically.
5. Do not store secrets in git.

## Generate strong API key (PowerShell)

```powershell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
```
