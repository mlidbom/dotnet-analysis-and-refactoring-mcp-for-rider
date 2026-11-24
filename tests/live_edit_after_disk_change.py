#!/usr/bin/env python3
"""Check that an edit right after a change on disk lands on the disk's text, not at the offsets of an older copy.

On 2026-11-24 a rename in a file a script had just rewritten replaced text at the old offsets: `KindOfUnansweredItem
WaitingForYourGo` became `KinSuggestionsm WaitingForYourGo`. Each round here rewrites a file on disk, changing the
length of a comment above the symbol, renames the symbol at once, and compares the file with the text expected: the
one written, with the rename applied. Run it against the sandbox Rider with the fixture solution open:

    dotnet run --project tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools -- sandbox start <fixture .slnx>
    python tests/live_edit_after_disk_change.py --solution <fixture .slnx>

It leaves the file as the fixture has it.
"""
import argparse
import json
import pathlib
import time
import urllib.request

COMMENTS = ['/// <summary>What an <see cref="ILoader"/> loads.</summary>',
            '/// <summary>What an <see cref="ILoader"/> loads, which this comment, longer every other round, shifts.</summary>']


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--url', default='http://127.0.0.1:23841/', help='The sandbox Rider by default')
    parser.add_argument('--solution', required=True, help='Absolute path of the open fixture solution')
    parser.add_argument('--file', default='Shapes.Drawing/Moving/Loader.cs', help='Relative to the solution')
    parser.add_argument('--rounds', type=int, default=20)
    parser.add_argument('--pause', type=float, default=0.0, help='Seconds between the change on disk and the rename')
    args = parser.parse_args()

    file = pathlib.Path(args.solution).parent / args.file
    original = file.read_bytes()
    names = ['Loaded', 'LoadedAgain']
    wrong = 0
    try:
        for round_number in range(1, args.rounds + 1):
            old, new = names[round_number % 2 == 0], names[round_number % 2 == 1]
            text = file.read_text(encoding='utf-8')
            current = next(comment for comment in COMMENTS if comment in text)
            written = text.replace(current, COMMENTS[1 - COMMENTS.index(current)])
            file.write_text(written, encoding='utf-8', newline='')
            time.sleep(args.pause)
            answer = call(args, 'rename_symbol', symbolName=f'Shapes.Drawing.Moving.{old}', newName=new)
            expected = written.replace(f'{old} Load()', f'{new} Load()').replace(f'class {old};', f'class {new};')
            on_disk = file.read_text(encoding='utf-8')
            fine = on_disk == expected
            print(f'round {round_number}: {answer.seconds:.2f}s{"" if fine else "  WRONG"}')
            if not fine:
                wrong += 1
                print(f'--- the rename answered:\n{answer.text}\n--- the file holds:\n{on_disk}\n--- expected:\n{expected}\n---')
                break
    finally:
        file.write_bytes(original)
    print(f'{wrong} wrong files in {round_number} rounds')
    raise SystemExit(1 if wrong else 0)


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
