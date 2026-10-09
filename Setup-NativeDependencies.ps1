<#
.SYNOPSIS
    Script automatizado para compilar e configurar as dependências nativas (SDL2 e FFmpeg) para UWP x64.
.DESCRIPTION
    Usa vcpkg para compilar FFmpeg (x64-uwp) e SDL2 (x64-uwp), copiando os DLLs diretamente para NativeLibs\x64.
#>

param(
    [string]$VcpkgRoot = "$PSScriptRoot\..\vcpkg"
)

$ErrorActionPreference = "Stop"

Write-Host "=== SysDVR UWP - Configuração de Dependências Nativas (x64) ===" -ForegroundColor Cyan

$TargetDir = Join-Path $PSScriptRoot "NativeLibs\x64"
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}

# 1. Verificar / Clonar vcpkg se necessário
if (-not (Test-Path (Join-Path $VcpkgRoot "vcpkg.exe"))) {
    Write-Host "[1/3] vcpkg não encontrado em '$VcpkgRoot'. Clonando e configurando..." -ForegroundColor Yellow
    git clone https://github.com/microsoft/vcpkg.git $VcpkgRoot
    & (Join-Path $VcpkgRoot "bootstrap-vcpkg.bat")
}

# 2. Instalar SDL2 e FFmpeg para UWP x64
Write-Host "[2/3] Instalando SDL2 e FFmpeg para o triplet x64-uwp via vcpkg..." -ForegroundColor Yellow
& (Join-Path $VcpkgRoot "vcpkg.exe") install `
    sdl2:x64-uwp `
    ffmpeg[core,avcodec,swscale,swresample]:x64-uwp `
    --recurse

# 3. Copiar DLLs compiladas para NativeLibs\x64
Write-Host "[3/3] Copiando DLLs compiladas para $TargetDir..." -ForegroundColor Yellow

$VcpkgBin = Join-Path $VcpkgRoot "installed\x64-uwp\bin"
$RequiredDlls = @("SDL2.dll", "avcodec*.dll", "avutil*.dll", "swscale*.dll", "swresample*.dll")

foreach ($pattern in $RequiredDlls) {
    $found = Get-ChildItem -Path $VcpkgBin -Filter $pattern -ErrorAction SilentlyContinue
    if ($found) {
        foreach ($file in $found) {
            Copy-Item -Path $file.FullName -Destination $TargetDir -Force
            Write-Host "  -> Copiado: $($file.Name)" -ForegroundColor Green
        }
    } else {
        Write-Warning "Arquivo '$pattern' não encontrado em $VcpkgBin"
    }
}

Write-Host "`nDependências nativas configuradas com sucesso em NativeLibs\x64!" -ForegroundColor Green
Write-Host "Você já pode abrir o SysDVR.UWP.sln ou SysDVR.UWP.csproj no Visual Studio 2022 e compilar para x64." -ForegroundColor Cyan
