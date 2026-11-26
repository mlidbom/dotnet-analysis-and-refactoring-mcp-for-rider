#!/usr/bin/env python3
"""Reproduce the deletion flake: a file deleted on disk, and the first call after it still finding what it declared.

Each round deletes a file of the fixture solution, makes one call at once, checks its answer as the specifications in
Serving/When_a_file_declaring_a_type_is_deleted_on_disk.cs and When_a_linked_compile_item_is_deleted_on_disk.cs do,
and puts the file back. Before each round, the calls those specifications start with must see the file again; a
round whose file Rider hasn't taken back in yet is counted too.

The flake was seen with the machine's CPU at 60 to 100 % (dev_docs/backlog.md, the entry on
When_a_file_declaring_a_type_is_deleted_on_disk). With --cores, the sandbox Rider's processes are held to that many
cores, and --load busy processes run on the same ones: contention like that, without loading the rest of the machine.

    dotnet run --project tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools -- sandbox start <fixture copy's .slnx>
    python tests/live_deletion_under_load.py --solution <fixture copy's .slnx> --cores 2 --load 4

The sandbox's processes get every core back at the end, and the busy processes end.
"""
import argparse
import json
import multiprocessing
import pathlib
import sys
import time
import urllib.request

import psutil

SANDBOX_FOLDER = 'DotNetAnalysisAndRefactoringMcpSandbox'
TYPE_NAME = 'DrawingStencil'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--url', default='http://127.0.0.1:23841/', help='The sandbox Rider by default')
    parser.add_argument('--solution', required=True, help="Absolute path of the open fixture copy's .slnx")
    parser.add_argument('--rounds', type=int, default=40)
    parser.add_argument('--cores', type=int, default=0, help="How many cores the sandbox's processes get; 0: all")
    parser.add_argument('--load', type=int, default=0, help='Busy processes on those cores')
    parser.add_argument('--checks', default='search,usages,errors,linked', help='Which checks the rounds take turns at')
    parser.add_argument('--wait-for-the-watcher', action='store_true',
                        help="Make the first call only once the sandbox's file watcher has reported the deletion")
    parser.add_argument('--keep-going', action='store_true', help="Don't stop at the first failed round")
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')

    fixture = Fixture(args, pathlib.Path(args.solution).parent)
    checks = [CHECKS[name] for name in args.checks.split(',')]
    with Contention(args.cores, args.load):
        failures = 0
        late_restores = 0
        for round_number in range(1, args.rounds + 1):
            check = checks[(round_number - 1) % len(checks)]
            outcome = check(fixture)
            print(f'round {round_number} {check.__name__}: {outcome}', flush=True)
            late_restores += 0 if outcome.taken_back.at_once else 1
            if not outcome.passed:
                failures += 1
                if not args.keep_going:
                    break
    print(f'{failures} failed rounds of {round_number}; in {late_restores}, the file put back was taken in only later')
    raise SystemExit(1 if failures else 0)


class Fixture:
    def __init__(self, args, folder):
        self.args = args
        self.declaration = folder / 'Shapes' / 'DrawingStencil.cs'
        self.consumer = folder / 'Shapes.Drawing' / 'StencilLibrary.cs'
        self.linked = folder / 'Shared' / 'StencilPattern.cs'

    def call(self, name, **arguments):
        return call(self.args, name, **arguments)

    def consumer_errors(self):
        return self.call('get_file_errors', filePath=str(self.consumer))

    def usages(self):
        return self.call('find_usages', symbolName=TYPE_NAME, kind='type')

    def symbols_of_the_linked_file(self):
        return self.call('list_symbols_in_file', filePath=str(self.linked))

    def symbols_of_the_declaration(self):
        return self.call('list_symbols_in_file', filePath=str(self.declaration))


class Outcome:
    def __init__(self, passed, answer, taken_back, watcher_seconds):
        self.passed = passed
        self.answer = answer
        self.taken_back = taken_back
        self.watcher_seconds = watcher_seconds

    def __str__(self):
        taken_back = '' if self.taken_back.at_once else f'file taken back in only {self.taken_back}, '
        watcher = '' if self.watcher_seconds is None else f'watcher {self.watcher_seconds * 1000:.0f} ms, '
        verdict = 'ok' if self.passed else f'FAILED\n--- the answer:\n{self.answer.text}\n---'
        return f'{taken_back}{watcher}first call {self.answer.seconds:.2f}s, {verdict}'


def after_deleting(fixture, file, precondition, first_call, passes):
    """
    Waits for the precondition, deletes the file, makes the first call, and puts the file back. With
    --wait-for-the-watcher, the first call waits until the sandbox's file watcher has reported the deletion.
    """
    taken_back = Precondition.waited_for(precondition)
    original = file.read_bytes()
    watcher_seconds = None
    try:
        pending_before = pending_disk_changes(fixture.args)
        file.unlink()
        if fixture.args.wait_for_the_watcher:
            watcher_seconds = seconds_until(lambda: pending_disk_changes(fixture.args) > pending_before)
        answer = first_call()
    finally:
        file.write_bytes(original)
    return Outcome(passes(answer.text), answer, taken_back, watcher_seconds)


