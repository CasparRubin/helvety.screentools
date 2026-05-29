# Creates or refreshes the MSIX dev signing cert (CN=Helvety) and PFX used by helvety.screentools.
# Run once per machine before building an MSIX: .\scripts\EnsureMsixDevCert.ps1

$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..\helvety.screentools' | Resolve-Path
$pfxPath = Join-Path $projectDir 'helvety.screentools_TemporaryKey.pfx'
$password = 'helvety-dev'

Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=Helvety' } | Remove-Item -Force

$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject 'CN=Helvety' `
    -KeyUsage DigitalSignature `
    -FriendlyName 'Helvety Screen Tools' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy Exportable `
    -KeySpec Signature `
    -KeyLength 2048 `
    -KeyAlgorithm RSA `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
        '2.5.29.19={text}1.3.6.1.4.1.311.10.3.13'
    )

$securePassword = ConvertTo-SecureString -String $password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword | Out-Null

Write-Host "MSIX dev certificate ready."
Write-Host "  Subject:    CN=Helvety"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  PFX:        $pfxPath"
Write-Host "  Password:   $password (also in helvety.screentools.csproj)"
