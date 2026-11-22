"""Check MCP discovery with no IDE, and that the next request notices an ordinary Rider listener appearing or disappearing."""
import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import statistics
import subprocess
import tempfile
import threading
import time
import urllib.request

from verify_on_demand import HTTP, wait_until


def verify(primary, source_settings, port, desktop_port):
    lab = Path(tempfile.mkdtemp(prefix="royder-discovery-"))
    settings = json.loads(source_settings.read_text(encoding="utf-8-sig"))
    settings.update(port=port, desktopPort=desktop_port, repositories=[])
    settings["rider"]["profileDirectory"] = str(lab / "profile")
    config = lab / "settings.json"
    config.write_text(json.dumps(settings))
    solution = str(lab / "Ordinary.sln")
    public = f"http://127.0.0.1:{port}/"

    def rpc(method, params=None):
        payload = json.dumps(dict(jsonrpc="2.0", id=1, method=method, params=params or {})).encode()
        req = urllib.request.Request(public, payload, {"Content-Type": "application/json"})
        with HTTP.open(req, timeout=10) as response:
            return json.load(response)["result"]

    def listing():
        return json.loads(rpc("tools/call", dict(name="list_solutions"))["content"][0]["text"])

    def health():
        with HTTP.open(public + "health", timeout=10) as response:
            return json.load(response)

    def fast_with_desktop_closed():
        for name, query in [("health", health), ("tools/list", lambda: rpc("tools/list")), ("list_solutions", listing)]:
            times = []
            for _ in range(3):
                start = time.monotonic()
                query()
                times.append((time.monotonic() - start) * 1000)
            print(name, "ms:", [round(t, 1) for t in times], flush=True)
            assert statistics.median(times) < 500, (name, times)

    class OrdinaryEndpoint(BaseHTTPRequestHandler):
        def do_POST(self):
            incoming = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            assert incoming["params"]["name"] == "list_solutions"
            listed = {"solutionCount": 1, "solutions": [{"name": "Ordinary", "path": solution, "toolCount": 25}]}
            body = json.dumps({"jsonrpc": "2.0", "id": incoming["id"], "result": {
                "content": [{"type": "text", "text": json.dumps(listed)}], "isError": False}}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *unused):
            pass

    with (lab / "primary.log").open("w") as log:
        proc = subprocess.Popen(["dotnet", str(primary.resolve()), "serve", str(config)], stdout=log, stderr=subprocess.STDOUT)
        try:
            wait_until(health, 20, "isolated primary")
            fast_with_desktop_closed()
            guide = rpc("initialize")["instructions"]
            cold = listing()
            assert cold["solutions"] == [] and cold["solutionCount"] == 0
            assert "absolute filePath" in cold["hint"] and "solutionName" in cold["hint"]
            assert "absolute filePath" in guide and len(guide) < 250, guide
            descriptor = next(it for it in rpc("tools/list")["tools"] if it["name"] == "list_solutions")
            assert "on demand" in descriptor["description"]
            with ThreadingHTTPServer(("127.0.0.1", desktop_port), OrdinaryEndpoint) as ordinary:
                threading.Thread(target=ordinary.serve_forever, daemon=True).start()
                try:
                    warm = listing()
                    assert [row["path"] for row in warm["solutions"]] == [solution] and warm["hint"] == cold["hint"]
                finally:
                    ordinary.shutdown()
            assert listing()["solutions"] == []
            fast_with_desktop_closed()
            assert not (lab / "profile" / "current.json").exists()
        finally:
            proc.terminate()
            proc.wait(timeout=20)
    print("PASS: cold discovery, immediate desktop detection, and return to headless operation")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--primary", type=Path, required=True)
    parser.add_argument("--settings", type=Path, required=True)
    parser.add_argument("--port", type=int, default=25210)
    parser.add_argument("--desktop-port", type=int, default=25220)
    args = parser.parse_args()
    verify(args.primary, args.settings, args.port, args.desktop_port)
