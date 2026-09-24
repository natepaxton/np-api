#!/usr/bin/env bash
# Starts the published API image in Production mode and checks it serves requests:
# /alive returns 200 and a protected route returns 401. The database is deliberately unreachable,
# because liveness must not depend on it and startup must not either.
#   scripts/smoke-test-image.sh [image]   (default: np-api:latest)
set -euo pipefail

image="${1:-np-api:latest}"
name="np-api-smoke-$$"
port="${SMOKE_PORT:-18080}"

cleanup() {
  if [[ $? -ne 0 ]]; then
    echo "--- container logs ---"
    docker logs "$name" 2>&1 | tail -50 || true
  fi
  docker rm -f "$name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --name "$name" -p "$port:8080" \
  -e Auth0__Domain=smoke-test.invalid \
  -e Auth0__Audience=np-api \
  -e Cloudinary__ApiKey=smoke-test \
  -e Cloudinary__ApiSecret=smoke-test \
  -e "ConnectionStrings__npdb=Host=db.invalid;Database=npdb;Username=x;Password=x;Timeout=2" \
  "$image" >/dev/null

for _ in $(seq 1 30); do
  if curl -fs "http://localhost:$port/alive" >/dev/null; then break; fi
  sleep 1
done

expect() {
  local path=$1 expected=$2 actual
  actual=$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:$port$path")
  if [[ "$actual" != "$expected" ]]; then
    echo "::error title=Container smoke test::GET $path returned $actual, expected $expected."
    return 1
  fi
  echo "GET $path -> $actual"
}

expect /alive 200
expect /api/v1/me 401
echo "Image $image started and served requests."
