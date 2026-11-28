#!/usr/bin/env python3
"""Check that get_file_errors, right after a change on disk, reads the file as it is on disk, not ReSharper's caches of it.

On 2026-11-28 get_file_errors answered "{ expected" where a method's body had begun before a comment above it grew, and
"Cannot resolve symbol" for a call to a method of that file from another, 2.3 s after a script had changed the file. No
document of the file was allocated, so the disk sync created it from disk and found it current; but Rider's project
model still had the file's old last write time, since the standalone primary's private Rider passes changes on only
every 15 s unless asked, and ReSharper counts its caches of a file current for as long as that time is unchanged.

Each round grows the comment above the class of a file that was there when the solution opened, and that no call has
named, whose method another file calls, and checks both files at once. The comment moves the class: a member of the
class that ReSharper's caches of the file place at its old offset doesn't resolve. It runs the disk sync's path for a
change the project model hasn't registered, which the backend's log shows as CacheRepairs' line; it reproduced the
stale answer in none of the tries of 2026-11-28, before the fix too, since both Riders kept the files' documents
allocated, even five minutes after the solution opened (--settle 300). In a copy of the fixture solution in a Git
repository of its own, which the standalone primary's private Rider opens directly in C:/Dev:

    python tests/live_errors_after_disk_change.py create --solution C:/Dev/<copy>/<fixture .slnx>
    python tests/live_errors_after_disk_change.py check --url http://127.0.0.1:23751/ --solution C:/Dev/<copy>/<fixture .slnx>

`create` writes the files, in Shapes.Drawing/Echoes, before a Rider opens the solution; `check` opens it with its first
call when the primary serves it, and deletes the files at the end.
"""
import argparse
import json
import pathlib
import shutil
import time
import urllib.request

SHORT_COMMENT = '/// <summary>Says a word again.</summary>'
LONG_COMMENT = '/// <summary>Says a word again, below a comment that this script grows on disk, which moves the class.</summary>'


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', choices=['create', 'check'])
    parser.add_argument('--url', default='http://127.0.0.1:23841/', help='The sandbox Rider by default')
    parser.add_argument('--solution', required=True, help='Absolute path of the fixture solution')
    parser.add_argument('--rounds', type=int, default=3, help='For create: one pair of files for each round')
    parser.add_argument('--settle', type=float, default=20.0,
                        help='Seconds between the callers resolving and the first change: past the private Rider\'s 15 s')
    args = parser.parse_args()

    folder = pathlib.Path(args.solution).parent / 'Shapes.Drawing' / 'Echoes'
    if args.command == 'create':
        folder.mkdir(exist_ok=True)
        for number in range(1, args.rounds + 1):
            Pair(folder, number).create()
        return

    pairs = [Pair(folder, int(echo.stem.removeprefix('Echo'))) for echo in sorted(folder.glob('Echo[0-9]*.cs'))]
    if not pairs:
        raise SystemExit(f'No files in {folder}: run create first, before a Rider opens the solution.')
    wrong = 0
    try:
        wait_until_the_callers_resolve(args, pairs)
        time.sleep(args.settle)
        for pair in pairs:
            pair.grow_the_comment()
            answer = call(args, 'get_file_errors', filePaths=[str(pair.caller), str(pair.echo)])
            fine = answer.text.count(' — 0 diagnostics') == 2
            print(f'round {pair.number}: {answer.seconds:.2f}s{"" if fine else "  WRONG"}')
            if not fine:
                wrong += 1
                print(f'--- get_file_errors answered:\n{answer.text}\n---')
    finally:
        shutil.rmtree(folder, ignore_errors=True)
    print(f'{wrong} wrong answers in {len(pairs)} rounds')
    raise SystemExit(1 if wrong else 0)


class Pair:
    """Echo<number>.cs, whose class has a comment above it, and EchoCaller<number>.cs, which calls a method of the class."""

    def __init__(self, folder, number):
        self.number = number
        self.echo = folder / f'Echo{number}.cs'
        self.caller = folder / f'EchoCaller{number}.cs'

    def create(self):
        self.echo.write_text(self._echo_with(SHORT_COMMENT), encoding='utf-8', newline='\r\n')
        self.caller.write_text(f'''namespace Shapes.Drawing.Echoes;

/// <summary>Calls <see cref="Echo{self.number}.Repeat"/> from a file of its own.</summary>
public static class EchoCaller{self.number}
{{
  public static string Twice(string word)
  {{
    return Echo{self.number}.Repeat(word);
  }}
}}
''', encoding='utf-8', newline='\r\n')

    def grow_the_comment(self):
        self.echo.write_text(self._echo_with(LONG_COMMENT), encoding='utf-8', newline='\r\n')

    def _echo_with(self, comment):
        return f'''namespace Shapes.Drawing.Echoes;

{comment}
public static class Echo{self.number}
{{
  /// <summary>The word twice.</summary>
  public static string Repeat(string word)
  {{
    return word + " " + word;
  }}
}}
'''


def wait_until_the_callers_resolve(args, pairs):
    """Checks only the callers: a call that named an echo would allocate its document before the change."""
    deadline = time.monotonic() + 300
    callers = [str(pair.caller) for pair in pairs]
    while True:
        answer = call(args, 'get_file_errors', filePaths=callers)
        if answer.text.count(' — 0 diagnostics') == len(callers):
            return
        if time.monotonic() > deadline:
            raise SystemExit(f'The callers still don\'t resolve after 5 minutes:\n{answer.text}')
        time.sleep(2)


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
    with urllib.request.urlopen(request, timeout=300) as response:
        result = json.load(response)
    if 'error' in result:
        raise SystemExit(f'{name} failed: {result["error"]}')
    return Answer('\n'.join(item.get('text', '') for item in result['result']['content']), time.monotonic() - started)


if __name__ == '__main__':
    main()
