# build-release.ps1 — Genera el instalador RGTools-Setup-<version>.exe listo para uso diario.
#
# Uso:
#   pwsh -File build-release.ps1                 # usa la <Version> actual del csproj
#   pwsh -File build-release.ps1 -Bump patch     # 1.0.0 -> 1.0.1  (correcciones)
#   pwsh -File build-release.ps1 -Bump minor     # 1.0.1 -> 1.1.0  (features)
#   pwsh -File build-release.ps1 -Bump major     # 1.1.0 -> 2.0.0  (cambios grandes)
#   pwsh -File build-release.ps1 -SetVersion 1.5.0   # fija una version exacta
#
# Pasos: (bump version) -> test -> publish single-file -> compila instalador con Inno Setup.

param(
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump,
    [string]$SetVersion
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
$csprojPath = "$root\RGTools.App\RGTools.App.csproj"

function Find-ISCC {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "Inno Setup (ISCC.exe) no encontrado. Instalalo con: winget install JRSoftware.InnoSetup"
}

function Get-CurrentVersion {
    [xml]$xml = Get-Content $csprojPath
    $v = ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
    if (-not $v) { throw "No se pudo leer <Version> del csproj." }
    return $v.Trim()
}

function Set-Version([string]$newVersion) {
    # Reemplazo textual para no reordenar/escapar el resto del csproj.
    $content = Get-Content $csprojPath -Raw
    $updated = [regex]::Replace($content, '<Version>.*?</Version>', "<Version>$newVersion</Version>")
    Set-Content -Path $csprojPath -Value $updated -NoNewline -Encoding utf8
}

function Step-Version([string]$current, [string]$part) {
    $p = $current.Split('.')
    while ($p.Count -lt 3) { $p += '0' }
    $maj = [int]$p[0]; $min = [int]$p[1]; $pat = [int]$p[2]
    switch ($part) {
        'major' { $maj++; $min = 0; $pat = 0 }
        'minor' { $min++; $pat = 0 }
        'patch' { $pat++ }
    }
    return "$maj.$min.$pat"
}

# 1) Resolver version
$version = Get-CurrentVersion
if ($SetVersion) {
    $version = $SetVersion
    Set-Version $version
    Write-Host "==> Version fijada a: $version" -ForegroundColor Cyan
}
elseif ($Bump) {
    $version = Step-Version $version $Bump
    Set-Version $version
    Write-Host "==> Version incrementada ($Bump): $version" -ForegroundColor Cyan
}
else {
    Write-Host "==> Version (sin cambios): $version" -ForegroundColor Cyan
}

Write-Host "==> 1/3 Tests..." -ForegroundColor Cyan
dotnet test "$root\RGTools.slnx" --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests fallaron. Release abortado." }

Write-Host "==> 2/3 Publish (single-file self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $csprojPath -c Release -r win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish fallo." }

Write-Host "==> 3/3 Instalador con Inno Setup..." -ForegroundColor Cyan
$iscc = Find-ISCC
& $iscc "/DAppVersion=$version" "$root\installer\RGTools.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup fallo." }

$out = Join-Path $root "releases\RGTools-Setup-$version.exe"
if (Test-Path $out) {
    $mb = [math]::Round((Get-Item $out).Length / 1MB, 1)
    Write-Host ""
    Write-Host "OK -> $out  ($mb MB)" -ForegroundColor Green
    Write-Host "Doble clic en ese .exe para instalar/actualizar." -ForegroundColor Green
}
else {
    throw "No se genero el instalador esperado en $out"
}
