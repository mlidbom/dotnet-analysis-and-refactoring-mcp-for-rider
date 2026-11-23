"""Where agents' C# work on this machine went without Royder, or Royder fell short: ranked by how often times what
it cost. dev_docs/what-agents-lack.md says what the kinds of episode are for, and what was found with them.

    python tools/agent_sessions/mine.py rank     [--since 2026-11-16] [--until ...] [--agent claude|codex]
    python tools/agent_sessions/mine.py breakdown
    python tools/agent_sessions/mine.py daily
    python tools/agent_sessions/mine.py samples  <kind> [--top 10] [--skip 0]
    python tools/agent_sessions/mine.py royder
    python tools/agent_sessions/mine.py sessions [--top 30]

`rank`: each kind of episode, with how many, in how many sessions, and the tokens of context they cost.
`breakdown`: all the C# work, by what each operation was: a Royder call, a text search, a read, an edit, a build.
`daily`: per day, symbols looked up as text against through Royder's navigation: what a fix should move.
`samples`: the costliest episodes of a kind, call by call, to read what the agent needed.
`royder`: Royder's calls by tool: how many, how many answered nothing, and what they cost.
`sessions`: the sessions that did C# work, whether Royder was offered and used, and what their C# work cost.

Times are UTC. Costs are tokens of context read: see records.py.
"""
import argparse
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone

from csharp_operations import category_of
from episodes import KINDS, SessionWork
from records import all_sessions, parse_time


def millions(tokens):
    return f"{tokens / 1e6:,.1f}M"


def read_work(arguments):
    since = parse_time(arguments.since).replace(tzinfo=timezone.utc)
    until = parse_time(arguments.until).replace(tzinfo=timezone.utc) if arguments.until else None
    agents = [arguments.agent] if arguments.agent else ["claude", "codex"]
    works = []
    for session in all_sessions(since, agents):
        session.calls = [it for it in session.calls if since <= it.time and (not until or it.time < until)]
        work = SessionWork(session)
        work.find_episodes()
        works.append(work)
    return works


def print_rank(works):
    csharp_cost = sum(call.cost for work in works for call in work.csharp_calls)
    royder_cost = sum(call.cost for work in works for call in work.royder_calls)
    csharp_sessions = [it for it in works if it.csharp_calls]
    print(f"{len(csharp_sessions)} sessions did C# work, in {sum(len(it.csharp_calls) for it in works):,} calls costing "
          f"{millions(csharp_cost)} tokens; Royder's {sum(len(it.royder_calls) for it in works):,} calls cost {millions(royder_cost)}.\n")
    print("kind | episodes | sessions | calls | cost | of the C# work's | per episode | then edited")
    episodes = [it for work in works for it in work.episodes]
    for kind in sorted(KINDS, key=lambda it: -sum(episode.cost for episode in episodes if episode.kind == it)):
        of_kind = [it for it in episodes if it.kind == kind]
        cost = sum(it.cost for it in of_kind)
        print(f"{kind} | {len(of_kind):,} | {len({it.session for it in of_kind})} | {sum(len(it.calls) for it in of_kind):,}"
              f" | {millions(cost)} | {cost / max(csharp_cost, 1):.1%} | {cost / max(len(of_kind), 1) / 1000:,.0f}k"
              f" | {sum(it.then_edited for it in of_kind) / max(len(of_kind), 1):.0%}")


def print_samples(works, kind, top, skip):
    print(KINDS[kind] + "\n")
    episodes = sorted((it for work in works for it in work.episodes if it.kind == kind), key=lambda it: -it.cost)
    for number, episode in enumerate(episodes[skip:skip + top], skip + 1):
        print(f"## {number}. {episode.session.label} {episode.time:%m-%d %H:%M} | {millions(episode.cost)} | {episode.note}")
        print(f"   {episode.session.path}")
        for call in episode.calls:
            first_lines = " / ".join(line.strip() for line in call.output.strip().splitlines()[:3])[:200]
            print(f"   - [{call.position}] {call.described()} | {len(call.output):,} chars x {call.responses_after}"
                  f" responses{' | FAILED' if call.failed else ''}")
            print(f"       -> {first_lines}")
        print()


def print_breakdown(works):
    """A call that does several things has its cost shared equally among them."""
    costs, calls, characters = defaultdict(float), Counter(), Counter()
    for work in works:
        for call in work.csharp_calls:
            operations = work.operations[call]
            for operation in operations:
                key = (call.session.agent, category_of(operation))
                costs[key] += call.cost / len(operations)
                calls[key] += 1
                characters[key] += len(call.output) * call.output_share / len(operations)
    total = sum(costs.values())
    print("agent | operation | calls | output characters | cost | of the C# work's")
    for key in sorted(costs, key=lambda it: -costs[it]):
        print(f"{key[0]} | {key[1]} | {calls[key]:,} | {characters[key] / 1e6:,.1f}M | {millions(costs[key])} | {costs[key] / total:.1%}")


