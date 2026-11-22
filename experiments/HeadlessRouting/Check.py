"""Run both real hosts against disposable Git worktrees. All MCP tool calls go through the primary."""
import json
import os
from pathlib import Path
import re
import tempfile
import time

import psutil

from hosts import GIB, Host, LOCAL_HTTP, MemoryBudget, PEER, PRIMARY, post, routing_turn
from worktrees import IMPORT, ROOT, SOURCE, git, private_worktrees


def require(condition, details):
    if not condition:
        raise RuntimeError(str(details))


def open_test_ports():
    return {c.laddr.port for c in psutil.net_connections(kind="tcp")
            if c.status == psutil.CONN_LISTEN and c.laddr.port in (PRIMARY, PEER)}


class Observations:
    def __init__(self, run, budget):
        self.path = run / "observations.jsonl"
        self.budget = budget
        self.origin = time.monotonic()

    def record(self, case, **data):
        with self.path.open("a", encoding="utf-8") as stream:
            print(json.dumps({"case": case, "atSeconds": time.monotonic() - self.origin, **data}), file=stream)

    def call(self, case, tool, **arguments):
        self.budget.check()
        start = time.monotonic()
        reply = post(PRIMARY, "tools/call", {"name": tool, "arguments": arguments})
        elapsed = time.monotonic() - start
        self.record(case, tool=tool, arguments=arguments, reply=reply, elapsed=elapsed)
        if "error" in reply:
            raise RuntimeError(f"JSON-RPC error: {reply}")
        answer = "\n".join(block["text"] for block in reply["result"]["content"])
        print(f"{case} ({elapsed:.3f}s): {answer[:180]}", flush=True)
        return answer

    def solutions(self, case):
        return json.loads(self.call(case, "list_solutions"))["solutions"]

    def symbols(self, case, path, **arguments):
        return self.call(case, "list_symbols_in_file", filePath=str(path), **arguments)


def snapshot(trees):
    return {path: digest for tree in trees for path, digest in tree.snapshot().items()}


def differences(before, after):
    return sorted(path for path in before.keys() | after.keys() if before.get(path) != after.get(path))


def require_no_project(answer, path):
    value, _ = json.JSONDecoder().raw_decode(answer)
    require(str(path) in value.get("error", "") and "no project" in value["error"], answer)


def require_declaration(answer, name):
    require(re.search(rf"(?m)^sealed class {re.escape(name)} :\d+\r?$", answer), answer)


def check_unhosted(observe, trees):
    outer, peer, nested = trees
    require([Path(s["path"]) for s in observe.solutions("only outer")] == [outer.solution], "Expected one host.")
    require_no_project(observe.symbols("unhosted sibling read", peer.source), peer.source)
    require_no_project(observe.symbols("unhosted nested read", nested.source), nested.source)
    before = snapshot(trees)
    answer = observe.call("unhosted nested fix", "fix_usings", filePath=str(nested.source))
    changed = differences(before, snapshot(trees))
    observe.record("disk after nested fix", changed=changed)
    require(not changed, f"Nested fix changed files: {changed}")
    require_no_project(answer, nested.source)


def check_pair(observe, trees):
    outer, peer, nested = trees
    entries = observe.solutions("both hosts")
    segments = {Path(s["path"]): s.get("uniquePathSegment") for s in entries}
    require(segments == {outer.solution: outer.root.name, peer.solution: peer.root.name}, segments)
    for tree in (outer, peer):
        reply = observe.symbols(f"absolute {tree.root.name}", tree.source)
        require_declaration(reply, tree.marker)
        named = observe.symbols(f"segment {tree.root.name}", SOURCE, solutionName=segments[tree.solution])
        require_declaration(named, tree.marker)
    outside = observe.path.parent / "outside.cs"
    outside.write_bytes(nested.files[nested.source])
    refusal = observe.symbols("file outside both roots", outside)
    require("Path is outside open solutions" in refusal, refusal)
    require_no_project(observe.symbols("unhosted nested read with sibling live", nested.source), nested.source)
    relative = observe.symbols("shared relative path", SOURCE)
    require("relative path exists in several open solutions" in relative, relative)
    unique = observe.symbols("peer-only relative path", peer.unique.relative_to(peer.root))
    require_declaration(unique, f"{peer.marker}Only")
    no_file = observe.call("no file or solution", "get_solution_structure")
    require("Multiple solutions are open" in no_file, no_file)
    same_name = observe.call("shared solution name", "get_solution_structure", solutionName="ReSharperMcp")
    require("Ambiguous solution name" in same_name, same_name)
    before = snapshot(trees)
    original = peer.source.read_bytes()
    try:
        answer = observe.call("routed peer fix", "fix_usings", filePath=str(peer.source))
        after = snapshot(trees)
        changed = differences(before, after)
        observe.record("disk after peer fix", changed=changed, before=original.decode(), after=peer.source.read_text("utf-8-sig"),
                       beforeSHA256=before[str(peer.source)], afterSHA256=after[str(peer.source)])
        require(changed == [str(peer.source)] and IMPORT in peer.source.read_text("utf-8-sig"), (changed, answer))
    finally:
        if peer.source.read_bytes() != original:
            peer.source.write_bytes(original)
    require(snapshot(trees) == before, "Unexpected changes after restoring the peer probe.")
    observe.record("peer probe restored", matchesBefore=True)


