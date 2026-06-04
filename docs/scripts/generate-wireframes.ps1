# generate-wireframes.ps1
# Regenerates all Excalidraw SVG images from .excalidraw source files using Kroki.io.
# Usage: .\docs\scripts\generate-wireframes.ps1
# Usage (single file): .\docs\scripts\generate-wireframes.ps1 -Filter "login"

param(
    [string]$Filter = "",
    [switch]$Changed
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
$sharedWireframesRoot = Join-Path $docsRoot "wireframes"
$useCasesRoot   = Join-Path $docsRoot "use-cases"

# Collect from docs/wireframes/ and docs/use-cases/*/* (use-case folders)
$wireframes = @()
$wireframes += Get-ChildItem -Path $sharedWireframesRoot -Filter "*.excalidraw" -Recurse
$wireframes += Get-ChildItem -Path $useCasesRoot -Filter "*.excalidraw" -Recurse |
    Where-Object {
        $_.DirectoryName -notmatch '[\\/]architecture[\\/]diagrams$'
    }
$wireframes = $wireframes | ForEach-Object {
    @{
        src = $_.FullName
        svg = [System.IO.Path]::ChangeExtension($_.FullName, ".svg")
    }
}

function Normalize-RepoPath {
    param([string]$Path)
    return $Path.Replace("\", "/").TrimStart("./")
}

function Resolve-LinkedWireframe {
    param([string]$MarkdownPath, [string]$Href)

    $cleanHref = ($Href -split "#")[0].Trim()
    if (-not $cleanHref.EndsWith(".excalidraw")) { return $null }
    if ($cleanHref.StartsWith("docs/")) { return Normalize-RepoPath $cleanHref }

    $markdownDir = Split-Path (Normalize-RepoPath $MarkdownPath) -Parent
    $relativeHref = if ($cleanHref.StartsWith("./")) { $cleanHref.Substring(2) } else { $cleanHref }
    return Normalize-RepoPath (Join-Path $markdownDir $relativeHref)
}

$changedExcalidraws = @{}
if ($Changed) {
    $statusLines = git -C (Split-Path $docsRoot -Parent) status --porcelain -- docs
    foreach ($line in $statusLines) {
        if (-not $line) { continue }
        $changedPath = Normalize-RepoPath $line.Substring(3)
        if ($changedPath.Contains(" -> ")) {
            $changedPath = Normalize-RepoPath (($changedPath -split " -> ")[-1])
        }

        if ($changedPath.EndsWith(".excalidraw")) {
            $changedExcalidraws[$changedPath] = $true
        } elseif ($changedPath.EndsWith(".svg")) {
            $changedExcalidraws[(Normalize-RepoPath ([System.IO.Path]::ChangeExtension($changedPath, ".excalidraw")))] = $true
        } elseif ($changedPath.EndsWith(".md") -and (Test-Path $changedPath)) {
            $content = Get-Content $changedPath -Raw -Encoding UTF8
            $matches = [regex]::Matches($content, "\]\(([^)]+\.excalidraw(?:#[^)]+)?)\)")
            foreach ($match in $matches) {
                $linkedPath = Resolve-LinkedWireframe -MarkdownPath $changedPath -Href $match.Groups[1].Value
                if ($linkedPath) {
                    $changedExcalidraws[$linkedPath] = $true
                }
            }
        }
    }
}

$filtered = $wireframes
if ($Changed) {
    $filtered = $filtered | Where-Object {
        $repoPath = Normalize-RepoPath (Resolve-Path -Relative $_.src)
        $changedExcalidraws.ContainsKey($repoPath)
    }
}
if ($Filter) {
    $filtered = $filtered | Where-Object { $_.src -like "*$Filter*" }
}

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
