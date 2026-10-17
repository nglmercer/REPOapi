"""Discover an ID and spawn content only when this script is explicitly run."""
import argparse
import json
import sys
from urllib.parse import urlencode
from repo_api import ApiError, RepoApi

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("kind", choices=("item", "enemy", "valuable", "upgrade"))
parser.add_argument("search", nargs="?", default="")
parser.add_argument("quantity", nargs="?", type=int, default=1)
args = parser.parse_args()
try:
    api = RepoApi()
    if not api.get("/health")["gameplayReady"]:
        raise ValueError("Host or start singleplayer and enter a generated gameplay level first.")
    capabilities = api.get("/api/capabilities")
    if not capabilities.get("spawn", {}).get(args.kind):
        raise ValueError(f"Spawning {args.kind} is unavailable in this mod version.")
    maximum = capabilities["limits"]["maxSpawnQuantity"]
    if not 1 <= args.quantity <= maximum:
        raise ValueError(f"quantity must be an integer from 1 to {maximum}.")
    category = {"item": "items", "enemy": "enemies", "valuable": "valuables", "upgrade": "upgrades"}[args.kind]
    catalog = api.get(f"/api/catalog/{category}?" + urlencode({"search": args.search}))[category]
    entry = next((value for value in catalog if value.get("spawnable")), None)
    if entry is None:
        raise ValueError(f"No matching {args.kind}. Change the search or wait for content registration in a level.")
    print(f"Selected {entry['name']} ({entry['id']})", file=sys.stderr)
    body = {
        f"{args.kind}Id": entry["id"], "quantity": args.quantity,
        "position": {"mode": "random_player"},
    }
    print("Request body: " + json.dumps(body), file=sys.stderr)
    result = api.post(f"/api/spawn/{args.kind}", body)
    print(json.dumps(result, indent=2))
except (ApiError, RuntimeError, ValueError) as error:
    print(str(error), file=sys.stderr)
    if isinstance(error, ApiError):
        print(json.dumps(error.response, indent=2), file=sys.stderr)
    sys.exit(1)
