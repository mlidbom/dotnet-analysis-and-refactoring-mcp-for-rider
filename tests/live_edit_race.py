#!/usr/bin/env python3
"""Reproduce the edit race: two edits of one file in a row, after which Rider's copy can be the first edit's text.

Renames a type that only its own file uses, and back, round after round, and counts the answers whose notes say
Rider's copy differs from disk. Run it against the sandbox Rider with the fixture solution open (see
dev_docs/backlog.md, the entry on two edits of one file in a row):

    dotnet run --project tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools -- sandbox start <fixture .sln>
    python tests/live_edit_race.py --solution <fixture .sln>

The renames restore the file each round, so the fixture is as it was after a run with no race. After one, the next
call's disk sync takes the disk's text back into Rider.
"""
import argparse
import json
import time
import urllib.request

SIGNS_OF_THE_RACE = ["hadn't saved", "differs from disk"]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--url', default='http://127.0.0.1:23841/', help='The sandbox Rider by default')
    parser.add_argument('--solution', required=True, help='Absolute path of the open fixture solution')
    parser.add_argument('--symbol', default='Shapes.Drawing.Moving.Loaded', help='A type only its own file uses')
    parser.add_argument('--other-name', default='LoadedAgain')
    parser.add_argument('--rounds', type=int, default=20)
    parser.add_argument('--pause', type=float, default=0.0, help='Seconds between the two edits of a round')
    parser.add_argument('--keep-going', action='store_true', help="Don't stop at the first race")
    args = parser.parse_args()

    namespace, _, name = args.symbol.rpartition('.')
    renamed = f'{namespace}.{args.other_name}'
    races = 0
    for round_number in range(1, args.rounds + 1):
        first = call(args, 'rename_symbol', symbolName=args.symbol, newName=args.other_name)
        time.sleep(args.pause)
        second = call(args, 'rename_symbol', symbolName=renamed, newName=name)
        signs = [(which, answer) for which, answer in (('first', first), ('second', second)) if any(sign in answer.text for sign in SIGNS_OF_THE_RACE)]
        print(f'round {round_number}: {first.seconds:.2f}s + {second.seconds:.2f}s{"  RACE" if signs else ""}')
        for which, answer in signs:
            races += 1
            print(f'--- the {which} edit answered:\n{answer.text}\n---')
        if signs and not args.keep_going:
            break
    print(f'{races} answers with signs of the race in {round_number} rounds')
    raise SystemExit(1 if races else 0)


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