class Precondition:
    """How a round's precondition came to hold: at once, or after how many failed checks and seconds."""

    def __init__(self, failed_checks, seconds):
        self.failed_checks = failed_checks
        self.seconds = seconds

    @property
    def at_once(self):
        return self.failed_checks == 0

    def __str__(self):
        return f'after {self.failed_checks} failed checks, {self.seconds:.1f}s'

    @staticmethod
    def waited_for(holds, timeout=60):
        started = time.monotonic()
        failed_checks = 0
        while not holds():
            failed_checks += 1
            if time.monotonic() - started > timeout:
                raise SystemExit(f'The precondition still failed after {timeout} s')
            time.sleep(0.2)
        return Precondition(failed_checks, time.monotonic() - started)


def seconds_until(condition, timeout=30):
    started = time.monotonic()
    while not condition():
        if time.monotonic() - started > timeout:
            raise SystemExit(f'Still not so after {timeout} s')
    return time.monotonic() - started


def pending_disk_changes(args):
    """What /health counts: the changes the file watcher reported that no call has taken yet, and stale files."""
    with urllib.request.urlopen(args.url.rstrip('/') + '/health', timeout=10) as response:
        return json.load(response)['pendingDiskChanges']


def the_declaration_is_back(fixture):
    return lambda: ' — 0 diagnostics' in fixture.consumer_errors().text and 'StencilLibrary.cs' in fixture.usages().text


def search(fixture):
    return after_deleting(fixture, fixture.declaration, the_declaration_is_back(fixture),
                          lambda: fixture.call('search_symbol', query=TYPE_NAME, kinds='type'),
                          lambda text: ' — 0 results' in text)


def usages(fixture):
    return after_deleting(fixture, fixture.declaration, the_declaration_is_back(fixture), fixture.usages,
                          lambda text: f'Symbol not found: {TYPE_NAME}' in text)


def errors(fixture):
    return after_deleting(fixture, fixture.declaration, the_declaration_is_back(fixture), fixture.consumer_errors,
                          lambda text: f"Cannot resolve symbol '{TYPE_NAME}'" in text)


def linked(fixture):
    return after_deleting(fixture, fixture.linked,
                          lambda: 'class StencilPattern' in fixture.symbols_of_the_linked_file().text,
                          fixture.symbols_of_the_declaration,
                          lambda text: 'deleted on disk' in text and TYPE_NAME in text)


CHECKS = {it.__name__: it for it in (search, usages, errors, linked)}


class Contention:
    """The sandbox Rider's processes held to a few cores, shared with busy processes."""

    def __init__(self, cores, load):
        self.cores = list(range(psutil.cpu_count() - cores, psutil.cpu_count())) if cores else []
        self.load = load
        self.sandbox = []
        self.busy = []

    def __enter__(self):
        if not self.cores:
            return self
        self.sandbox = sandbox_processes()
        if not self.sandbox:
            raise SystemExit('Found no sandbox Rider process')
        for process in self.sandbox:
            process.cpu_affinity(self.cores)
        for _ in range(self.load):
            busy = multiprocessing.Process(target=spin, daemon=True)
            busy.start()
            psutil.Process(busy.pid).cpu_affinity(self.cores)
            self.busy.append(busy)
        print(f'{len(self.sandbox)} sandbox processes on cores {self.cores}, with {self.load} busy processes', flush=True)
        return self

    def __exit__(self, *_):
        for busy in self.busy:
            busy.terminate()
        every_core = list(range(psutil.cpu_count()))
        for process in self.sandbox:
            try:
                process.cpu_affinity(every_core)
            except psutil.NoSuchProcess:
                pass


def sandbox_processes():
    """The sandbox Rider's frontend, which its command line tells, and every process under it."""
    for process in psutil.process_iter(['name', 'cmdline']):
        if process.info['name'] == 'rider64.exe' and SANDBOX_FOLDER in ' '.join(process.info['cmdline'] or []):
            return [process, *process.children(recursive=True)]
    return []


def spin():
    while True:
        pass


class Answer:
    def __init__(self, text, seconds):
        self.text = text
        self.seconds = seconds


def call(args, name, **arguments):
    request = urllib.request.Request(
        args.url,
        json.dumps({'jsonrpc': '2.0', 'id': 1, 'method': 'tools/call',
                    'params': {'name': name, 'arguments': dict(arguments, solutionName=args.solution)}}).encode(),
        {'Content-Type': 'application/json'})
    started = time.monotonic()
    with urllib.request.urlopen(request, timeout=130) as response:
        result = json.load(response)
    if 'error' in result:
        raise SystemExit(f'{name} failed: {result["error"]}')
    return Answer('\n'.join(item.get('text', '') for item in result['result']['content']), time.monotonic() - started)


if __name__ == '__main__':
    main()
