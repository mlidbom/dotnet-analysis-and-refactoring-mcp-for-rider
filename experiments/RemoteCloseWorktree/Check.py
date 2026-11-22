"""Black-box contract checks against an already running private two-project-capable server on Windows."""
import argparse
import concurrent.futures
from http.client import HTTPConnection
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time


OPEN = "/api/royder-open-worktree"
CLOSE = "/api/royder-close-worktree"
DISCARD = CLOSE + "/discard"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


class Host:
    def __init__(self, args):
        self.port = args.port
        self.royder = args.royder_port
        self.token = os.environ["ROYDER_MANAGED_HOST_TOKEN"]
        if not self.token.strip():
            raise ValueError("A private host token is required")
        for port in (self.port, self.royder):
            if not 0 < port < 65536 or port in {*range(23751, 23761), 23841, 23842}:
                raise ValueError("Supply the private server's ports")
        owners = subprocess.check_output(
            ["pwsh", "-NoProfile", "-Command",
             f"(Get-NetTCPConnection -State Listen -LocalPort {self.port} -ErrorAction Stop).OwningProcess"],
            text=True).split()
        if set(owners) != {str(args.jvm_pid)}:
            raise ValueError("The supplied JVM PID does not own the built-in HTTP listener")

    def post(self, operation, solution, *, status=200, match=None, authorized=True):
        headers = {"Content-Type": "text/plain; charset=utf-8"}
        if authorized:
            headers["Authorization"] = "Bearer " + self.token
        if match is not None:
            headers["If-Match"] = match
        connection = HTTPConnection("127.0.0.1", self.port, timeout=120)
        try:
            connection.request("POST", operation, str(solution).encode("utf-8"), headers)
            response = connection.getresponse()
            detail = response.read().decode("utf-8", errors="replace")
            if response.status != status:
                raise AssertionError(f"{operation} {solution}: expected {status}, got {response.status}: {detail}")
            tag = response.getheader("ETag")
            if operation == OPEN and status == 200 and not re.fullmatch(r'"[0-9a-f-]{36}"', tag or ""):
                raise AssertionError(f"Missing or invalid strong ETag: {tag!r}")
            return tag
        finally:
            connection.close()

    def wait_for_solution(self, solution, present):
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            connection = HTTPConnection("127.0.0.1", self.royder, timeout=5)
            try:
                connection.request("GET", "/health")
                response = connection.getresponse()
                if response.status != 200:
                    raise RuntimeError(f"Private Royder /health returned {response.status}")
                solutions = json.loads(response.read())["solutions"]
            finally:
                connection.close()
            matches = [item for item in solutions if Path(item["path"]) == solution]
            if (not present and not matches) or (present and len(matches) == 1 and not matches[0]["indexing"]):
                return
            if len(matches) > 1:
                raise AssertionError(f"Duplicate solution {solution}")
            time.sleep(0.25)
        raise AssertionError(f"Timeout waiting for {solution} present={present}")


