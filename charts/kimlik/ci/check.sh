#!/bin/sh
# Lints the chart and renders it with each example of values, and checks that it refuses what cannot work.
set -eu
cd "$(dirname "$0")/.."

for values in ci/*-values.yaml; do
  helm lint . --strict --values "$values"
  helm template kimlik . --values "$values" > /dev/null
done

refuses() {
  if helm template kimlik . "$@" > /dev/null 2>&1; then
    echo "The chart should refuse: $*" >&2
    exit 1
  fi
}

refuses --set secret.values.connectionString=x --set secret.values.masterKey=y
refuses --set publicUrl=id.example.com --set secret.values.connectionString=x --set secret.values.masterKey=y
refuses --set publicUrl=https://id.example.com/ --set secret.values.connectionString=x
refuses --values ci/quickstart-values.yaml --set migration.job.enabled=true
refuses --values ci/quickstart-values.yaml --set replicas=3

echo "The chart renders with every example, and refuses what it should."
