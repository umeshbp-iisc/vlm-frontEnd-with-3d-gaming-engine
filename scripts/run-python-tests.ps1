$ErrorActionPreference = "Stop"

$gateway = Join-Path $PSScriptRoot "..\vlm-gateway"
$python = Join-Path $gateway ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    throw "Project Python environment is missing: $python"
}

Push-Location $gateway
try {
    & $python -m pytest -q
    if ($LASTEXITCODE -ne 0) {
        throw "Python gateway tests failed with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
