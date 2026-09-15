# generate_pack.ps1 —— 生成 zip 与 latest.json 清单
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
$root = (Get-Item $scriptDir).Parent.FullName
$releaseDir = Join-Path $root "release"
$miscPath = Join-Path $root "src\Misc.cs"

$semVer = "1.0.0"
$fullVer = "1.0.0"

if (Test-Path $miscPath) {
    $content = Get-Content $miscPath -Raw -Encoding UTF8
    if ($content -match 'ActionVersion\s*=\s*"([^"]+)"') {
        $fullVer = $matches[1]
        if ($fullVer -match 'v(\d+\.\d+(?:\.\d+)?)') {
            $semVer = $matches[1]
            if ($semVer.Split('.').Count -eq 2) {
                $semVer = "$semVer.0"
            }
        }
    }
}

Write-Host "[*] Version: $fullVer (SemVer: $semVer)"

$zipName = "psmenu_standalone.zip"
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

$items = Get-ChildItem -LiteralPath $releaseDir | Where-Object { 
    $_.Name -ne $zipName -and $_.Name -ne "latest.json" -and $_.Name -ne "version.txt" 
}

if ($items.Count -eq 0) {
    Write-Error "No files found in $releaseDir to archive"
}

# 压缩
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in $items) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $item.FullName, $item.Name, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $zip.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLower()
$fileSize = (Get-Item -LiteralPath $zipPath).Length
Write-Host "[+] Zip created: $zipPath ($fileSize bytes, SHA256: $sha256)"

# 写入 version.txt
$versionFile = Join-Path $releaseDir "version.txt"
Set-Content -LiteralPath $versionFile -Value $semVer -Encoding UTF8

# 写入 latest.json
$dateStr = (Get-Date).ToString("yyyy-MM-dd")
$meta = @{
    version = $semVer
    full_version = $fullVer
    date = $dateStr
    note = "PSMenu 独立版一键分发压缩包"
    files = @(
        @{
            path = "psmenu_standalone.zip"
            url = "https://gitee.com/weizhiOWO/psmenu-standalone/raw/main/release/$zipName"
            fallbackUrl = "https://raw.githubusercontent.com/weizhi123sdo/psmenu-standalone/main/release/$zipName"
            sha256 = $sha256
            size = $fileSize
        }
    )
}

$jsonText = $meta | ConvertTo-Json -Depth 5
$latestFile = Join-Path $releaseDir "latest.json"
Set-Content -LiteralPath $latestFile -Value $jsonText -Encoding UTF8

Write-Host "[+] latest.json generated: $latestFile"
