#!/usr/bin/env bash
#
# Provisions the Azure resources for the Hacker News reader.
#
# Run this in Azure Cloud Shell (https://shell.azure.com) with Bash selected. Cloud Shell runs
# inside Azure, so it does not depend on local connectivity to management.azure.com — which is
# what blocks provisioning from this machine.
#
# Safe to re-run: every step checks for an existing resource first.

set -euo pipefail

RG=hackernews-rg
LOCATION=centralus
PLAN=hackernews-plan
API=hackernews-api-tm101
SWA=hackernews-ui-tm101
SWA_LOCATION=centralus          # Static Web Apps is available in a subset of regions
RUNTIME="DOTNETCORE|10.0"

echo "Subscription: $(az account show --query name -o tsv)"
echo

step() { printf '\n=== %s ===\n' "$1"; }
have() { [ -n "$(eval "$1" 2>/dev/null)" ]; }

step "Resource providers"
for p in Microsoft.Web; do
  state=$(az provider show -n "$p" --query registrationState -o tsv)
  if [ "$state" != "Registered" ]; then
    echo "registering $p ..."
    az provider register -n "$p" --wait
  fi
  echo "  $p: $(az provider show -n "$p" --query registrationState -o tsv)"
done

step "Resource group"
if have "az group show -n $RG --query name -o tsv"; then
  echo "  $RG already exists"
else
  az group create -n "$RG" -l "$LOCATION" -o none
  echo "  created $RG"
fi

step "App Service plan (F1 Free)"
if have "az appservice plan show -n $PLAN -g $RG --query name -o tsv"; then
  echo "  $PLAN already exists"
else
  az appservice plan create -n "$PLAN" -g "$RG" --is-linux --sku F1 -l "$LOCATION" -o none
  echo "  created $PLAN"
fi

step "Web App"
if have "az webapp show -n $API -g $RG --query name -o tsv"; then
  echo "  $API already exists"
else
  az webapp create -n "$API" -g "$RG" -p "$PLAN" --runtime "$RUNTIME" -o none
  echo "  created $API"
fi

step "Web App configuration"
# TrustForwardedHeaders is not optional in production: without it every caller shares one
# rate-limit bucket (App Service terminates the connection, so Kestrel only ever sees the
# front end's address) and HSTS is never emitted.
az webapp config appsettings set -n "$API" -g "$RG" -o none --settings \
  Network__TrustForwardedHeaders=true \
  ASPNETCORE_ENVIRONMENT=Production

# HTTPS Only is the redirect the app deliberately does not perform itself.
az webapp update -n "$API" -g "$RG" --https-only true -o none

# App Service recycles an instance whose probe fails, which on F1 also bins the warm cache.
az webapp config set -n "$API" -g "$RG" --generic-configurations '{"healthCheckPath": "/health"}' -o none || \
  echo "  (health check path not set — configure it in the portal if needed)"

echo "  settings applied"

step "Static Web App (Free)"
if have "az staticwebapp show -n $SWA -g $RG --query name -o tsv"; then
  echo "  $SWA already exists"
else
  az staticwebapp create -n "$SWA" -g "$RG" -l "$SWA_LOCATION" --sku Free -o none
  echo "  created $SWA"
fi

step "Wire CORS: let the UI origin call the API"
SWA_HOST=$(az staticwebapp show -n "$SWA" -g "$RG" --query defaultHostname -o tsv)
az webapp config appsettings set -n "$API" -g "$RG" -o none --settings \
  Cors__AllowedOrigins__0="https://$SWA_HOST"
echo "  allowed origin: https://$SWA_HOST"

step "Done — URLs"
echo "  API : https://$(az webapp show -n "$API" -g "$RG" --query defaultHostName -o tsv)"
echo "  UI  : https://$SWA_HOST"

step "Secrets to add to GitHub"
cat <<NOTE
Two secrets are needed by the deploy workflows. Print them with the commands below, then add
each one at:
  https://github.com/tariqmehmood101/hackernews-reader/settings/secrets/actions

1) AZURE_API_PUBLISH_PROFILE
   az webapp deployment list-publishing-profiles -n $API -g $RG --xml

2) AZURE_SWA_DEPLOYMENT_TOKEN
   az staticwebapp secrets list -n $SWA -g $RG --query properties.apiKey -o tsv

Treat both as credentials: they grant deploy rights until rotated.
NOTE
