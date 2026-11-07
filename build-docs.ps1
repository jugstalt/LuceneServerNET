# Builds the MkDocs site and copies it to the docs-gviewonline repository.
# Usage: ./build-docs.ps1 [-Target <path>] [-NoCopy]
param(
    [string]$Target = (Join-Path $PSScriptRoot "..\docs-gviewonline\app\other\luceneserver-net"),
    [switch]$NoCopy
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$venv = Join-Path $PSScriptRoot ".venv-docs"
if (-not (Test-Path $venv)) {
    python -m venv $venv
}
$python = if ($IsLinux -or $IsMacOS) { Join-Path $venv "bin/python" } else { Join-Path $venv "Scripts\python.exe" }
& $python -m pip install --quiet -r docs/requirements.txt
& $python -m mkdocs build --strict --clean
if ($LASTEXITCODE -ne 0) { throw "mkdocs build failed" }

if ($NoCopy) { return }

$site = Join-Path $PSScriptRoot "artifacts\docs\site"
if (Test-Path $Target) {
    Remove-Item -Recurse -Force $Target
}
New-Item -ItemType Directory -Force $Target | Out-Null
Copy-Item -Recurse -Force (Join-Path $site "*") $Target
Write-Host "Docs copied to $Target"
