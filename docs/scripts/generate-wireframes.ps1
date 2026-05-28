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
        return $true
    } catch {
        Write-Host "  FAIL $([System.IO.Path]::GetFileName($excalidrawPath)): $_" -ForegroundColor Red
        return $false
    }
}

$docsRoot      = Split-Path $PSScriptRoot -Parent
$wireframesRoot = Join-Path $docsRoot "wireframes"
$useCasesRoot   = Join-Path $docsRoot "use-cases"

# Collect from docs/wireframes/ (shared + template) and docs/use-cases/*/wireframes/
$wireframes = @()
$wireframes += Get-ChildItem -Path $wireframesRoot -Filter "*.excalidraw" -Recurse
$wireframes += Get-ChildItem -Path $useCasesRoot -Filter "*.excalidraw" -Recurse |
    Where-Object { $_.DirectoryName -match '[\\/]wireframes$' }
$wireframes = $wireframes | ForEach-Object {
    @{
        src = $_.FullName
        svg = [System.IO.Path]::ChangeExtension($_.FullName, ".svg")
    }
}

$filtered = if ($Filter) { $wireframes | Where-Object { $_.src -like "*$Filter*" } } else { $wireframes }

Write-Host ""
Write-Host "Generating $($filtered.Count) wireframe(s) via Kroki.io..." -ForegroundColor Cyan
Write-Host ""

$failed = 0
foreach ($w in $filtered) {
    $ok = Export-ExcalidrawToSvg -excalidrawPath $w.src -svgPath $w.svg
    if (-not $ok) { $failed++ }
    Start-Sleep -Milliseconds 300
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "Done with $failed failure(s)." -ForegroundColor Red
    exit 1
}
Write-Host "Done." -ForegroundColor Cyan
