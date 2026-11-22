"""Run one isolated engine and record its HTTP and console observations."""

import hashlib
import json
import queue
import subprocess
import threading
import time
import urllib.request
from pathlib import Path


HERE = Path(__file__).resolve().parents[1]
ROOT = HERE.parents[1]
SOLUTION = ROOT / "ReSharperMcp.sln"
CACHE = HERE / "obj" / "live"
HEADLESS_PORT = 23910
RIDER_PORT = 23751
_http = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def request(port, path, data=None):
    body = None if data is None else json.dumps(data).encode()
    url = f"http://127.0.0.1:{port}/{path}"
    headers = {"Content-Type": "application/json"}
    with _http.open(urllib.request.Request(url, body, headers), timeout=145) as response:
        return json.load(response)


class LiveEngine:
    def __init__(self, evidence, solution=SOLUTION, probe_missing_import=False, request_zones=None):
        self.evidence = evidence
        self.solution = solution
        self.probe_missing_import = probe_missing_import
        self.request_zones = request_zones
        self.cache = CACHE / ("zones-" + request_zones) if request_zones else CACHE
        self.lines = queue.Queue()
        self.observations = []

    def __enter__(self):
        command = ["pwsh", "-NoProfile", "-File", str(HERE / "Run.ps1"), "-Cache", str(self.cache),
                   "-Royder", "-Listen", "-Symbol", "-", "-Solution", str(self.solution)]
        if self.probe_missing_import:
            command.append("-ProbeMissingImport")
        if self.request_zones:
            command.extend(["-RequestZones", self.request_zones])
        self.process = subprocess.Popen(command, cwd=ROOT, stdin=subprocess.PIPE,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
        self.reader = threading.Thread(target=self._record_console, daemon=True)
        self.reader.start()
        try:
            startup = self._until_ready(240)
            if self.request_zones:
                for option, zone in (("editing", "ICodeEditingZone"), ("external-sources", "ExternalSourcesZone")):
                    requested = self.request_zones in ("both", option)
                    if f"Royder zone request: {zone}={requested}" not in startup:
                        raise RuntimeError(f"The platform did not evaluate the requested activator for {zone}.")
            health = request(HEADLESS_PORT, "health")
            self.observe("health", health)
            registered, = health["solutions"]
            assert Path(registered["path"]) == self.solution, registered
            artifacts = HERE / "obj/package-build/bin"
            def built_hash(name):
                return hashlib.sha256((artifacts / name / "debug" / (name + ".dll")).read_bytes()).hexdigest()
            host_hash = built_hash("DotNetAnalysisAndRefactoringMcp")
            tools_hash = built_hash("DotNetAnalysisAndRefactoringMcp.Tools")
            selected = registered["tools"]
            assert (selected["host"], selected["build"]) == (host_hash, "live"), selected
            folder = self.cache / "tools" / host_hash / tools_hash
            assert Path(selected["folder"]) == folder, selected
            loaded_dll = folder / selected["build"] / "DotNetAnalysisAndRefactoringMcp.Tools.dll"
            assert hashlib.sha256(loaded_dll.read_bytes()).hexdigest() == tools_hash, selected
            self.require_solution(HEADLESS_PORT)
        except BaseException:
            self.__exit__(None, None, None)
            raise
        return self

    def _record_console(self):
        with (self.evidence / "engine.log").open("w", encoding="utf-8") as log:
            for line in self.process.stdout:
                log.write(line)
                log.flush()
                self.lines.put(line.rstrip())
        self.lines.put(None)

    def _until_ready(self, timeout):
        deadline = time.monotonic() + timeout
        output = []
        while True:
            line = self.lines.get(timeout=max(0, deadline - time.monotonic()))
            if line == "READY":
                return output
            if line is None:
                raise RuntimeError(f"Engine exited; see {self.evidence / 'engine.log'}: {output[-4:]}")
            output.append(line)

    def console(self, command):
        self.process.stdin.write(command + "\n")
        self.process.stdin.flush()
        return self.observe(command, self._until_ready(30))

    def document(self, path):
        output = self.console("document " + str(path))
        return json.loads(next(line[9:] for line in output if line.startswith("DOCUMENT ")))

    def require_solution(self, port, expected=None):
        answer = self.call("list_solutions", port=port)
        solutions = json.loads(answer)["solutions"]
        assert any(Path(item["path"]) == (expected or self.solution) for item in solutions), solutions
        return solutions

    def call(self, tool, port=HEADLESS_PORT, **arguments):
        rider_reads = {"list_solutions", "list_symbols_in_file", "search_symbol", "get_file_errors"}
        if port != HEADLESS_PORT and (port != RIDER_PORT or tool not in rider_reads):
            raise ValueError("The everyday Royder permits only read calls for this check.")
        if tool != "list_solutions":
            arguments["solutionName"] = str(self.solution if port == HEADLESS_PORT else SOLUTION)
        started = time.monotonic()
        response = request(port, "", {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                                      "params": {"name": tool, "arguments": arguments}})
        elapsed = time.monotonic() - started
        if "error" in response:
            self.observe("JSON-RPC error", response["error"], tool=tool, port=port, seconds=round(elapsed, 3))
            raise RuntimeError(f"{tool} failed at JSON-RPC dispatch: {response['error']}")
        answer = "\n".join(it["text"] for it in response["result"]["content"] if it["type"] == "text")
        self.observe(tool, answer, port=port, seconds=round(elapsed, 3), arguments=arguments)
        return answer

    def observe(self, action, value, **details):
        record = {"action": action, **details, "value": value}
        self.observations.append(record)
        with (self.evidence / "observations.jsonl").open("a", encoding="utf-8") as stream:
            print(json.dumps(record), file=stream)
        print(action, details.get("seconds", ""), str(value)[:380], flush=True)
        return value

    def __exit__(self, *_):
        try:
            if self.process.poll() is None:
                try:
                    self.process.stdin.write("exit\n")
                    self.process.stdin.close()
                except BrokenPipeError:
                    pass  # The exit code below reports a host that already closed its input.
                self.process.wait(timeout=30)
        finally:
            if self.process.poll() is None:
                subprocess.run(["taskkill", "/pid", str(self.process.pid), "/t", "/f"], check=True)
                self.process.wait(timeout=10)
            self.reader.join(timeout=5)
        if self.process.returncode:
            raise RuntimeError(f"Host exit {self.process.returncode}: {self.evidence / 'engine.log'}")
