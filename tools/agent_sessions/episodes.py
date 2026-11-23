"""Episodes of C# work in which Royder fell short, or would likely have done better than what the agent did.

An episode is the calls that one need took: a text search and the reads of its hits, a Royder call and what the agent
did after it. Each call belongs to one episode at most, the first found for it in the order of `KINDS`.
"""
import os
from dataclasses import dataclass

from csharp_operations import Build, RoyderQuestion, SourceEdit, SourceRead, TextSearch, operations_of

LOOKAHEAD = 6
EDIT_LOOKAHEAD = 20
LARGE_FILE_CHARACTERS = 8000

KINDS = {
    "royder-answered-nothing":
        "A Royder call failed or found nothing. The C# searches and reads in the calls after it are what the agent did "
        "instead.",
    "royder-then-searched-as-text":
        "After a Royder answer, a text search for a name Royder was asked about, and the reads of its hits.",
    "declaration-searched-as-text":
        "A text search for where a symbol is declared, such as `class Foo`, and the reads of its hits: "
        "go_to_definition or get_symbol_source.",
    "symbol-searched-as-text":
        "A text search for a symbol's name, and the reads of its hits: find_usages, search_symbol, or "
        "get_symbol_source.",
    "renamed-by-hand":
        "The same name replaced by another in several edits, or by a shell replacement: rename_symbol.",
    "whole-file-read":
        f"A whole C# file read, of more than {LARGE_FILE_CHARACTERS:,} characters: list_symbols_in_file and "
        "get_symbol_source for the members needed.",
    "compile-errors-found-by-building":
        "A build or test run that stopped at compile errors: get_file_errors after the edit.",
}


@dataclass(eq=False)
class Episode:
    """`then_edited`: whether the session edited a file the episode read or found within the next
    `EDIT_LOOKAHEAD` calls: reading what is to be edited is needed anyway, and Claude Code's Edit requires a Read."""
    kind: str
    calls: list
    note: str
    then_edited: bool = False

    @property
    def session(self):
        return self.calls[0].session

    @property
    def time(self):
        return self.calls[0].time

    @property
    def cost(self):
        return sum(it.cost for it in self.calls)


class SessionWork:
    """A session's calls with their C# operations, and the episodes found in them."""

    def __init__(self, session):
        self.session = session
        self.calls = session.calls
        self.operations = {call: operations_of(call) for call in self.calls}
        self.claimed = set()
        self.episodes = []

    @property
    def csharp_calls(self):
        return [it for it in self.calls if self.operations[it]]

    @property
    def royder_calls(self):
        return [it for it in self.calls if it.is_royder]

    def find_episodes(self):
        for index, call in enumerate(self.calls):
            for question in self._of(call, RoyderQuestion):
                self._after_royder(index, call, question)
        for index, call in enumerate(self.calls):
            if call not in self.claimed:
                self._searched_as_text(index, call)
        self._renamed_by_hand()
        for call in self.calls:
            if call not in self.claimed:
                self._whole_file_read(call)
                self._compile_errors(call)
        for episode in self.episodes:
            episode.then_edited = self._edited_after(episode)
        return self.episodes

    def _edited_after(self, episode):
        files = {it.file for call in episode.calls for it in self._of(call, SourceRead)}
        files |= {hit for call in episode.calls for it in self._of(call, TextSearch) for hit in it.files_hit}
        last = max(it.position for it in episode.calls)
        edited = {it.file for call in self.calls[last + 1:last + 1 + EDIT_LOOKAHEAD] for it in self._of(call, SourceEdit)}
        return any(_same_file(one, other) for one in edited for other in files)

    def _of(self, call, kind):
        return [it for it in self.operations[call] if isinstance(it, kind)]

    def _claim(self, kind, calls, note):
        """An episode of the calls given: the first, which found it, and those of the rest no other episode has."""
        first, *rest = calls
        calls = [first] + [it for it in dict.fromkeys(rest) if it not in self.claimed and it is not first]
        self.claimed.update(calls)
        self.episodes.append(Episode(kind, calls, note))

    def _following(self, index):
        """The C# work in the calls after the one at `index`, up to the next Royder call."""
        following = []
        for call in self.calls[index + 1:index + 1 + LOOKAHEAD]:
            if call.is_royder:
                break
            if call not in self.claimed and (self._of(call, TextSearch) or self._of(call, SourceRead)):
                following.append(call)
        return following

    def _after_royder(self, index, call, question):
        if question.answered_nothing:
            self._claim("royder-answered-nothing", [call] + self._following(index),
                        f"{question.tool} {question.names or question.files}")
            return
        names = {name.rsplit(".", 1)[-1] for name in question.names}
        searches = [it for it in self._following(index)
                    if any(names & {name.rsplit(".", 1)[-1] for name in search.identifiers} for search in self._of(it, TextSearch))]
        if searches:
            hits = {file for it in searches for search in self._of(it, TextSearch) for file in search.files_hit}
            self._claim("royder-then-searched-as-text", [call] + searches + self._reads_of(index, hits),
                        f"{question.tool} {sorted(names)}")

    def _searched_as_text(self, index, call):
        searches = [it for it in self._of(call, TextSearch) if it.identifiers]
        if not searches:
            return
        identifiers = {name for it in searches for name in it.identifiers}
        hits = {file for it in searches for file in it.files_hit}
        more_searches = [it for it in self._following(index)
                         if any(identifiers & set(search.identifiers) for search in self._of(it, TextSearch))]
        kind = "declaration-searched-as-text" if any(it.for_declaration for it in searches) else "symbol-searched-as-text"
        self._claim(kind, [call] + more_searches + self._reads_of(index, hits), f"{sorted(identifiers)}, {len(hits)} files hit")

    def _reads_of(self, index, files):
        return [it for it in self._following(index) if any(_same_file(read.file, hit) for read in self._of(it, SourceRead) for hit in files)]

    def _renamed_by_hand(self):
        by_pair = {}
        for call in self.calls:
            for edit in self._of(call, SourceEdit):
                if edit.renamed:
                    by_pair.setdefault((edit.renamed[0].lower(), edit.renamed[1].lower()), []).append(call)
        for pair, calls in by_pair.items():
            calls = list(dict.fromkeys(calls))
            by_shell = any(it.tool == "shell" for it in calls)
            if len(calls) >= 2 or by_shell:
                self._claim("renamed-by-hand", calls, f"{pair[0]} -> {pair[1]} in {len(calls)} calls")

    def _whole_file_read(self, call):
        large = [it for it in self._of(call, SourceRead) if it.whole]
        if large and len(call.output) * call.output_share > LARGE_FILE_CHARACTERS:
            self._claim("whole-file-read", [call], f"{os.path.basename(large[0].file)}, {len(call.output):,} characters")

    def _compile_errors(self, call):
        if any(it.compile_errors for it in self._of(call, Build)):
            self._claim("compile-errors-found-by-building", [call],
                        f"{max(it.compile_errors for it in self._of(call, Build))} kinds of compile error")


def _same_file(read_path, hit_path):
    read_path = read_path.replace("\\", "/").lower()
    hit_path = hit_path.replace("\\", "/").lower().removeprefix("./")
    return read_path.endswith(hit_path) or hit_path.endswith(read_path)
