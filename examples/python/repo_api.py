"""Standard-library HTTP client. Importing it does not send requests."""
import argparse
import json
import os
import sys
import uuid
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


class ApiError(Exception):
    def __init__(self, status, response):
        error = response.get("error", {})
        super().__init__(f"HTTP {status} {error.get('code', 'API_ERROR')}: {error.get('message', 'Request failed')}")
        self.status = status
        self.response = response  # Includes confirmed partial spawn results, if any.


class RepoApi:
    def __init__(self, token=None, base_url=None, timeout=15):
        self.token = (token if token is not None else os.environ.get("REPO_API_TOKEN", "")).strip()
        if not self.token:
            raise ValueError("Set REPO_API_TOKEN to AuthenticationToken from your BepInEx config.")
        self.base_url = (base_url or os.environ.get("REPO_API_URL", "http://127.0.0.1:17453")).rstrip("/")
        self.timeout = timeout

    def request(self, method, path, body=None, idempotency_key=None):
        if method not in ("GET", "POST") or not path.startswith("/") or path.startswith("//"):
            raise ValueError("Use GET or POST with an API path starting with one slash.")
        headers = {"Authorization": f"Bearer {self.token}"}
        payload = None
        if method == "POST":
            idempotency_key = idempotency_key or str(uuid.uuid4())
            headers.update({"Content-Type": "application/json", "Idempotency-Key": idempotency_key})
            payload = json.dumps(body if body is not None else {}).encode("utf-8")
            print(f"Idempotency-Key: {idempotency_key}", file=sys.stderr)
        request = Request(self.base_url + path, data=payload, headers=headers, method=method)
        try:
            with urlopen(request, timeout=self.timeout) as response:
                result = json.load(response)
                if result.get("success") is False:
                    raise ApiError(response.status, result)
                return result
        except HTTPError as error:
            try:
                result = json.loads(error.read().decode("utf-8"))
            except (ValueError, UnicodeError):
                result = {"error": {"code": "INVALID_RESPONSE", "message": "Expected a JSON API response; retain the printed key before retrying."}}
            raise ApiError(error.code, result) from None
        except (URLError, TimeoutError, OSError) as error:
            if method == "POST":
                raise RuntimeError(f"Response not confirmed. Retry the SAME path and body with Idempotency-Key {idempotency_key}; do not generate a new key.") from None
            raise RuntimeError("Cannot reach the API within the client timeout. Check the game and REPO_API_URL.") from None
        except (ValueError, UnicodeError):
            raise RuntimeError("Expected JSON. For a POST, retain the printed idempotency key before retrying.") from None

    def get(self, path):
        return self.request("GET", path)

    def post(self, path, body, key=None):
        return self.request("POST", path, body, key)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("method", choices=("GET", "POST"))
    parser.add_argument("path")
    parser.add_argument("body", nargs="?", help="JSON object for POST")
    parser.add_argument("key", nargs="?", help="Original idempotency key when retrying")
    args = parser.parse_args()
    try:
        result = RepoApi().request(args.method, args.path, json.loads(args.body) if args.body is not None else None, args.key)
        print(json.dumps(result, indent=2))
    except (ApiError, RuntimeError, ValueError) as error:
        print(str(error), file=sys.stderr)
        if isinstance(error, ApiError):
            print(json.dumps(error.response, indent=2), file=sys.stderr)
        sys.exit(1)
