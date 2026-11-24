"""Check that the primary answers a tool call by its deadline when Rider answers later, and passes on what Rider
answers in time. A stand-in for desktop Rider answers one tool slowly; no Rider runs."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import socket
import subprocess
import tempfile
import threading
import time
import urllib.request
from verify_on_demand import wait_until, HTTP

DEADLINE_SECONDS = 3
SLOW_SECONDS = 8


def free_port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def stand_in_for_desktop_rider(solution, finished):
    """Answers as desktop Rider with one open solution: `find_usages` after SLOW_SECONDS, other tools at once."""
    listed = dict(solutions=[dict(name=solution.stem, path=str(solution), toolCount=2)], solutionCount=1)

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def answer(self, body):
            data = json.dumps(body).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            self.answer(dict(solutions=[dict(path=str(solution), indexing=False)]))

        def do_POST(self):
            request = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            params = request.get("params") or {}
            if request["method"] == "tools/list":
                tools = [dict(name=name, description=name, inputSchema=dict(type="object", properties={}))
                         for name in ("find_usages", "list_symbols_in_file")]
                return self.answer(dict(jsonrpc="2.0", id=request["id"], result=dict(tools=tools)))
            text = json.dumps(listed) if params["name"] == "list_solutions" else "answered by Rider"
            if params["name"] == "find_usages":
                time.sleep(SLOW_SECONDS)
                finished.set()
            self.answer(dict(jsonrpc="2.0", id=request["id"], result=dict(content=[dict(type="text", text=text)], isError=False)))

    return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--primary", type=Path, required=True)
    parser.add_argument("--settings", type=Path, required=True, help="Valid settings with the pinned catalog")
    args = parser.parse_args()
    lab = Path(tempfile.mkdtemp(prefix="royder-answer-deadline-"))
    solution = lab / "checkout" / "StandIn.sln"
    (solution.parent / ".git").mkdir(parents=True)
    solution.write_text("")
    finished = threading.Event()
    desktop = ThreadingHTTPServer(("127.0.0.1", free_port()), stand_in_for_desktop_rider(solution, finished))
    threading.Thread(target=desktop.serve_forever, daemon=True).start()
    port = free_port()
    while 0 <= port - desktop.server_address[1] < 10:  # The settings keep each Rider's ten ports apart from the primary's.
        port = free_port()
    settings = json.loads(args.settings.read_text(encoding="utf-8-sig"))
    settings.update(port=port, desktopPort=desktop.server_address[1], answerWithinSeconds=DEADLINE_SECONDS)
    settings["rider"]["profileDirectory"] = str(lab / "profile")
    config = lab / "primary.json"
    config.write_text(json.dumps(settings))

    def call(name):
        body = json.dumps(dict(jsonrpc="2.0", id=7, method="tools/call",
                               params=dict(name=name, arguments=dict(solutionName=solution.stem)))).encode()
        request = urllib.request.Request(f"http://127.0.0.1:{port}/", body, headers={"Content-Type": "application/json"})
        started = time.monotonic()
        answer = json.load(HTTP.open(request, timeout=30))["result"]
        return answer, time.monotonic() - started

    with (lab / "primary.log").open("w") as log:
        primary = subprocess.Popen(["dotnet", str(args.primary.resolve()), "serve", str(config)], stdout=log, stderr=subprocess.STDOUT)
        try:
            wait_until(lambda: HTTP.open(f"http://127.0.0.1:{port}/health", timeout=3).status == 200, 25, "the primary")
            answer, seconds = call("find_usages")
            assert answer["isError"] and f"within {DEADLINE_SECONDS} seconds" in answer["content"][0]["text"], answer
            assert seconds < DEADLINE_SECONDS + 2 and not finished.is_set(), (seconds, finished.is_set())
            print(f"PASS: the slow call was answered by the deadline, after {seconds:.1f} s, while Rider went on", flush=True)
            answer, seconds = call("list_symbols_in_file")
            assert not answer["isError"] and answer["content"][0]["text"] == "answered by Rider", answer
            print(f"PASS: a call Rider answers in time passes its answer on, after {seconds:.1f} s", flush=True)
            assert finished.wait(SLOW_SECONDS + 2), "Rider never finished the slow call"
        finally:
            primary.terminate()
            primary.wait(30)
            desktop.shutdown()
    print("Private verification files:", lab)


if __name__ == "__main__":
    main()
