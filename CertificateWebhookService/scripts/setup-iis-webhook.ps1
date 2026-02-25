param(
    [string]$SiteName = "BugenceCertWebhook",
    [string]$AppPoolName = "BugenceCertWebhookPool",
    [string]$PublishPath = "C:\services\bugence-cert-webhook",
    [string]$HostHeader = "cert-webhook.bugence.com",
    [int]$Port = 443,
    [string]$CertificateThumbprint = ""
)

$ErrorActionPreference = "Stop"
Import-Module WebAdministration

if (-not (Test-Path $PublishPath)) {
    throw "PublishPath not found: $PublishPath"
}

if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-Item "IIS:\AppPools\$AppPoolName" | Out-Null
}

Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value "ApplicationPoolIdentity"

if (-not (Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $PublishPath -ApplicationPool $AppPoolName -Port 80 -HostHeader $HostHeader | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PublishPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

$httpBinding = Get-WebBinding -Name $SiteName -Protocol "http" -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq "*:80:$HostHeader" }
if (-not $httpBinding) {
    New-WebBinding -Name $SiteName -Protocol "http" -Port 80 -HostHeader $HostHeader | Out-Null
}

if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol "https" -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq "*:$Port:$HostHeader" }
    if (-not $httpsBinding) {
        New-WebBinding -Name $SiteName -Protocol "https" -Port $Port -HostHeader $HostHeader | Out-Null
    }

    $bindingPath = "IIS:\SslBindings\0.0.0.0!$Port!$HostHeader"
    if (Test-Path $bindingPath) {
        Remove-Item $bindingPath -Force
    }

    New-Item $bindingPath -Thumbprint $CertificateThumbprint -SSLFlags 1 | Out-Null
}

Write-Host "Webhook IIS setup complete."
Write-Host "Site: $SiteName"
Write-Host "Host: $HostHeader"
Write-Host "Path: $PublishPath"