class Fixture:
    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="royder-managed-worktree-spec-")).resolve()
        self.seed = self.root / "seed"
        self.seed.mkdir()
        self.tree = self.root / "agent"
        self.solution = self.tree / "Probe.slnx"
        self.git("init", "--quiet")
        (self.seed / ".gitignore").write_text(".idea/\nbin/\nobj/\n*.DotSettings.user\n", encoding="utf-8")
        (self.seed / "NuGet.Config").write_text("<configuration><packageSources><clear/></packageSources></configuration>")
        (self.seed / "Probe.slnx").write_text('<Solution><Project Path="Probe.csproj" /></Solution>')
        (self.seed / "Probe.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        (self.seed / "Shape.cs").write_text("public sealed class Shape;")
        self.git("add", "--all")
        self.git("-c", "user.name=Royder specifications", "-c", "user.email=fixture@localhost",
                 "commit", "--quiet", "-m", "Private worktree fixture")
        print(f"Private fixture and evidence: {self.root}", flush=True)

    def git(self, *args):
        subprocess.run(["git", "-C", str(self.seed), *args], check=True)

    def add(self):
        if self.tree.exists():
            raise AssertionError(f"Git's former worktree path already exists: {self.tree}")
        self.git("worktree", "add", "--quiet", "--detach", str(self.tree), "HEAD")

    def remove(self):
        self.git("worktree", "remove", str(self.tree))
        if self.tree.exists():
            raise AssertionError(f"Git returned success but left {self.tree}")

    def project_settings(self):
        return {p.relative_to(self.tree): (p.stat().st_mtime_ns, p.read_bytes())
                for p in (self.tree / ".idea").rglob("*") if p.is_file()}


def check(host, anchor):
    host.wait_for_solution(anchor, True)
    anchor_tag = host.post(OPEN, anchor)
    fixture = Fixture()
    solution = fixture.solution
    host.post(OPEN, solution, status=403, authorized=False)
    host.post(OPEN, solution, status=404)
    host.post(OPEN, "relative.slnx", status=400)

    fixture.add()
    current = None
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            requests = [executor.submit(host.post, OPEN, solution) for _ in range(2)]
            current, second = (request.result() for request in requests)
        require(current == second, "Concurrent opens returned different registrations")
        host.wait_for_solution(solution, True)
        host.post(DISCARD, solution, status=428)
        host.post(DISCARD, solution, status=412, match='"another-instance"')
        host.post(CLOSE, solution, match=current)
        previous, current = current, None
        host.wait_for_solution(solution, False)
        current = host.post(OPEN, solution)
        require(previous != current, "Reopen reused the old registration")
        host.wait_for_solution(solution, True)
        print("Passed concurrent opens, admission, stale preconditions and saving close", flush=True)

        fixture.remove()
        host.post(CLOSE, solution, status=409, match=current)
        host.post(DISCARD, solution, match=current)
        current = None
        host.wait_for_solution(solution, False)
        time.sleep(3)
        require(not fixture.tree.exists(), "Discard recreated the missing path")
        print("Passed discard of a missing worktree", flush=True)

        fixture.add()
        current = host.post(OPEN, solution)
        host.wait_for_solution(solution, True)
        fixture.remove()
        fixture.add()
        settings = fixture.tree / ".idea" / ".idea.Probe" / ".idea"
        settings.mkdir(parents=True)
        (settings / "workspace.xml").write_text('<project version="4"><component name="ReplacementSentinel"/></project>')
        before = fixture.project_settings()
        require(host.post(OPEN, solution, status=409) == current, "Replacement conflict did not identify the old instance")
        host.post(CLOSE, solution, status=409, match=current)
        host.post(DISCARD, solution, match=current)
        previous, current = current, None
        host.wait_for_solution(solution, False)
        time.sleep(3)
        require(fixture.project_settings() == before, "The old project changed the replacement's .idea")
        current = host.post(OPEN, solution)
        require(current != previous, "Replacement received the vanished instance's ETag")
        host.wait_for_solution(solution, True)
        host.post(DISCARD, solution, status=412, match=previous)
        host.post(CLOSE, solution, match=current)
        current = None
        host.wait_for_solution(solution, False)
        fixture.remove()
        host.wait_for_solution(anchor, True)
        require(host.post(OPEN, anchor) == anchor_tag, "The sibling's original project was closed")
        print("Passed replacement discard, stale retry and sibling survival", flush=True)
    finally:
        if current is not None:
            host.post(DISCARD, solution, match=current)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", required=True, type=int, help="Private JVM built-in HTTP port")
    parser.add_argument("--jvm-pid", required=True, type=int)
    parser.add_argument("--royder-port", required=True, type=int, help="Private Royder primary for /health")
    parser.add_argument("--anchor", required=True, type=Path, help="Already open private sibling solution")
    args = parser.parse_args()
    check(Host(args), args.anchor.resolve(strict=True))
