"""Call the installed primary's public port throughout an install, as MCP clients do, and say whether any call failed.

Calls /health on a new connection every 20 ms and tools/list on a connection kept alive every 300 ms, each with the MCP
session the first answer gave, until the given seconds have passed. Prints how many calls were answered, refused or
failed otherwise, and which processes served the port, in order. Exits 1 when a call failed.

    python tests/headless/watch_public_port.py --seconds 120 &
    ./tools/Update-InstalledRoyderPrimary.ps1 -PackageDirectory <package>
"""
import argparse
import http.client
import json
import sys
import threading
import time
import urllib.request

HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def watch(port, seconds):
    url = f"http://127.0.0.1:{port}"
    session = HTTP.open(url + "/health", timeout=30).headers["Mcp-Session-Id"]
    finished = threading.Event()
    answered, refused, failures, servers = [0], [0], [], []
    lock = threading.Lock()

    def note(outcome):
        with lock:
            if outcome is None:
                answered[0] += 1
            elif isinstance(outcome, ConnectionRefusedError):
                refused[0] += 1
            else:
                failures.append(f"{time.strftime('%H:%M:%S')} {outcome!r}")

    def on_new_connections():
        while not finished.is_set():
            try:
                health = json.load(HTTP.open(urllib.request.Request(url + "/health", headers={"Mcp-Session-Id": session}), timeout=30))
                serving = (health.get("primary") or {}).get("processId", "a primary that names no process")
                with lock:
                    if not servers or servers[-1] != serving:
                        servers.append(serving)
                note(None)
            except Exception as error:
                note(error)
            time.sleep(.02)

    def on_a_connection_kept_alive():
        connection = http.client.HTTPConnection("127.0.0.1", port, timeout=30)
        body = json.dumps(dict(jsonrpc="2.0", id=1, method="tools/list", params={}))
        while not finished.is_set():
            try:
                connection.request("POST", "/", body, {"Content-Type": "application/json", "Mcp-Session-Id": session})
                response = connection.getresponse()
                response.read()
                if response.status != 200:
                    raise RuntimeError(f"tools/list answered HTTP {response.status}")
                note(None)
            except Exception as error:
                note(error)
                connection.close()
            time.sleep(.3)

    callers = [threading.Thread(target=it) for it in (on_new_connections, on_a_connection_kept_alive)]
    for caller in callers:
        caller.start()
    finished.wait(seconds)
    finished.set()
    for caller in callers:
        caller.join()
    print(f"{answered[0]} answered, {refused[0]} refused, {len(failures)} failed otherwise; served by {servers}", flush=True)
    for failure in failures[:10]:
        print("  " + failure, flush=True)
    return 1 if refused[0] or failures else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=23751)
    parser.add_argument("--seconds", type=float, default=120)
    arguments = parser.parse_args()
    sys.exit(watch(arguments.port, arguments.seconds))
