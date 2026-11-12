$ErrorActionPreference = "Stop"

$gateway = Join-Path $PSScriptRoot "..\vlm-gateway"
$python = Join-Path $gateway ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    throw "Project Python environment is missing. Create vlm-gateway\.venv and install requirements-dev.txt first."
}

if (-not $env:VLM_PROVIDER) {
    $env:VLM_PROVIDER = "ollama"
}
if (-not $env:VLM_MODEL) {
    $env:VLM_MODEL = "qwen2.5vl:3b"
}

Push-Location $gateway
try {
    & $python -m uvicorn app.main:app --host 127.0.0.1 --port 8765
    if ($LASTEXITCODE -ne 0) {
        throw "VLM gateway exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
