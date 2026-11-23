#!/usr/bin/env pwsh
# Pre-package hook (azd runs it before it packages the Functions project, inside "azd up" and "azd deploy").
#
# Builds the product's web app from src/ui/harness: "npm ci --ignore-scripts" from the lock file (no package install
# script runs, as in every other install of these packages), then "npm run build:app", which writes
# dist/app. The Functions project copies dist/app into wwwroot/app of its package, and the app serves it under /app on
# the same origin as the API. The build holds no secret and no tenant value: the app writes /app/config.json from its
# own settings at run time.
#
# Needs: Node.js 22 or later with npm, and network access to the npm registry.

$ErrorActionPreference = 'Stop'

$harness = Join-Path $PSScriptRoot '..' '..' '..' '..' 'ui' 'harness'
$harness = (Resolve-Path $harness).Path
if (-not (Test-Path (Join-Path $harness 'package-lock.json'))) {
    throw "The web app's source is not at $harness."
}

Push-Location $harness
try {
    npm ci --no-audit --no-fund --ignore-scripts
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    npm run build:app
    if ($LASTEXITCODE -ne 0) { throw 'npm run build:app failed.' }
}
finally {
    Pop-Location
}

if (-not (Test-Path (Join-Path $harness 'dist' 'app' 'index.html'))) {
    throw 'The web app build wrote no dist/app/index.html.'
}

Write-Host "The web app is built in $(Join-Path $harness 'dist' 'app')."
