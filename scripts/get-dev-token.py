#!/usr/bin/env python3
"""Gets an np-api access token for local testing: Authorization Code + PKCE against Auth0, like the
frontends. Opens your browser to sign in, catches the redirect on http://localhost:8765/callback,
and exchanges the code. Nothing to paste, no client secret (np-postman is a public client).

    scripts/get-dev-token.py                 # writes .dev-token (gitignored), prints the claims
    scripts/get-dev-token.py --out FILE      # write somewhere else
    scripts/get-dev-token.py --print         # also print the raw token (e.g. for Postman)

    curl -H "Authorization: Bearer $(cat .dev-token)" https://localhost:7223/api/v1/me -k

Overrides: NP_API_AUTH0_DOMAIN, NP_API_AUTH0_CLIENT_ID (terraform output spa_client_ids), NP_API_AUDIENCE.
Standard library only.
"""

import argparse
import base64
import hashlib
import http.server
import json
import os
import secrets
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
import webbrowser
from datetime import datetime, timezone

DOMAIN = os.environ.get("NP_API_AUTH0_DOMAIN", "nate-paxton.auth0.com")
CLIENT_ID = os.environ.get("NP_API_AUTH0_CLIENT_ID", "EgPoS5XFTbd2J7x7roGlynSVRlq2NwZq")  # np-postman
AUDIENCE = os.environ.get("NP_API_AUDIENCE", "np-api")
PORT = 8765
REDIRECT_URI = f"http://localhost:{PORT}/callback"
TIMEOUT_SECONDS = 300


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def decode_claims(token: str) -> dict:
    payload = token.split(".")[1]
    return json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))


def wait_for_code(expected_state: str) -> str:
    result: dict = {}
    done = threading.Event()

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            url = urllib.parse.urlparse(self.path)
            if url.path != "/callback":
                self.send_error(404)
                return
            params = dict(urllib.parse.parse_qsl(url.query))
            if params.get("state") != expected_state:
                result["error"] = "State mismatch; ignoring this response."
            elif "error" in params:
                result["error"] = f"{params['error']}: {params.get('error_description', '')}"
            else:
                result["code"] = params.get("code")
            ok = "code" in result
            self.send_response(200 if ok else 400)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.end_headers()
            message = "Signed in. You can close this tab." if ok else f"Sign-in failed: {result.get('error')}"
            self.wfile.write(f"<p style='font-family:sans-serif'>{message}</p>".encode())
            done.set()

        def log_message(self, *args):
            pass

    server = http.server.HTTPServer(("127.0.0.1", PORT), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        if not done.wait(TIMEOUT_SECONDS):
            sys.exit(f"Timed out after {TIMEOUT_SECONDS}s waiting for the sign-in redirect.")
    finally:
        server.shutdown()

    if "code" not in result:
        sys.exit(result.get("error", "No authorization code received."))
    return result["code"]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=".dev-token", help="file to write the access token to (default: .dev-token)")
    parser.add_argument("--print", action="store_true", help="also print the raw access token")
    parser.add_argument("--no-browser", action="store_true", help="print the sign-in URL instead of opening it")
    args = parser.parse_args()

    verifier = b64url(secrets.token_bytes(32))
    challenge = b64url(hashlib.sha256(verifier.encode()).digest())
    state = b64url(secrets.token_bytes(16))

    authorize_url = f"https://{DOMAIN}/authorize?" + urllib.parse.urlencode({
        "response_type": "code",
        "client_id": CLIENT_ID,
        "redirect_uri": REDIRECT_URI,
        "audience": AUDIENCE,
        "scope": "openid profile email",
        "code_challenge": challenge,
        "code_challenge_method": "S256",
        "state": state,
    })

    print(f"Sign in to get an access token for '{AUDIENCE}'...", file=sys.stderr)
    if args.no_browser or not webbrowser.open(authorize_url):
        print(f"Open this URL to sign in:\n{authorize_url}", file=sys.stderr)

    code = wait_for_code(state)

    body = urllib.parse.urlencode({
        "grant_type": "authorization_code",
        "client_id": CLIENT_ID,
        "code_verifier": verifier,
        "code": code,
        "redirect_uri": REDIRECT_URI,
    }).encode()
    request = urllib.request.Request(f"https://{DOMAIN}/oauth/token", data=body,
                                     headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            tokens = json.load(response)
    except urllib.error.HTTPError as error:
        sys.exit(f"Token exchange failed ({error.code}): {error.read().decode()}")

    token = tokens["access_token"]
    descriptor = os.open(args.out, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as file:
        file.write(token)

    claims = decode_claims(token)
    expires = datetime.fromtimestamp(claims["exp"], tz=timezone.utc).isoformat()
    print(json.dumps({
        "saved_to": args.out,
        "sub": claims.get("sub"),
        "aud": claims.get("aud"),
        "permissions": claims.get("permissions", []),
        "expires": expires,
    }, indent=2))
    if args.print:
        print(token)


if __name__ == "__main__":
    main()
