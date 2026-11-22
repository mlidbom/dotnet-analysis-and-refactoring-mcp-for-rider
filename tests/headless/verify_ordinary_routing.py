"""Compare a running ordinary Royder endpoint with the same request through the standalone primary."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import urllib.request
from verify_on_demand import wait_until, HTTP


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--primary", type=Path, required=True)
    parser.add_argument("--settings", type=Path, required=True, help="Valid settings with the pinned catalog")
    parser.add_argument("--desktop-port", type=int, default=23751)
    parser.add_argument("--port", type=int, default=24935)
    parser.add_argument("--solution", required=True)
    parser.add_argument("--symbol", required=True)
    args = parser.parse_args()
    lab = Path(tempfile.mkdtemp(prefix="royder-ordinary-routing-"))
    settings = json.loads(args.settings.read_text(encoding="utf-8-sig"))
    settings.update(port=args.port, desktopPort=args.desktop_port)
    settings["rider"]["profileDirectory"] = str(lab / "profile")
    config = lab / "primary.json"
    config.write_text(json.dumps(settings))

    def call(port, name, arguments):
        body = json.dumps(dict(jsonrpc="2.0", id=42, method="tools/call", params=dict(name=name, arguments=arguments))).encode()
        req = urllib.request.Request(f"http://127.0.0.1:{port}/", body, headers={"Content-Type":"application/json"})
        answer = json.load(HTTP.open(req, timeout=25))["result"]
        assert not answer["isError"], answer
        return json.loads(answer["content"][0]["text"])

    with (lab / "primary.log").open("w") as log:
        proc = subprocess.Popen(["dotnet", str(args.primary.resolve()), "serve", str(config)], stdout=log, stderr=subprocess.STDOUT)
        try:
            wait_until(lambda: HTTP.open(f"http://127.0.0.1:{args.port}/health", timeout=3).status == 200, 25, "the primary")
            assert call(args.desktop_port, "list_solutions", {}) == call(args.port, "list_solutions", {})
            query = dict(solutionName=args.solution, symbolName=args.symbol, format="json")
            direct = call(args.desktop_port, "go_to_definition", query)
            assert direct["declarations"] and direct == call(args.port, "go_to_definition", query)
            assert not (lab / "profile" / "current.json").exists()
            print("PASS: ordinary solution catalog and JSON declarations match, with no private Rider launch")
        finally:
            proc.terminate()
            proc.wait(timeout=20)


if __name__ == "__main__":
    main()