ROYDER_NAVIGATION = {"royder.find_usages", "royder.go_to_definition", "royder.get_symbol_source", "royder.get_symbol_info",
                     "royder.search_symbol", "royder.list_symbols_in_file", "royder.find_implementations",
                     "royder.get_call_hierarchy", "royder.get_type_hierarchy", "royder.browse_namespace", "royder.flow"}
LOOKED_UP_AS_TEXT = {"symbol-searched-as-text", "declaration-searched-as-text"}


def print_daily(works):
    """Per day, how symbols were looked up: as text, or through Royder's navigation; the measure a fix should move."""
    days = defaultdict(Counter)
    for work in works:
        for call in work.calls:
            if call.tool in ROYDER_NAVIGATION:
                days[call.time.date()]["through Royder"] += 1
            elif call.is_royder:
                days[call.time.date()]["Royder's other calls"] += 1
        for episode in work.episodes:
            if episode.kind in LOOKED_UP_AS_TEXT:
                days[episode.time.date()]["as text"] += 1
            elif episode.kind == "whole-file-read":
                days[episode.time.date()]["whole files"] += 1
        for day in {call.time.date() for call in work.csharp_calls}:
            days[day]["sessions"] += 1
    print("day (UTC) | sessions doing C# work | symbols looked up as text | through Royder | share through Royder"
          " | large whole files read | Royder's other calls")
    for day, counts in sorted(days.items()):
        looked_up = counts["as text"] + counts["through Royder"]
        print(f"{day} | {counts['sessions']} | {counts['as text']} | {counts['through Royder']}"
              f" | {counts['through Royder'] / max(looked_up, 1):.0%} | {counts['whole files']} | {counts['''Royder's other calls''']}")


def print_royder(works):
    calls = [call for work in works for call in work.royder_calls]
    print("tool | calls | of them answered nothing | Claude | Codex | cost")
    by_tool = defaultdict(list)
    for call in calls:
        by_tool[call.tool].append(call)
    for tool, of_tool in sorted(by_tool.items(), key=lambda item: -len(item[1])):
        nothing = 0
        for call in of_tool:
            work = next(it for it in works if it.session is call.session)
            nothing += any(getattr(it, "answered_nothing", False) for it in work.operations[call])
        agents = Counter(it.session.agent for it in of_tool)
        print(f"{tool} | {len(of_tool)} | {nothing} | {agents['claude']} | {agents['codex']} | {millions(sum(it.cost for it in of_tool))}")


def print_sessions(works, top):
    csharp = [it for it in works if it.csharp_calls]
    groups = Counter((it.session.agent, it.session.royder_offered, bool(it.royder_calls)) for it in csharp)
    costs = defaultdict(float)
    for it in csharp:
        costs[(it.session.agent, it.session.royder_offered, bool(it.royder_calls))] += sum(call.cost for call in it.csharp_calls)
    print("agent | Royder offered | Royder used | sessions | their C# work's cost")
    for key, count in sorted(groups.items()):
        print(f"{key[0]} | {key[1]} | {key[2]} | {count} | {millions(costs[key])}")
    print("\nsession | started | offered | Royder calls | C# calls | C# cost | folder")
    for work in sorted(csharp, key=lambda it: -sum(call.cost for call in it.csharp_calls))[:top]:
        print(f"{work.session.label} | {work.calls[0].time:%m-%d %H:%M} | {work.session.royder_offered} | {len(work.royder_calls)}"
              f" | {len(work.csharp_calls)} | {millions(sum(call.cost for call in work.csharp_calls))} | {work.session.folder}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("what", choices=["rank", "breakdown", "daily", "samples", "royder", "sessions"])
    parser.add_argument("kind", nargs="?", choices=list(KINDS))
    parser.add_argument("--since", default="2026-11-16", help="UTC date or time")
    parser.add_argument("--until", help="UTC date or time, excluded")
    parser.add_argument("--agent", choices=["claude", "codex"])
    parser.add_argument("--top", type=int, default=10)
    parser.add_argument("--skip", type=int, default=0)
    arguments = parser.parse_args()
    if arguments.what == "samples" and not arguments.kind:
        parser.error("samples needs a kind")
    works = read_work(arguments)
    if arguments.what == "rank":
        print_rank(works)
    elif arguments.what == "breakdown":
        print_breakdown(works)
    elif arguments.what == "daily":
        print_daily(works)
    elif arguments.what == "samples":
        print_samples(works, arguments.kind, arguments.top, arguments.skip)
    elif arguments.what == "royder":
        print_royder(works)
    else:
        print_sessions(works, arguments.top)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
