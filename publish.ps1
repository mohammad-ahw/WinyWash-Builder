<#
    publish.ps1
    ---------------------------------------------------------------------------
    اسکریپت build و publish رسمی WinyWash برای توزیع نهایی.

    چرا این اسکریپت لازم است؟
      `dotnet publish` بدون مشخص کردن Runtime، کل گراف RID (لینوکس، مک،
      browser-wasm، win-arm64 و ...) را بازیابی می کند. خروجی ~300MB می شود
      در حالی که فقط پوشه win-x64 حدود 40MB آن لازم است.
      این اسکریپت publish را به win-x64 محدود می کند و پوشه های بی استفاده
      را پاک می کند.

    ساختار خروجی:
      publish\
        WinyWash\        ← تمام فایل های اجرایی؛ WinyWash.exe همین جا است
        (ZIP در ریشه ریپو ساخته می شود)

    نمونه استفاده:
        .\publish.ps1
        .\publish.ps1 -Runtime win-arm64
        .\publish.ps1 -Output publish-debug -Configuration Debug -NoReadyToRun
#>

[CmdletBinding()]
param(
    # هدف build
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    # معماری هدف (بسته به سیستم کاربر نهایی)
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string]$Runtime = 'win-x64',

    # پوشه خروجی (نسبت به ریشه ریپو)
    [string]$Output = 'publish',

    # آیا runtime هم داخل خروجی باشد؟ (برای کاربران بدون .NET باید true باشد)
    [bool]$SelfContained = $true,

    # فایل های آماده سازی شده (ReadyToRun) سریع تر بالا می آیند ولی حجم را زیاد می کنند
    [switch]$NoReadyToRun,

    # نگه داشتن فایل های pdb برای دیباگ
    [switch]$Symbols
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\CleanC.UI\CleanC.UI.csproj'
$outDir = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
# ساختار تمیز: پوشه WinyWash شامل لانچر WinyWash.exe و پوشه هسته app و والپیپرهاست
$winyWashDir = Join-Path $outDir 'WinyWash'
$engineDir = Join-Path $winyWashDir 'app'

if (-not (Test-Path -LiteralPath $proj)) {
    throw "پروژه پیدا نشد: $proj"
}

Write-Host "==> WinyWash publish" -ForegroundColor Cyan
Write-Host "    Configuration : $Configuration"
Write-Host "    Runtime       : $Runtime"
Write-Host "    SelfContained : $SelfContained"
Write-Host "    Output        : $winyWashDir"
Write-Host "    Engine Dir    : $engineDir"

# خروجی قبلی app کاملاً حذف می شود تا فایل های RID قدیمی باقی نمانند
if (Test-Path -LiteralPath $engineDir) {
    Write-Host "==> حذف خروجی قبلی app..." -ForegroundColor DarkGray
    Remove-Item -LiteralPath $engineDir -Recurse -Force -ErrorAction SilentlyContinue
}

$publishArgs = @(
    'publish', $proj,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', "$SelfContained",
    '-o', $engineDir,
    '-p:Platform=x64',
    '-p:ErrorOnDuplicatePublishOutputFiles=false',
    '--nologo'
)

if ($NoReadyToRun) { $publishArgs += '-p:PublishReadyToRun=false' }
if ($Symbols)      { $publishArgs += '-p:DebugType=portable' }
else               { $publishArgs += '-p:DebugSymbols=false' }

Write-Host "==> dotnet $($publishArgs -join ' ')" -ForegroundColor DarkGray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "publish با کد خطا $LASTEXITCODE تمام شد."
}

# ---------------------------------------------------------------------------
# پاک سازی RIDهای اضافی: اگر به هر دلیلی RID غیر هدف بازیابی شد، حذفش کن
# ---------------------------------------------------------------------------
$targetNative = switch ($Runtime) {
    'win-x64'   { @('win', 'win-x64') }
    'win-x86'   { @('win', 'win-x86') }
    'win-arm64' { @('win', 'win-arm64') }
}

