#!/usr/bin/env python3
"""Headless-browser regression for the local RESET confirmation flow."""
from __future__ import annotations

import http.server
import json
import os
import subprocess
import threading
import time
import urllib.error
import urllib.request

APP_URL = "http://127.0.0.1:5080/"
WEBDRIVER = "http://127.0.0.1:9515"
ELEMENT_KEY = "element-6066-11e4-a52e-4f735466cecf"


class FakeOllama(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        payload = b'{"models":[]}'
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def do_POST(self):
        # Make the reset's model-backed crew generation visibly asynchronous,
        # then fail so production's deterministic fallback completes the reset.
        time.sleep(1.5)
        self.send_response(500)
        self.send_header("Content-Length", "0")
        self.end_headers()


def request(method: str, url: str, payload=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(
        url,
        data=data,
        method=method,
        headers={"Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=10) as response:
        raw = response.read()
        return json.loads(raw) if raw else {}


def wait_until(predicate, timeout=20, interval=0.1, message="condition"):
    deadline = time.time() + timeout
    last_error = None
    while time.time() < deadline:
        try:
            value = predicate()
            if value:
                return value
        except Exception as exc:  # transient DOM/navigation state
            last_error = exc
        time.sleep(interval)
    raise AssertionError(f"Timed out waiting for {message}: {last_error}")


def main():
    fake = http.server.ThreadingHTTPServer(("127.0.0.1", 11435), FakeOllama)
    fake_thread = threading.Thread(target=fake.serve_forever, daemon=True)
    fake_thread.start()

    env = os.environ.copy()
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development",
        "ASPNETCORE_URLS": APP_URL.rstrip("/"),
        "OLLAMA_ENDPOINT": "http://127.0.0.1:11435",
        "OLLAMA_MODEL_NAME": "qwen3:4b",
    })
    app = subprocess.Popen(
        ["dotnet", "run", "--project", "src/Overseer.Web/Overseer.Web.csproj",
         "-c", "Release", "--no-build", "--no-launch-profile"],
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    driver = None
    session_id = None
    try:
        def server_ready():
            if app.poll() is not None:
                output = app.stdout.read() if app.stdout is not None else ""
                raise RuntimeError(f"Local server exited with {app.returncode}:\n{output}")
            return urllib.request.urlopen(APP_URL, timeout=3).status == 200

        wait_until(
            server_ready,
            timeout=30,
            message="local server",
        )

        driver = subprocess.Popen(
            ["chromedriver", "--port=9515", "--log-level=SEVERE"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.STDOUT,
        )
        wait_until(
            lambda: request("GET", WEBDRIVER + "/status").get("value", {}).get("ready"),
            timeout=10,
            message="ChromeDriver",
        )
        created = request("POST", WEBDRIVER + "/session", {
            "capabilities": {
                "alwaysMatch": {
                    "browserName": "chrome",
                    "goog:chromeOptions": {
                        "args": ["--headless=new", "--no-sandbox", "--disable-dev-shm-usage"]
                    },
                }
            }
        })
        session_id = created["value"]["sessionId"]
        base = f"{WEBDRIVER}/session/{session_id}"
        request("POST", base + "/url", {"url": APP_URL})

        # Owner #99: validate the actual browser cascade, not just source text.
        # Reuse a real rendered room so Blazor's CSS-isolation attribute remains present.
        wait_until(
            lambda: request("POST", base + "/execute/sync", {
                "script": "return !!document.querySelector('.station-authority-layer .room-node[data-room-id]');",
                "args": [],
            }).get("value"),
            timeout=15,
            message="rendered station room",
        )
        fire_style = request("POST", base + "/execute/sync", {
            "script": """
                const room = document.querySelector('.station-authority-layer .room-node[data-room-id]');
                if (!room) return null;
                room.classList.add('has-fire', 'fire-inferno');
                room.style.setProperty('--fire-x', '50%');
                room.style.setProperty('--fire-y', '50%');
                room.style.setProperty('--fire-r', '35%');
                const pseudo = getComputedStyle(room, '::after');
                const result = { backgroundImage: pseudo.backgroundImage, content: pseudo.content };
                room.classList.remove('has-fire', 'fire-inferno');
                room.style.removeProperty('--fire-x');
                room.style.removeProperty('--fire-y');
                room.style.removeProperty('--fire-r');
                return result;
            """,
            "args": [],
        }).get("value")
        if not fire_style:
            raise AssertionError("Could not inspect a rendered station room for fire styling")
        if "repeating-radial-gradient" in fire_style.get("backgroundImage", ""):
            raise AssertionError(f"Fire ring layer still rendered: {fire_style}")
        if "🔥" not in fire_style.get("content", ""):
            raise AssertionError(f"Fire flame sprite did not render: {fire_style}")

        def find(xpath):
            result = request("POST", base + "/element", {"using": "xpath", "value": xpath})
            return result["value"][ELEMENT_KEY]

        def click(xpath):
            element = wait_until(lambda: find(xpath), message=xpath)
            request("POST", f"{base}/element/{element}/click", {})

        click("//button[normalize-space()='MENU']")
        click("//section[contains(@class,'station-menu-popover')]//button[normalize-space()='RESET RUN']")
        wait_until(
            lambda: find("//section[contains(@class,'station-menu-popover')]//button[normalize-space()='CONFIRM RESET?']"),
            message="reset confirmation",
        )
        click("//section[contains(@class,'station-menu-popover')]//button[normalize-space()='CONFIRM RESET?']")

        # Regression: the old UI rendered nothing until ResetAsync returned.
        wait_until(
            lambda: find("//*[contains(@class,'station-processing-status') and contains(.,'PROCESSING') ]"),
            timeout=3,
            message="visible reset processing acknowledgement",
        )

        def status_gone():
            try:
                find("//*[contains(@class,'station-processing-status')]")
                return False
            except urllib.error.HTTPError as exc:
                if exc.code == 404:
                    return True
                raise

        wait_until(status_gone, timeout=15, message="reset completion")

        def menu_gone():
            try:
                find("//section[contains(@class,'station-menu-popover')]")
                return False
            except urllib.error.HTTPError as exc:
                if exc.code == 404:
                    return True
                raise

        wait_until(menu_gone, message="menu closed after reset")
        clock = find("//*[contains(@class,'station-clock') and contains(.,'T+00:00')]")
        if not clock:
            raise AssertionError("Reset did not return the mission clock to T+00:00")
        print("Local reset browser smoke passed.")
    finally:
        if session_id is not None:
            try:
                request("DELETE", f"{WEBDRIVER}/session/{session_id}")
            except Exception:
                pass
        if driver is not None:
            driver.terminate()
            driver.wait(timeout=5)
        app.terminate()
        try:
            app.wait(timeout=5)
        except subprocess.TimeoutExpired:
            app.kill()
        fake.shutdown()
        fake.server_close()


if __name__ == "__main__":
    main()
