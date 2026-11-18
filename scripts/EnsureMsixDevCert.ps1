# Creates or refreshes the MSIX dev signing cert (CN=Helvety) and PFX used by helvety.screentools.
# Run once per machine before building an MSIX: .\scripts\EnsureMsixDevCert.ps1
# Follows https://learn.microsoft.com/windows/msix/package/create-certificate-package-signing
# The PFX is a local dev key (gitignored); its password only guards that local file.

$ErrorActionPreference = 'Stop'
$projectDir = Join-Path $PSScriptRoot '..\helvety.screentools' | Resolve-Path
$pfxPath = Join-Path $projectDir 'helvety.screentools_TemporaryKey.pfx'
$cerPath = Join-Path $projectDir 'helvety.screentools_TemporaryKey.cer'
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
    -NotAfter (Get-Date).AddYears(2) `
    -TextExtension @(
        '2.5.29.37={text}1.3.6.1.5.5.7.3.3', # Enhanced key usage: code signing
        '2.5.29.19={text}'                   # Basic constraints: end entity, not a CA
    )

$securePassword = ConvertTo-SecureString -String $password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $securePassword | Out-Null
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null

Write-Host "MSIX dev certificate ready."
Write-Host "  Subject:    CN=Helvety"
Write-Host "  Thumbprint: $($cert.Thumbprint)"
Write-Host "  PFX:        $pfxPath"
Write-Host "  CER:        $cerPath (public certificate only)"
Write-Host "  Password:   $password (also in helvety.screentools.csproj)"
Write-Host ""
Write-Host "To install MSIX packages signed with it, trust it for sideloading only (elevated PowerShell):"
Write-Host "  Import-Certificate -FilePath `"$cerPath`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host "Do not add this self-signed certificate to Trusted Root Certification Authorities."
