# ICU Assist VLM Gateway

## Run locally

From this directory, activate the Python 3.11 environment and start the API:

```powershell
.\.venv\Scripts\Activate.ps1
$env:VLM_PROVIDER = "ollama"
$env:VLM_MODEL = "qwen2.5vl:3b"
uvicorn app.main:app --host 127.0.0.1 --port 8000
```

Ollama must be running locally with the configured vision model. To develop
without a local model, set `VLM_PROVIDER=mock`; the mock adapter is
deterministic and uses the same action validator.

Open `http://127.0.0.1:8000/docs` for the interactive API documentation.

## Tests

```powershell
.\.venv\Scripts\python.exe -m pytest
```