$runtimeRoot = Join-Path $engineDir 'runtimes'
if (Test-Path -LiteralPath $runtimeRoot) {
    Get-ChildItem -LiteralPath $runtimeRoot -Directory | ForEach-Object {
        if ($targetNative -notcontains $_.Name) {
            Write-Host "    حذف RID بلااستفاده: $($_.Name)" -ForegroundColor DarkGray
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    }

    # پوشه runtimes اگر خالی شد کلاً حذف می شود
    if (-not (Get-ChildItem -LiteralPath $runtimeRoot | Select-Object -First 1)) {
        Remove-Item -LiteralPath $runtimeRoot -Recurse -Force
    }
}

# فایل های pdb فقط با -Symbols نگه داشته می شوند
if (-not $Symbols) {
    Get-ChildItem -LiteralPath $engineDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

# حذف createdump.exe جهت رفع حساسیت های بی مورد آنتی ویروس و کاهش حجم
$dumpExe = Join-Path $engineDir 'createdump.exe'
if (Test-Path -LiteralPath $dumpExe) {
    Write-Host "==> حذف createdump.exe ..." -ForegroundColor DarkGray
    Remove-Item -LiteralPath $dumpExe -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# کامپایل لانچر رسمی ریشه WinyWash.exe
# ---------------------------------------------------------------------------
$launcherSrc = Join-Path $root 'src\CleanC.Launcher\Program.cs'
$launcherExe = Join-Path $winyWashDir 'WinyWash.exe'
$iconPath = Join-Path $root 'src\CleanC.UI\Assets\app.ico'
$manifestPath = Join-Path $root 'src\CleanC.UI\app.manifest'
$cscPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

Write-Host "==> کامپایل لانچر اصلی WinyWash.exe در ریشه..." -ForegroundColor Cyan
& $cscPath /target:winexe /platform:x64 /optimize+ "/win32icon:$iconPath" "/win32manifest:$manifestPath" "/out:$launcherExe" "$launcherSrc"
if ($LASTEXITCODE -ne 0) {
    throw "کامپایل لانچر WinyWash.exe با خطا مواجه شد."
}

# ---------------------------------------------------------------------------
# کپی دارایی های ظاهری و آیکون ها به پوشه توزیع
# ---------------------------------------------------------------------------
$assetsDir = Join-Path $winyWashDir 'assets'
$screenshotsDir = Join-Path $assetsDir 'screenshots'
if (-not (Test-Path $screenshotsDir)) { New-Item -ItemType Directory -Force -Path $screenshotsDir | Out-Null }
Copy-Item (Join-Path $root 'src/CleanC.UI/Assets/app.ico') (Join-Path $assetsDir 'winywash.ico') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'src/CleanC.UI/Assets/app-icon.png') (Join-Path $assetsDir 'app-icon.png') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'assets/logo.png') (Join-Path $assetsDir 'logo.png') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'assets/screenshots/acrylic-dark.png') (Join-Path $screenshotsDir 'acrylic-dark.png') -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root 'assets/screenshots/acrylic-light.png') (Join-Path $screenshotsDir 'acrylic-light.png') -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# گزارش حجم نهایی
# ---------------------------------------------------------------------------
$files = Get-ChildItem -LiteralPath $winyWashDir -Recurse -File
$sizeMb = ($files | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "==> publish کامل شد" -ForegroundColor Green
Write-Host ("    فایل ها : {0}" -f $files.Count)
$sizeMbStr = $sizeMb.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
Write-Host "    حجم    : $sizeMbStr MB"
Write-Host ("    اجرا    : {0}" -f ($launcherExe)) -ForegroundColor Green

# ایجاد آرشیو فشرده ZIP برای تحویل
$zipPath = Join-Path $root "WinyWash-Release-win-x64.zip"
Write-Host "==> فشرده سازی خروجی به $zipPath ..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$zipPy = Join-Path $root 'scratch_zip.py'
$pyCode = @"
import os, zipfile
base_dir = r'$winyWashDir'
zip_file = r'$zipPath'
with zipfile.ZipFile(zip_file, 'w', zipfile.ZIP_DEFLATED) as z:
    for foldername, subfolders, filenames in os.walk(base_dir):
        if '.git' in subfolders: subfolders.remove('.git')
        for filename in filenames:
            file_path = os.path.join(foldername, filename)
            arcname = os.path.relpath(file_path, base_dir).replace('\\', '/')
            z.write(file_path, arcname)
"@
[System.IO.File]::WriteAllText($zipPy, $pyCode, [System.Text.Encoding]::UTF8)
& python $zipPy
Remove-Item -LiteralPath $zipPy -Force -ErrorAction SilentlyContinue
$zipSizeMb = (Get-Item -LiteralPath $zipPath).Length / 1MB
$zipSizeMbStr = $zipSizeMb.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
Write-Host "==> آرشیو نهایی ایجاد شد: $zipPath ($zipSizeMbStr MB)" -ForegroundColor Green

# ایجاد فایل چکسام SHA256
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
$hashPath = Join-Path $root "WinyWash-Release-win-x64.zip.sha256"
Set-Content -Path $hashPath -Value "$hash *WinyWash-Release-win-x64.zip" -Encoding ASCII
Write-Host "==> SHA256: $hash" -ForegroundColor Yellow

