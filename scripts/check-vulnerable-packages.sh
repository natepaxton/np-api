#!/usr/bin/env bash
# Lists NuGet packages (direct and transitive) with known vulnerabilities and fails on the
# severities in FAIL_ON_SEVERITIES (default: High and Critical). Lower severities are reported as
# warnings. `dotnet package list` itself always exits 0, so its JSON output is checked here.
#   scripts/check-vulnerable-packages.sh [solution-or-project]
set -euo pipefail

target="${1:-NpApi.slnx}"
fail_on="${FAIL_ON_SEVERITIES:-High,Critical}"

report=$(dotnet package list --project "$target" --vulnerable --include-transitive --format json)

findings=$(jq -c '
  [ .projects[]
    | .path as $project
    | .frameworks[]?
    | (.topLevelPackages[]?, .transitivePackages[]?)
    | select(.vulnerabilities != null)
    | .vulnerabilities[] as $v
    | { project: ($project | split("/") | last), package: .id, version: .resolvedVersion,
        severity: $v.severity, advisory: $v.advisoryurl } ]
  | unique' <<<"$report")

count=$(jq 'length' <<<"$findings")
if [[ "$count" -eq 0 ]]; then
  echo "No vulnerable packages found."
  exit 0
fi

blocking=$(jq --arg fail "$fail_on" '[.[] | select(.severity as $s | $fail | split(",") | index($s))] | length' <<<"$findings")

jq -r '.[] | "\(.severity)\t\(.package) \(.version)\t\(.project)\t\(.advisory)"' <<<"$findings" | column -t -s $'\t'

if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "### Vulnerable packages"
    echo "| Severity | Package | Project | Advisory |"
    echo "| --- | --- | --- | --- |"
    jq -r '.[] | "| \(.severity) | \(.package) \(.version) | \(.project) | \(.advisory) |"' <<<"$findings"
  } >>"$GITHUB_STEP_SUMMARY"
fi

if [[ "$blocking" -gt 0 ]]; then
  echo "::error title=Vulnerable packages::$blocking finding(s) at severity $fail_on. Update the packages listed above."
  exit 1
fi

echo "::warning title=Vulnerable packages::$count lower-severity finding(s); not blocking."