def check_peer_loss(observe, peer_host, peer):
    pid = peer_host.engine.pid
    killed_at = peer_host.kill_peer()
    observe.record("killed peer", pid=pid, returnedAfter=time.monotonic() - killed_at)
    for delay in (0, 65):
        while (remaining := killed_at + delay - time.monotonic()) > 0:
            observe.budget.check()
            time.sleep(min(0.5, remaining))
        entries = observe.solutions(f"list after {delay}s idle")
        observe.record("idle registrations", sinceKill=time.monotonic() - killed_at, paths=[s["path"] for s in entries])
    with LOCAL_HTTP.open(f"http://127.0.0.1:{PRIMARY}/health", timeout=10) as response:
        observe.record("health after peer death", health=json.load(response))
    for attempt in range(1, 4):
        observe.symbols(f"file call after death {attempt}", peer.source)
        entries = observe.solutions(f"list after forwarded call {attempt}")
        paths = [s["path"] for s in entries]
        observe.record("registrations after forwarded call", attempt=attempt, sinceKill=time.monotonic() - killed_at, paths=paths)
        if str(peer.solution) not in paths:
            break
    diagnostic = observe.budget.run_command(["dotnet", "run", "--file", str(Path(__file__).with_name("ConnectionRefusal.cs"))], ROOT)
    observe.record("transport on closed peer port", diagnostic=diagnostic.strip())


def record_host(observe, host):
    observe.record("host indexed", pid=host.engine.pid, solution=str(host.tree.solution), port=host.port,
                   privateState=str(host.state), availableGiBBeforeStart=host.available_at_start)


def main():
    run_root = Path(os.environ["TEMP"]) / "royder-headless/royder-codex-3"
    run_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="routing-", dir=run_root))
    print(f"Evidence: {run}", flush=True)
    with routing_turn(run_root):
        check(run)


def check(run):
    require(not open_test_ports(), "Routing ports are already in use.")
    require(psutil.virtual_memory().available >= 6 * GIB, "Fewer than 6 GiB available before the routing experiment.")
    with MemoryBudget(run) as budget, private_worktrees(run) as trees:
        outer, peer, nested = trees
        observe = Observations(run, budget)
        observe.record("setup", revision=git(ROOT, "rev-parse", "HEAD").decode().strip(),
                       worktrees=[str(tree.root) for tree in trees], availableGiB=psutil.virtual_memory().available/GIB)
        for tree in (outer, peer):
            budget.check()
            budget.run_command(["dotnet", "restore", str(tree.solution), "--verbosity", "quiet"], tree.root)
        with Host(outer, run, PRIMARY, budget) as primary_host:
            record_host(observe, primary_host)
            check_unhosted(observe, trees)
            with Host(peer, run, PEER, budget) as peer_host:
                record_host(observe, peer_host)
                check_pair(observe, trees)
                check_peer_loss(observe, peer_host, peer)
            with Host(peer, run, PEER, budget, suffix="-replacement") as replacement:
                record_host(observe, replacement)
                recovered = observe.symbols("replacement peer read", peer.source)
                require_declaration(recovered, peer.marker)
            entries = observe.solutions("list after graceful peer exit")
            require([Path(s["path"]) for s in entries] == [outer.solution], entries)
            require_no_project(observe.symbols("file after deregistration", peer.source), peer.source)
        observe.record("stopped", minimumAvailableGiB=budget.minimum/GIB)
    require(not open_test_ports(), "A routing port is still in use.")
    print(f"Completed; minimum available memory {budget.minimum/GIB:.2f} GiB. Evidence: {run}")


if __name__ == "__main__":
    main()
