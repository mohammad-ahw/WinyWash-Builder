<#
    publish.ps1
    ---------------------------------------------------------------------------
    اسکریپت build و publish رسمی CleanC برای توزیع نهایی.

    چرا این اسکریپت لازم است؟
      `dotnet publish` بدون مشخص کردن Runtime، کل گراف RID (لینوکس، مک،
      browser-wasm، win-arm64 و ...) را بازیابی می کند. خروجی ~300MB می شود
      در حالی که فقط پوشه win-x64 حدود 40MB آن لازم است.
      این اسکریپت publish را به win-x64 محدود می کند و پوشه های بی استفاده
      را پاک می کند.

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

if (-not (Test-Path -LiteralPath $proj)) {
    throw "پروژه پیدا نشد: $proj"
}

Write-Host "==> CleanC publish" -ForegroundColor Cyan
Write-Host "    Configuration : $Configuration"
Write-Host "    Runtime       : $Runtime"
Write-Host "    SelfContained : $SelfContained"
Write-Host "    Output        : $outDir"

# خروجی قبلی کاملاً حذف می شود تا فایل های RID قدیمی باقی نمانند
if (Test-Path -LiteralPath $outDir) {
    Write-Host "==> حذف خروجی قبلی..." -ForegroundColor DarkGray
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

$publishArgs = @(
    'publish', $proj,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', "$SelfContained",
    '-o', $outDir,
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

$runtimeRoot = Join-Path $outDir 'runtimes'
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
    Get-ChildItem -LiteralPath $outDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

# کپی والپیپرهای CleanC در صورت وجود
$wallpaperSrc = Join-Path $root 'wallpaper'
if (Test-Path -LiteralPath $wallpaperSrc) {
    Write-Host "==> کپی والپیپرهای CleanC..." -ForegroundColor DarkGray
    $wpDest = Join-Path $outDir 'wallpaper'
    if (Test-Path -LiteralPath $wpDest) { Remove-Item -LiteralPath $wpDest -Recurse -Force }
    Copy-Item -Path $wallpaperSrc -Destination $outDir -Recurse -Force
}

# ---------------------------------------------------------------------------
# گزارش حجم نهایی
# ---------------------------------------------------------------------------
$files = Get-ChildItem -LiteralPath $outDir -Recurse -File
$sizeMb = ($files | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Host ""
Write-Host "==> publish کامل شد" -ForegroundColor Green
Write-Host ("    فایل ها : {0}" -f $files.Count)
$sizeMbStr = $sizeMb.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
Write-Host "    حجم    : $sizeMbStr MB"
Write-Host ("    اجرا    : {0}" -f (Join-Path $outDir 'CleanC.UI.exe')) -ForegroundColor Green

# ایجاد آرشیو فشرده ZIP برای تحویل
$zipPath = Join-Path $root "CleanC-Release-win-x64.zip"
Write-Host "==> فشرده سازی خروجی به $zipPath ..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
$zipSizeMb = (Get-Item -LiteralPath $zipPath).Length / 1MB
$zipSizeMbStr = $zipSizeMb.ToString("0.0", [System.Globalization.CultureInfo]::InvariantCulture)
Write-Host "==> آرشیو نهایی ایجاد شد: $zipPath ($zipSizeMbStr MB)" -ForegroundColor Green

