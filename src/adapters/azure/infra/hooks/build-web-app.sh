#!/bin/sh
# Pre-package hook (azd runs it before it packages the Functions project, inside "azd up" and "azd deploy").
#
# Builds the product's web app from src/ui/harness: "npm ci --ignore-scripts" from the lock file (no package install
# script runs, as in every other install of these packages), then "npm run build:app", which writes
# dist/app. The Functions project copies dist/app into wwwroot/app of its package, and the app serves it under /app on
# the same origin as the API. The build holds no secret and no tenant value: the app writes /app/config.json from its
# own settings at run time.
#
# Needs: Node.js 22 or later with npm, and network access to the npm registry.

set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
harness=$(CDPATH= cd -- "$script_dir/../../../../ui/harness" && pwd)
if [ ! -f "$harness/package-lock.json" ]; then
    echo "The web app's source is not at $harness." >&2
    exit 1
fi

cd "$harness"
npm ci --no-audit --no-fund --ignore-scripts
npm run build:app

if [ ! -f "$harness/dist/app/index.html" ]; then
    echo "The web app build wrote no dist/app/index.html." >&2
    exit 1
fi

echo "The web app is built in $harness/dist/app."
