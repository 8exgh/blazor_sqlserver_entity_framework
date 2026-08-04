#!/bin/sh
# Waits for the app to come up (it in turn waits for SQL Server), then runs the Playwright suite.
set -e

: "${E2E_BASE_URL:?E2E_BASE_URL must be set}"

echo "Waiting for $E2E_BASE_URL ..."
for i in $(seq 1 90); do
    if curl -fsS -o /dev/null "$E2E_BASE_URL"; then
        echo "App is up."
        break
    fi
    sleep 2
done
curl -fsS -o /dev/null "$E2E_BASE_URL" || { echo "App never became reachable" >&2; exit 1; }

exec dotnet test InvoiceRecon.E2E/InvoiceRecon.E2E.csproj -c Release --no-build \
    --logger "trx;LogFileName=e2e.trx" --results-directory /results
