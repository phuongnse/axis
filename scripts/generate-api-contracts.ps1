$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build src\Axis.Api\Axis.Api.csproj --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Push-Location src\Axis.Api
    try {
        dotnet tool run swagger tofile --output "$root\openapi.json" "bin\Debug\net8.0\Axis.Api.dll" v1
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }

    Push-Location frontend
    try {
        npm run gen:api-types
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
