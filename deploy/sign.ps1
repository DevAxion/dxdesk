<#
.SYNOPSIS
  DXDesk exe fayllarını Authenticode ilə imzalayır və sertifikatı GPO/quraşdırma üçün ixrac edir.

.DESCRIPTION
  Windows 11 (SmartScreen / Smart App Control / AppLocker) imzasız və ya etibarsız
  sertifikatla imzalanmış exe-ləri bloklayır ("Windows protected your PC",
  "nəşriyyatçı yoxlanıla bilmədi", sertifikat zənciri xətası).

  Bu skript:
    1) CurrentUser\My-də kod imzalama sertifikatını tapır (və ya -Thumbprint ilə verilir);
    2) deploy\admin və deploy\service-package-dəki bütün exe-ləri SHA256 + vaxt möhürü ilə imzalayır;
    3) sertifikatın açıq hissəsini deploy\service-package\DXDesk-signing.cer kimi ixrac edir.

  Self-signed / daxili (AD CS) sertifikat istifadə edirsinizsə, .cer faylı hər kompüterdə
  "Trusted Root Certification Authorities" və "Trusted Publishers"-də olmalıdır:
    - ən yaxşısı GPO ilə: Computer Configuration > Policies > Windows Settings >
      Security Settings > Public Key Policies (hər iki bölməyə import edin);
    - və ya install-service.bat .cer faylını yanında görəndə özü əlavə edir.
  Kommersiya (DigiCert, Sectigo və s.) və ya Azure Trusted Signing sertifikatı ilə
  heç nə yaymağa ehtiyac yoxdur.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File deploy\sign.ps1
  powershell -ExecutionPolicy Bypass -File deploy\sign.ps1 -Thumbprint 2F01231185F4F7186AE11776E61DBE6D1A2BBE16
  powershell -ExecutionPolicy Bypass -File deploy\sign.ps1 -NewSelfSigned   # yeni "CN=DXDesk Code Signing" yaradır
#>
param(
    [string]$Thumbprint,
    [switch]$NewSelfSigned,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# ---- 1) Sertifikat ----
if ($NewSelfSigned) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=DXDesk Code Signing' `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5)
    Write-Host "Yeni sertifikat yaradıldı: $($cert.Thumbprint)"
}
elseif ($Thumbprint) {
    $cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
}
else {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
        Where-Object { $_.Subject -match 'DXDesk|SRXDesk' -and $_.NotAfter -gt (Get-Date) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
}

if (-not $cert) {
    throw "Kod imzalama sertifikatı tapılmadı. -Thumbprint verin və ya -NewSelfSigned istifadə edin."
}
Write-Host "Sertifikat: $($cert.Subject)  ($($cert.Thumbprint)), bitir: $($cert.NotAfter)"

# ---- 2) İmzalama ----
$files = Get-ChildItem -Path (Join-Path $root 'admin'), (Join-Path $root 'service-package') -Filter *.exe -ErrorAction SilentlyContinue
if (-not $files) { throw "İmzalanacaq exe tapılmadı (əvvəlcə dotnet publish edin)." }

foreach ($f in $files) {
    $r = Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert `
        -HashAlgorithm SHA256 -TimestampServer $TimestampUrl
    if ($r.Status -ne 'Valid' -and $r.Status -ne 'UnknownError') {
        throw "$($f.Name): imzalama alınmadı - $($r.Status) $($r.StatusMessage)"
    }
    # Self-signed sertifikat bu kompüterdə etibarlı olmaya bilər (UnknownError) — imza yenə də yazılıb.
    Write-Host ("  imzalandı: {0,-22} {1}" -f $f.Name, $r.Status)
}

# ---- 3) .cer ixracı (yalnız açıq açar) ----
$cerPath = Join-Path $root 'service-package\DXDesk-signing.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
Write-Host "Sertifikat ixrac edildi: $cerPath"
Write-Host "Self-signed/daxili sertifikatdırsa, onu GPO ilə 'Trusted Root' və 'Trusted Publishers'-ə yayın."
