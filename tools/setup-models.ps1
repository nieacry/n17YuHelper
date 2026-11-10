<#
  n17 YuHelper - download llama.cpp (llama-server) and the local models.

  Usage (from the project folder):
      tools\setup-models.cmd                 everything that is missing
      tools\setup-models.cmd -SkipRuntime    models only
      tools\setup-models.cmd -SkipOcr        skip the OCR model
      tools\setup-models.cmd -SkipText       skip the translation model
      tools\setup-models.cmd -Qwen           also download Qwen3.5 9B (second translation model, ~5.7 GB)
      tools\setup-models.cmd -Cuda12         force the CUDA 12.4 build (older drivers)
      tools\setup-models.cmd -UpdateRuntime  re-download the latest llama-server

  Files go to:
      runtime\llama-server\                  llama-server.exe + CUDA DLLs
      models\ocr\paddleocr-vl-1.6\           PaddleOCR-VL-1.6 (about 1.8 GB)
      models\llm\gemma-4-12b-qat\            Gemma 4 12B QAT Q4_0 (about 7 GB)
#>
param(
    [switch]$SkipRuntime,
    [switch]$SkipOcr,
    [switch]$SkipText,
    [switch]$Qwen,
    [switch]$Cuda12,
    [switch]$UpdateRuntime
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$Root    = Split-Path -Parent $PSScriptRoot
$Runtime = Join-Path $Root 'runtime\llama-server'
$OcrDir  = Join-Path $Root 'models\ocr\paddleocr-vl-1.6'
$TextDir = Join-Path $Root 'models\llm\gemma-4-12b-qat'
$Temp    = Join-Path $env:TEMP 'n17YuHelper-setup'

function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }

function Get-File([string]$Url, [string]$Dest) {
    if (Test-Path $Dest) {
        Write-Host "  already there: $(Split-Path -Leaf $Dest)"
        return
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Dest) | Out-Null
    $part = "$Dest.part"
    Write-Host "  downloading $(Split-Path -Leaf $Dest)"
    # curl.exe is built into Windows 10/11: shows progress and can resume (-C -).
    & curl.exe -L --fail --retry 3 --retry-delay 2 -C - -o $part $Url
    if ($LASTEXITCODE -ne 0) { throw "Download failed ($LASTEXITCODE): $Url" }
    Move-Item -Force $part $Dest
}

# ------------------------------------------------------------------ llama-server

if (-not $SkipRuntime) {
    Step 'llama.cpp (llama-server)'
    $exe = Join-Path $Runtime 'llama-server.exe'

    if ((Test-Path $exe) -and -not $UpdateRuntime) {
        Write-Host "  already there: $exe"
        Write-Host '  (use -UpdateRuntime to download the latest build)'
    }
    else {
        # CUDA 13 builds need driver 580+; older drivers use CUDA 12.4.
        $cudaMajor = '12'
        if (-not $Cuda12) {
            try {
                $driver = (& nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>$null | Select-Object -First 1).Trim()
                Write-Host "  NVIDIA driver: $driver"
                if ([int]($driver.Split('.')[0]) -ge 580) { $cudaMajor = '13' }
            }
            catch {
                Write-Host '  nvidia-smi not found - using the CUDA 12.4 build' -ForegroundColor Yellow
            }
        }

        # "latest" can be a versioned release (v0.9.0) with source code only;
        # the Windows binaries are on the nightly b#### releases. Take the newest one that has them.
        $releases = Invoke-RestMethod -Uri 'https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=30' -Headers @{ 'User-Agent' = 'n17YuHelper-setup' }
        $main = $null; $cudart = $null; $tag = $null
        foreach ($release in $releases) {
            $m = $release.assets | Where-Object { $_.name -match "^llama-.+-bin-win-cuda-$cudaMajor\.\d+-x64\.zip$" } | Select-Object -First 1
            $c = $release.assets | Where-Object { $_.name -match "^cudart-llama-bin-win-cuda-$cudaMajor\.\d+-x64\.zip$" } | Select-Object -First 1
            if ($m -and $c) { $main = $m; $cudart = $c; $tag = $release.tag_name; break }
        }
        if (-not $main) { throw "No CUDA $cudaMajor Windows build found in the last 30 llama.cpp releases" }

        Write-Host "  release $tag : $($main.name) + $($cudart.name)"
        New-Item -ItemType Directory -Force -Path $Temp | Out-Null
        $zip1 = Join-Path $Temp $main.name
        $zip2 = Join-Path $Temp $cudart.name
        Get-File $main.browser_download_url $zip1
        Get-File $cudart.browser_download_url $zip2

        # Stop a running llama-server from this folder before replacing files.
        Get-Process llama-server -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Runtime*" } | Stop-Process -Force

        New-Item -ItemType Directory -Force -Path $Runtime | Out-Null
        Expand-Archive -Force -Path $zip1 -DestinationPath $Runtime
        Expand-Archive -Force -Path $zip2 -DestinationPath $Runtime
        Set-Content -Path (Join-Path $Runtime 'VERSION.txt') -Value "$tag`n$($main.name)`n$($cudart.name)"

        if (-not (Test-Path $exe)) { throw "llama-server.exe not found after unpacking into $Runtime" }
        Remove-Item -Force $zip1, $zip2
        Write-Host "  ok: $exe" -ForegroundColor Green
    }
}

# ------------------------------------------------------------------ OCR model

if (-not $SkipOcr) {
    Step 'OCR model: PaddleOCR-VL-1.6 (about 1.8 GB)'
    $base = 'https://huggingface.co/PaddlePaddle/PaddleOCR-VL-1.6-GGUF/resolve/main'
    foreach ($f in 'PaddleOCR-VL-1.6-GGUF.gguf', 'PaddleOCR-VL-1.6-GGUF-mmproj.gguf', 'chat_template.jinja') {
        Get-File "$base/$f" (Join-Path $OcrDir $f)
    }
    Write-Host "  ok: $OcrDir" -ForegroundColor Green
}

# ------------------------------------------------------------------ Translation model

if (-not $SkipText) {
    Step 'Translation model: Gemma 4 12B QAT Q4_0 (about 7 GB)'
    $base = 'https://huggingface.co/google/gemma-4-12B-it-qat-q4_0-gguf/resolve/main'
    Get-File "$base/gemma-4-12b-it-qat-q4_0.gguf" (Join-Path $TextDir 'gemma-4-12b-it-qat-q4_0.gguf')
    Get-File "$base/mmproj-gemma-4-12b-it-qat-q4_0.gguf" (Join-Path $TextDir 'mmproj-gemma-4-12b-it-qat-q4_0.gguf')   # voice input
    Write-Host "  ok: $TextDir" -ForegroundColor Green
}

# ------------------------------------------------------------------ Qwen (optional)

if ($Qwen) {
    Step 'Second translation model: Qwen3.5 9B Q4_K_M (about 5.7 GB)'
    $QwenDir = Join-Path $Root 'models\llm\qwen3.5-9b'
    Get-File 'https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf' (Join-Path $QwenDir 'Qwen3.5-9B-Q4_K_M.gguf')
    Write-Host "  ok: $QwenDir  (choose it in Settings > Models > Translation model)" -ForegroundColor Green
}

Step 'Done'
Write-Host '  Start (or restart) n17 YuHelper: Ctrl+Shift+Win+O = OCR, Ctrl+Shift+Win+T = translate.'
