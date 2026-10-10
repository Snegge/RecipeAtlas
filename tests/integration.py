"""Run after dotnet build. Uses an isolated database, random password and local port."""
import base64
import copy
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "backend/RecipeAtlas.Api/bin/Debug/net10.0/RecipeAtlas.Api.dll"
DOTNET = os.environ.get("RECIPE_ATLAS_DOTNET", "dotnet")
if not DLL.exists():
    raise SystemExit("First run: dotnet build backend/RecipeAtlas.Api")

password = secrets.token_urlsafe(32)
hashed = subprocess.run([DOTNET, str(DLL), "--hash-password"], input=password + "\n",
                        text=True, capture_output=True, check=True).stdout.strip()
with socket.socket() as sock:
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
base = f"http://127.0.0.1:{port}"
cookies = http.cookiejar.CookieJar()
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))
checks = 0


def request(method, path, expected, data=None, csrf=True, authenticated=True):
    global checks
    headers = {"X-RecipeAtlas": "1"} if csrf else {}
    if isinstance(data, (dict, list)):
        data = json.dumps(data).encode()
        headers["Content-Type"] = "application/json"
    elif isinstance(data, bytes):
        headers["Content-Type"] = "application/octet-stream"
    req = urllib.request.Request(base + path, data=data, headers=headers, method=method)
    opener = client if authenticated else urllib.request.build_opener()
    try:
        response = opener.open(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    body = response.read()
    assert response.code == expected, (method, path, response.code, expected, body[:500])
    checks += 1
    if "json" in response.headers.get("Content-Type", "") and body:
        return json.loads(body)
    return body


with tempfile.TemporaryDirectory(prefix="recipeatlas-test-") as temporary:
    environment = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development",
                       ASPNETCORE_URLS=base, DataDirectory=temporary,
                       Owner__PasswordHash=hashed)
    log_path = Path(temporary) / "server.log"
    log = log_path.open("w+")
    process = None

    def start():
        running = subprocess.Popen([DOTNET, str(DLL)], cwd=ROOT, env=environment,
                                   stdout=log, stderr=subprocess.STDOUT)
        for _ in range(150):
            if running.poll() is not None:
                log.flush()
                raise RuntimeError(log_path.read_text())
            try:
                with urllib.request.urlopen(base + "/health", timeout=1) as response:
                    if response.status == 200:
                        return running
            except (OSError, urllib.error.URLError):
                time.sleep(0.1)
        running.terminate()
        running.wait(timeout=10)
        raise RuntimeError("Server did not start.\n" + log_path.read_text())

    try:
        process = start()
        request("GET", "/api/recipes", 401, authenticated=False)
        request("POST", "/api/auth/login", 403, {"password": password}, csrf=False)
        request("POST", "/api/auth/login", 401, {"password": "incorrect"})
        request("POST", "/api/auth/login", 204, {"password": password})
        request("GET", "/api/auth/me", 200)
        units = request("GET", "/api/units", 200)
        assert {unit["code"] for unit in units} >= {"g", "tbsp", "toTaste"}
        recipe = json.loads((ROOT / "sample-recipe.json").read_text())
        invalid = copy.deepcopy(recipe)
        invalid["ingredients"][0]["unit"] = "handful"
        request("POST", "/api/recipes", 400, invalid)
        invalid = copy.deepcopy(recipe)
        invalid["ingredients"] = [None]
        request("POST", "/api/recipes", 400, invalid)
        invalid = copy.deepcopy(recipe)
        invalid["ingredients"][0]["quantity"] = "-1"
        request("POST", "/api/recipes", 400, invalid)
        invalid = copy.deepcopy(recipe)
        invalid["sourceUrl"] = "javascript:alert(1)"
        request("POST", "/api/recipes", 400, invalid)
        created = request("POST", "/api/recipes", 201, recipe)
        path = "/api/recipes/" + created["id"]
        assert created["ingredients"][0]["quantity"] == "200"
        assert created["ingredients"][-1]["quantity"] == ""
        for quantity, expected in [("0,5", "0.5"), ("1/3", "1/3"), ("1 1/2", "1 1/2"),
                                   ("3–4", "3-4"), ("1/2 bis 1 1/2", "1/2-1 1/2")]:
            payload = copy.deepcopy(recipe)
            payload["ingredients"][0]["quantity"] = quantity
            result = request("PUT", path, 200, payload)
            assert result["ingredients"][0]["quantity"] == expected
            assert request("GET", path, 200)["ingredients"][0]["quantity"] == expected
        for quantity in [None, "", "1/0", "-1", "4-3", "0", "100001", "0.0001", "1" * 65, 2]:
            invalid = copy.deepcopy(recipe)
            invalid["ingredients"][0]["quantity"] = quantity
            request("PUT", path, 400, invalid)
        invalid = copy.deepcopy(recipe)
        invalid["ingredients"][-1]["quantity"] = None
        request("PUT", path, 400, invalid)
        invalid = copy.deepcopy(recipe)
        invalid["ingredients"][0]["unit"] = "lb"
        request("PUT", path, 400, invalid)
        request("PUT", path, 200, recipe)
        listing = request("GET", "/api/recipes?search=PASTA&pageSize=5", 200)
        assert listing["total"] == 1 and "ingredients" not in listing["items"][0]
        assert request("GET", "/api/recipes?search=olive", 200)["total"] == 1
        assert request("GET", "/api/recipes?search=%25", 200)["total"] == 0
        request("GET", "/api/recipes?pageSize=101", 400)
        recipe["title"] = "Updated pasta"
        recipe["ingredients"] = recipe["ingredients"][:1]
        recipe["ingredients"][0]["quantity"] = "3—4"
        recipe["steps"] = ["Replacement step"]
        updated = request("PUT", path, 200, recipe)
        assert updated["ingredients"][0]["quantity"] == "3-4"
        assert len(updated["ingredients"]) == 1 and updated["steps"] == recipe["steps"]
        request("PUT", path + "/image", 415, b"<svg></svg>")
        request("PUT", path + "/image", 413, b"x" * (5 * 1024 * 1024 + 1))
        png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWZkAAAAASUVORK5CYII=")
        request("PUT", path + "/image", 403, png, csrf=False)
        request("PUT", path + "/image", 200, png)
        assert request("GET", path + "/image", 200) == png
        request("GET", path + "/image", 401, authenticated=False)
        assert request("GET", path, 200)["imageUrl"] == path + "/image"

        # Prove database persistence AND cookie validity across a process restart.
        process.terminate()
        process.wait(timeout=10)
        process = start()
        persisted = request("GET", path, 200)
        assert persisted["title"] == "Updated pasta"
        assert persisted["ingredients"][0]["quantity"] == "3-4"
        assert request("GET", path + "/image", 200) == png
        request("DELETE", path + "/image", 204)
        request("GET", path + "/image", 404)
        assert request("GET", path, 200)["imageUrl"] is None
        request("PUT", path + "/image", 200, png)
        request("DELETE", path, 204)
        request("GET", path, 404)
        request("GET", path + "/image", 404)
        request("POST", "/api/auth/logout", 204)
        request("GET", "/api/recipes", 401)
        print(f"Passed {checks} HTTP checks, including persistence across restart.")
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            process.wait(timeout=10)
        log.close()
