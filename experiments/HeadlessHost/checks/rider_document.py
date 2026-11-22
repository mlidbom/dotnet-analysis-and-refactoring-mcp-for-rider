"""Read the checkout's live Rider frontend document through its native MCP."""

import json
import re
import time
import urllib.request

from .engine import ROOT


class RiderDocuments:
    def __init__(self):
        self.url = json.loads((ROOT / ".mcp.json").read_text())["mcpServers"]["rider"]["url"]
        self.headers = {"Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def __enter__(self):
        reply = self._request({"id": 1, "method": "initialize", "params": {
            "protocolVersion": "2025-03-26", "capabilities": {},
            "clientInfo": {"name": "HeadlessHost document comparison", "version": "1"}}})
        self.headers["MCP-Protocol-Version"] = reply["result"]["protocolVersion"]
        self._request({"method": "notifications/initialized"})
        return self

    def _request(self, body):
        request = urllib.request.Request(self.url, json.dumps({"jsonrpc": "2.0", **body}).encode(), self.headers)
        with self.http.open(request, timeout=20) as reply:
            if session := reply.headers.get("Mcp-Session-Id"):
                self.headers["Mcp-Session-Id"] = session
            if reply.status != 202:
                return json.load(reply)

    def read(self, path):
        if not path.is_relative_to(ROOT):
            raise ValueError(f"Expected a document in this checkout: {path}")
        reply = self._request({"id": 2, "method": "tools/call", "params": {"name": "read_file", "arguments": {
            "file_path": str(path), "rootFolder": str(ROOT), "limit": 200}}})
        result = reply["result"]
        text = result["content"][0]["text"]
        if result.get("isError"):
            raise RuntimeError(text)
        numbered = text.split("\n")
        if not all(line.startswith(f"L{number}: ") for number, line in enumerate(numbered, 1)):
            raise ValueError(f"Unexpected read_file response: {text}")
        return "\n".join(re.sub(r"^L\d+: ", "", line) for line in numbered)

    def compare(self, path, expected, engine):
        start = time.monotonic()
        while True:
            actual = self.read(path)
            elapsed = time.monotonic() - start
            matches = actual == expected
            engine.observe("Rider frontend document", {"matchesDisk": matches, "text": actual},
                           path=str(path), seconds=round(elapsed, 3))
            if matches or elapsed >= 30:
                return matches
            time.sleep(.25)

    def __exit__(self, *_):
        if "Mcp-Session-Id" in self.headers:
            with self.http.open(urllib.request.Request(self.url, headers=self.headers, method="DELETE"), timeout=10):
                pass
