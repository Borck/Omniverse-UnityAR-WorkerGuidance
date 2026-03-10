$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\..\.."
$python = Join-Path $root ".venv\Scripts\python.exe"
$pytestConfig = Join-Path $root "server-kit\pytest.ini"

if (-not (Test-Path $python)) {
  throw "Python executable not found at $python. Activate or create .venv first."
}

$tests = @(
  "server-kit/tests/test_health.py",
  "server-kit/tests/test_session_bridge_http.py",
  "server-kit/tests/test_grpc_session_stub.py",
  "server-kit/tests/test_layer_stack_resolver.py",
  "server-kit/tests/test_export_pipeline.py",
  "server-kit/tests"
)

Write-Host "Running M12 backend validation matrix..." -ForegroundColor Cyan

foreach ($testPath in $tests) {
  Write-Host "-> pytest $testPath" -ForegroundColor Yellow
  & $python -m pytest -c $pytestConfig $testPath -q
  if ($LASTEXITCODE -ne 0) {
    throw "Validation failed for $testPath"
  }
}

Write-Host "All automated matrix checks passed." -ForegroundColor Green
