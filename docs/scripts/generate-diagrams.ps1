# generate-diagrams.ps1
# Regenerates all PlantUML PNG images from .puml source files using Kroki.io.
# Usage: .\docs\scripts\generate-diagrams.ps1
# Usage (single file): .\docs\scripts\generate-diagrams.ps1 -Filter "execution-flow"

param(
    [string]$Filter = ""
)

function Export-PlantUmlToPng {
    param([string]$pumlPath, [string]$pngPath)

    $content = Get-Content $pumlPath -Raw -Encoding UTF8
    $body = [System.Text.Encoding]::UTF8.GetBytes($content)

    try {
        Invoke-WebRequest `
            -Uri "https://kroki.io/plantuml/png" `
            -Method POST `
            -Body $body `
            -ContentType "text/plain; charset=utf-8" `
            -TimeoutSec 30 `
            -OutFile $pngPath
        $size = [math]::Round((Get-Item $pngPath).Length / 1024, 1)
        Write-Host "  OK  ($size KB) $([System.IO.Path]::GetFileName($pngPath))" -ForegroundColor Green
    } catch {
        Write-Host "  FAIL $([System.IO.Path]::GetFileName($pumlPath)): $_" -ForegroundColor Red
    }
}

$docsRoot = Split-Path $PSScriptRoot -Parent

$diagrams = @(
    # System-level diagrams
    @{ puml = "$docsRoot\diagrams\system-context.puml";                                       png = "$docsRoot\diagrams\system-context.png" },
    @{ puml = "$docsRoot\diagrams\container-diagram.puml";                                    png = "$docsRoot\diagrams\container-diagram.png" },
    @{ puml = "$docsRoot\diagrams\module-overview.puml";                                      png = "$docsRoot\diagrams\module-overview.png" },

    # Epic diagrams
    @{ puml = "$docsRoot\epics\E01-platform-foundation\diagrams\tenant-provisioning.puml";    png = "$docsRoot\epics\E01-platform-foundation\diagrams\tenant-provisioning.png" },
    @{ puml = "$docsRoot\epics\E02-identity-access\diagrams\auth-flow.puml";                  png = "$docsRoot\epics\E02-identity-access\diagrams\auth-flow.png" },
    @{ puml = "$docsRoot\epics\E03-data-modeling\diagrams\data-model.puml";                   png = "$docsRoot\epics\E03-data-modeling\diagrams\data-model.png" },
    @{ puml = "$docsRoot\epics\E04-workflow-builder\diagrams\workflow-model.puml";             png = "$docsRoot\epics\E04-workflow-builder\diagrams\workflow-model.png" },
    @{ puml = "$docsRoot\epics\E05-form-builder\diagrams\form-model.puml";                    png = "$docsRoot\epics\E05-form-builder\diagrams\form-model.png" },
    @{ puml = "$docsRoot\epics\E06-workflow-engine\diagrams\execution-flow.puml";              png = "$docsRoot\epics\E06-workflow-engine\diagrams\execution-flow.png" }
)

$filtered = if ($Filter) { $diagrams | Where-Object { $_.puml -like "*$Filter*" } } else { $diagrams }

Write-Host ""
Write-Host "Generating $($filtered.Count) diagram(s) via Kroki.io..." -ForegroundColor Cyan
Write-Host ""

foreach ($d in $filtered) {
    Export-PlantUmlToPng -pumlPath $d.puml -pngPath $d.png
    Start-Sleep -Milliseconds 300
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
