param(
    [Parameter(Mandatory = $true)]
    [string]$Domain,
    [string]$Apex,
    [string]$WebRoot = "C:\PublishedNew\wwwroot",
    [string]$WacsPath = "C:\Tools\win-acme\wacs.exe",
    [string]$PfxOutputRoot = "C:\Tools\win-acme\out",
    [string]$Email = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Domain)) {
    throw "Domain is required."
}

if (-not (Test-Path $WacsPath)) {
    throw "win-acme executable not found at $WacsPath"
}

if (-not (Test-Path $WebRoot)) {
    throw "Web root not found at $WebRoot"
}

$wellKnown = Join-Path $WebRoot ".well-known\acme-challenge"
if (-not (Test-Path $wellKnown)) {
    New-Item -Path $wellKnown -ItemType Directory -Force | Out-Null
}

# Allow IIS to serve extensionless ACME token files under /.well-known/acme-challenge.
$challengeWebConfig = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <staticContent>
      <remove fileExtension="." />
      <mimeMap fileExtension="." mimeType="text/plain" />
    </staticContent>
  </system.webServer>
</configuration>
'@
Set-Content -Path (Join-Path $wellKnown "web.config") -Value $challengeWebConfig -Encoding ASCII

New-Item -Path $PfxOutputRoot -ItemType Directory -Force | Out-Null

$safeDomain = ($Domain.ToLowerInvariant() -replace "[^a-z0-9.-]", "-")
$pfxPassword = [Convert]::ToBase64String([Guid]::NewGuid().ToByteArray()).TrimEnd('=').Replace('+', 'A').Replace('/', 'B')
$friendly = "bugence-$safeDomain"
$pfxFileName = "$safeDomain"

$args = @(
    "--source", "manual",
    "--host", $Domain,
    "--friendlyname", $friendly,
    "--validation", "filesystem",
    "--validationmode", "http-01",
    "--webroot", $WebRoot,
    "--store", "pfxfile",
    "--pfxfilepath", $PfxOutputRoot,
    "--pfxfilename", $pfxFileName,
    "--pfxpassword", $pfxPassword,
    "--installation", "none",
    "--accepttos",
    "--notaskscheduler",
    "--closeonfinish"
)

if (-not [string]::IsNullOrWhiteSpace($Email)) {
    $args += @("--emailaddress", $Email)
}

$stdout = Join-Path $env:TEMP ("wacs-" + [Guid]::NewGuid().ToString("N") + ".out.log")
$stderr = Join-Path $env:TEMP ("wacs-" + [Guid]::NewGuid().ToString("N") + ".err.log")

try {
    $proc = Start-Process -FilePath $WacsPath -ArgumentList $args -NoNewWindow -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $stdOutText = if (Test-Path $stdout) { Get-Content -Path $stdout -Raw } else { "" }
    $stdErrText = if (Test-Path $stderr) { Get-Content -Path $stderr -Raw } else { "" }

    if ($proc.ExitCode -ne 0) {
        $msg = if (-not [string]::IsNullOrWhiteSpace($stdErrText)) { $stdErrText } else { $stdOutText }
        throw "win-acme failed with exit code $($proc.ExitCode). $msg"
    }

    $pfx = Get-ChildItem -Path $PfxOutputRoot -Filter "$pfxFileName*.pfx" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $pfx) {
        throw "win-acme finished but no PFX file was produced under $PfxOutputRoot"
    }

    $result = @{
        pfxPath = $pfx.FullName
        pfxPassword = $pfxPassword
    }

    $result | ConvertTo-Json -Compress
}
finally {
    if (Test-Path $stdout) { Remove-Item $stdout -Force -ErrorAction SilentlyContinue }
    if (Test-Path $stderr) { Remove-Item $stderr -Force -ErrorAction SilentlyContinue }
}
