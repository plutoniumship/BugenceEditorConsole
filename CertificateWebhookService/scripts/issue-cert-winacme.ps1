param(
  [Parameter(Mandatory = $true)][string]$Domain,
  [Parameter(Mandatory = $false)][string]$Apex
)

$ErrorActionPreference = "Stop"

# Update these paths/settings for your server.
$wacsPath = "C:\tools\win-acme\wacs.exe"
$email = "admin@yourdomain.com"

if (!(Test-Path $wacsPath)) {
  throw "win-acme not found at $wacsPath"
}

# Example issuance command. Adjust validation mode as needed for your environment.
& $wacsPath `
  --target manual `
  --host $Domain `
  --accepttos `
  --emailaddress $email `
  --store certificatestore `
  --certificatestore My `
  --installation none `
  --notaskscheduler `
  --closeonfinish

if ($LASTEXITCODE -ne 0) {
  throw "win-acme failed with exit code $LASTEXITCODE"
}

$cert = Get-ChildItem Cert:\LocalMachine\My |
  Where-Object {
    $_.HasPrivateKey -and
    $_.NotAfter -gt (Get-Date).AddDays(7) -and
    (
      $_.Subject -match "CN=$Domain" -or
      ($_.DnsNameList -and ($_.DnsNameList.Unicode -contains $Domain))
    )
  } |
  Sort-Object NotBefore -Descending |
  Select-Object -First 1

if (-not $cert) {
  throw "No certificate found for $Domain in LocalMachine\\My after issuance."
}

@{
  thumbprint = $cert.Thumbprint
} | ConvertTo-Json -Compress
