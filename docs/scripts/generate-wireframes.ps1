# generate-wireframes.ps1
# Regenerates all Excalidraw SVG images from .excalidraw source files using Kroki.io.
# Usage: .\docs\scripts\generate-wireframes.ps1
# Usage (single file): .\docs\scripts\generate-wireframes.ps1 -Filter "login"

param(
    [string]$Filter = ""
)

function Export-ExcalidrawToSvg {
    param([string]$excalidrawPath, [string]$svgPath)

    $content = Get-Content $excalidrawPath -Raw -Encoding UTF8
    $body = [System.Text.Encoding]::UTF8.GetBytes($content)

    try {
        Invoke-WebRequest `
            -Uri "https://kroki.io/excalidraw/svg" `
            -Method POST `
            -Body $body `
            -ContentType "text/plain; charset=utf-8" `
            -TimeoutSec 60 `
            -OutFile $svgPath
        $size = [math]::Round((Get-Item $svgPath).Length / 1024, 1)
        Write-Host "  OK  ($size KB) $([System.IO.Path]::GetFileName($svgPath))" -ForegroundColor Green
    } catch {
        Write-Host "  FAIL $([System.IO.Path]::GetFileName($excalidrawPath)): $_" -ForegroundColor Red
    }
}

$wireframesRoot = Split-Path $PSScriptRoot -Parent | Join-Path -ChildPath "wireframes"

$wireframes = Get-ChildItem -Path $wireframesRoot -Filter "*.excalidraw" |
    ForEach-Object {
        @{
            src = $_.FullName
            svg = [System.IO.Path]::ChangeExtension($_.FullName, ".svg")
        }
    }

$filtered = if ($Filter) { $wireframes | Where-Object { $_.src -like "*$Filter*" } } else { $wireframes }

Write-Host ""
Write-Host "Generating $($filtered.Count) wireframe(s) via Kroki.io..." -ForegroundColor Cyan
Write-Host ""

foreach ($w in $filtered) {
    Export-ExcalidrawToSvg -excalidrawPath $w.src -svgPath $w.svg
    Start-Sleep -Milliseconds 300
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
