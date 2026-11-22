"""Own the two launcher process trees and enforce the physical-memory budget during blocking tool calls."""
import json
from contextlib import contextmanager
import msvcrt
import os
from pathlib import Path
import queue
import re
import subprocess
import threading
import time
from urllib.request import ProxyHandler, Request, build_opener

import psutil

from worktrees import ROOT


PRIMARY, PEER = 23920, 23921
GIB = 1024 ** 3
LOCAL_HTTP = build_opener(ProxyHandler({}))


def post(port, method, params):
    if port not in (PRIMARY, PEER):
        raise ValueError(f"Not a routing experiment port: {port}")
    body = dict(jsonrpc="2.0", id=1, method=method, params=params)
    request = Request(f"http://127.0.0.1:{port}/", data=json.dumps(body).encode(),
                      headers={"Content-Type": "application/json"})
    with LOCAL_HTTP.open(request, timeout=145) as response:
        return json.load(response)


@contextmanager
def routing_turn(root):
    """Two copies of this experiment share one pair of ports."""
    with (root / "routing-ports.lock").open("a+b") as guard:
        guard.seek(0)
        try:
            msvcrt.locking(guard.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError as error:
            raise RuntimeError("Another routing experiment holds ports 23920 and 23921.") from error
        try:
            yield
        finally:
            guard.seek(0)
            msvcrt.locking(guard.fileno(), msvcrt.LK_UNLCK, 1)


def stop_processes(processes):
    for process in processes:
        try:
            if process.is_running():
                process.kill()
        except psutil.NoSuchProcess:
            pass


def descendants(process):
    try:
        return process.children(recursive=True)
    except psutil.NoSuchProcess:
        return []


def finish_children(children):
    _, remaining = psutil.wait_procs(children, timeout=3)
    if remaining:
        stop_processes(remaining)
        _, remaining = psutil.wait_procs(remaining, timeout=5)
        if remaining:
            raise RuntimeError(f"Owned child processes still running: {[p.pid for p in remaining]}")


class MemoryBudget:
    def __init__(self, run):
        self.path = run / "memory.jsonl"
        self.minimum = psutil.virtual_memory().available
        self.low = None
        self.processes = []
        self.lock = threading.Lock()
        self.done = threading.Event()

    def __enter__(self):
        self.watcher = threading.Thread(target=self.watch)
        self.watcher.start()
        return self

    def start(self, command, *, starting_host=False, **kwargs):
        with self.lock:
            self.check()
            available = psutil.virtual_memory().available
            if available < 2 * GIB:
                self.low = available
                self.check()
            if starting_host and available < 6 * GIB:
                raise RuntimeError(f"Host start requires 6 GiB available; measured {available / GIB:.2f} GiB.")
            process = psutil.Popen(command, creationflags=subprocess.CREATE_NO_WINDOW, **kwargs)
            self.processes.append(process)
            return process, available / GIB

    def run_command(self, command, cwd):
        env = dict(os.environ, MSBUILDDISABLENODEREUSE="1", UseSharedCompilation="false")
        process, _ = self.start(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding="utf-8", errors="replace")
        try:
            output, _ = process.communicate()
        finally:
            if process.poll() is None:
                stop_processes([*descendants(process), process])
                process.wait(timeout=10)
        self.check()
        if process.returncode:
            raise RuntimeError(f"{command} exited with {process.returncode}: {output}")
        return output

    def check(self):
        if self.low is not None:
            raise RuntimeError(f"Stopped hosts: available memory fell to {self.low / GIB:.2f} GiB (limit 2 GiB).")

    def watch(self):
        with self.path.open("w", encoding="utf-8") as log:
            while not self.done.is_set():
                available = psutil.virtual_memory().available
                self.minimum = min(self.minimum, available)
                print(json.dumps({"at": time.time(), "availableBytes": available}), file=log, flush=True)
                if available < 2 * GIB:
                    with self.lock:
                        self.low = available
                        for process in self.processes:
                            if process.is_running():
                                stop_processes([*descendants(process), process])
                    return
                self.done.wait(0.5)

    def __exit__(self, *_):
        self.done.set()
        self.watcher.join()
        self.check()


class Host:
    def __init__(self, tree, run, port, budget, suffix=""):
        self.tree = tree
        self.state = run / f"state-{port}{suffix}"
        self.log = run / f"host-{port}{suffix}.log"
        self.port = port
        self.budget = budget
        self.lines = queue.Queue()
        self.engine = None
        self.killed = False

    def __enter__(self):
        env = dict(os.environ, MSBUILDDISABLENODEREUSE="1", UseSharedCompilation="false",
                   RESHARPER_HOST_CONFIG_DIR=str(self.state / "roaming"))
        self.launcher, self.available_at_start = self.budget.start(
            ["pwsh", "-NoProfile", "-File", str(ROOT / "experiments/HeadlessHost/Run.ps1"),
             "-Royder", "-Listen", "-Symbol", "-", "-Solution", str(self.tree.solution),
             "-Cache", str(self.state), "-ExpectedRoyderPort", str(self.port)],
            starting_host=True, cwd=ROOT, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            text=True, encoding="utf-8", errors="replace")
        self.reader = threading.Thread(target=self.read_console, daemon=True)
        self.reader.start()
        try:
            deadline = time.monotonic() + 300
            while True:
                self.budget.check()
                line = self.lines.get(timeout=max(0, deadline - time.monotonic()))
                if line is None:
                    raise RuntimeError(f"Host exited before READY. See {self.log}")
                if match := re.match(r"INDEXED pid=(\d+) ", line):
                    candidate = psutil.Process(int(match[1]))
                    if candidate.ppid() != self.launcher.pid or str(self.tree.solution) not in candidate.cmdline():
                        raise RuntimeError("The indexed PID does not belong to this launcher and solution.")
                    self.engine = candidate
                if line == "READY":
                    break
            status = post(self.port, "internal/status", {})["result"]
            if (self.engine is None or status["port"] != self.port
                    or status["role"] != ("primary" if self.port == PRIMARY else "peer")
                    or [Path(s["path"]) for s in status["solutions"]] != [self.tree.solution]):
                raise RuntimeError(f"Unexpected host identity on {self.port}: {status}")
            return self
        except BaseException:
            self.close()
            raise

    def read_console(self):
        with self.log.open("w", encoding="utf-8") as log:
            for line in self.launcher.stdout:
                log.write(line)
                log.flush()
                self.lines.put(line.rstrip())
        self.lines.put(None)

    def kill_peer(self):
        if self.port != PEER or not self.engine or not self.engine.is_running():
            raise RuntimeError("The owned peer engine is not running.")
        children = descendants(self.engine)
        started = time.monotonic()
        self.engine.kill()
        self.engine.wait(timeout=10)
        self.killed = True
        self.launcher.wait(timeout=15)
        finish_children(children)
        return started

    def close(self):
        if self.launcher.poll() is None:
            children = descendants(self.launcher)
            try:
                self.launcher.stdin.close()  # EOF ends SolutionSession.Listen.
                self.launcher.wait(timeout=30)
            except BrokenPipeError:
                stop_processes([*children, self.launcher])
                self.launcher.wait(timeout=10)
            except (psutil.TimeoutExpired, subprocess.TimeoutExpired):
                stop_processes([*children, self.launcher])
                self.launcher.wait(timeout=10)
            finish_children(children)
        self.reader.join(timeout=5)

    def __exit__(self, exception_type, *_):
        self.close()
        if exception_type is None and self.launcher.returncode and not self.killed:
            raise RuntimeError(f"Host exit {self.launcher.returncode}. See {self.log}")
