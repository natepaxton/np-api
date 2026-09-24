#!/usr/bin/env bash
# Fails when coverage in a ReportGenerator JSON summary is below the minimums.
#   scripts/check-coverage.sh [summary.json]
# Minimums can be overridden with MIN_LINE_COVERAGE / MIN_BRANCH_COVERAGE (percent).
set -euo pipefail

summary="${1:-coverage-report/Summary.json}"
min_line="${MIN_LINE_COVERAGE:-80}"
min_branch="${MIN_BRANCH_COVERAGE:-70}"

if [[ ! -f "$summary" ]]; then
  echo "Coverage summary not found: $summary" >&2
  exit 1
fi

line=$(jq -r '.summary.linecoverage // 0' "$summary")
branch=$(jq -r '.summary.branchcoverage // 0' "$summary")

status=0
check() {
  local name=$1 actual=$2 minimum=$3
  if awk -v a="$actual" -v m="$minimum" 'BEGIN { exit !(a < m) }'; then
    echo "::error title=Coverage below minimum::$name coverage is ${actual}%, minimum is ${minimum}%."
    status=1
  else
    echo "$name coverage ${actual}% (minimum ${minimum}%)"
  fi
}

check "Line" "$line" "$min_line"
check "Branch" "$branch" "$min_branch"
exit $status
