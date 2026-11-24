"""The tool calls agents made on this machine, read from what Claude Code and Codex record of their sessions.

Claude Code writes each session's transcript under ~/.claude/projects/<project>/<session id>.jsonl, its subagents'
under <session id>/subagents/, and moves archived sessions to ~/.claude/session-archive. Codex writes each session's
rollout under ~/.codex/sessions/YYYY/MM/DD, and archived ones under ~/.codex/archived_sessions. Both are read into one
shape here: a session, and its tool calls in order, each with what it cost.

A call's cost is counted in tokens of context read: its share of the context that the response making it read, and
its output, read again by every later response until a compaction or the session's end. That is what each further
round trip, and each large output, costs a session; ~/.claude/dev_docs/usage holds the measurements behind it.
"""
import glob
import json
import os
import re
from collections import defaultdict
from dataclasses import dataclass, field
from datetime import datetime, timezone

CLAUDE_ROOTS = [os.path.expanduser("~/.claude/projects"), os.path.expanduser("~/.claude/session-archive")]
CODEX_ROOTS = [os.path.expanduser("~/.codex/sessions"), os.path.expanduser("~/.codex/archived_sessions")]
CHARACTERS_PER_TOKEN = 4
PROBE_SESSION_PREFIX = "royder-probe"
CLAUDE_ROYDER_PREFIX = "mcp__dotnet-analysis-and-refactoring__"
CODEX_ROYDER_PREFIX = "mcp__dotnet_analysis_and_refactoring__"
CLAUDE_TOOLS = {"Read": "read", "Grep": "grep", "Glob": "glob", "Edit": "edit", "MultiEdit": "edit", "Write": "write",
                "Bash": "shell", "PowerShell": "shell", "ToolSearch": "tool-search"}


def parse_time(text):
    return datetime.fromisoformat(text.replace("Z", "+00:00"))


def royder_tool(name):
    """`royder.find_usages` for Royder's tools as either agent names them, else None."""
    for prefix in (CLAUDE_ROYDER_PREFIX, CODEX_ROYDER_PREFIX):
        if name.startswith(prefix):
            return "royder." + name.removeprefix(prefix)
    return None


@dataclass(eq=False)
class Call:
    """One tool call: `tool` is `read`, `grep`, `glob`, `edit`, `write`, `shell`, `patch`, `tool-search`,
    `royder.<its tool>`, or the tool's own name for the others."""
    session: "Session"
    position: int
    time: datetime
    tool: str
    arguments: dict
    output: str = ""
    failed: bool = False
    context_tokens: float = 0.0
    output_share: float = 1.0
    responses_after: int = 0
    answered: datetime | None = None

    @property
    def seconds(self):
        """How long the call took, from the response making it to its result, or None without a result."""
        return (self.answered - self.time).total_seconds() if self.answered else None

    @property
    def output_tokens(self):
        return len(self.output) * self.output_share / CHARACTERS_PER_TOKEN

    @property
    def cost(self):
        """Tokens read because of this call: its share of its response's context, and its output read again."""
        return self.context_tokens + self.output_tokens * self.responses_after

    @property
    def is_royder(self):
        return self.tool.startswith("royder.")

    def described(self, width=160):
        text = self.arguments.get("command") or self.arguments.get("patch") or json.dumps(self.arguments, ensure_ascii=False)
        text = " ".join(str(text).split())
        return f"{self.tool} {text[:width]}"


@dataclass(eq=False)
class Session:
    agent: str
    path: str
    id: str = ""
    name: str = ""
    folder: str = ""
    parent: str = ""
    royder_offered: bool = False
    calls: list = field(default_factory=list)

    @property
    def is_subagent(self):
        return bool(self.parent)

    @property
    def label(self):
        return f"{self.agent} {self.name or self.id[:8]}"


class _Responses:
    """The responses of a session in order, its compactions, and the calls each response made: from them, each call's
    share of its response's context and the responses that read its output again."""

    def __init__(self):
        self.count = 0
        self.compactions = []
        self._context_tokens = {}
        self._calls = defaultdict(list)
        self._results = []

    def response(self, key, context_tokens):
        if key not in self._context_tokens:
            self.count += 1
            self._context_tokens[key] = context_tokens

    def made(self, key, call):
        self._calls[key].append(call)

    def compaction(self):
        self.compactions.append(self.count)

    def result(self, call):
        self._results.append((call, self.count))

    def settle(self):
        for key, calls in self._calls.items():
            for call in calls:
                call.context_tokens = self._context_tokens.get(key, 0) / len(calls)
        for call, seen_at in self._results:
            until = next((at for at in self.compactions if at >= seen_at), self.count)
            call.responses_after = until - seen_at


class ClaudeTranscript:
    MARKERS = ('"tool_use"', '"tool_result"', '"usage"', "compact_boundary", '"custom-title"', '"agent-name"',
               "deferred_tools_delta")

    def __init__(self, root, path, seen_calls):
        self.path = path
        self.seen_calls = seen_calls
        parts = os.path.relpath(path, root).split(os.sep)
        is_subagent = "subagents" in parts
        self.session = Session("claude", path, id=parts[-1].removesuffix(".jsonl"),
                               parent=parts[-3] if is_subagent and len(parts) >= 3 else "")

    def read(self):
        responses = _Responses()
        pending = {}
        with open(self.path, encoding="utf-8", errors="replace") as lines:
            for line in lines:
                if not any(marker in line for marker in self.MARKERS):
                    continue
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    continue
                kind = entry.get("type")
                self.session.folder = self.session.folder or entry.get("cwd") or ""
                if kind == "custom-title":
                    self.session.name = entry.get("customTitle") or self.session.name
                elif kind == "agent-name":
                    self.session.name = self.session.name or entry.get("agentName") or ""
                elif kind == "system" and entry.get("subtype") == "compact_boundary":
                    responses.compaction()
                elif kind == "attachment":
                    added = (entry.get("attachment") or {}).get("addedNames") or []
                    self.session.royder_offered |= any(royder_tool(name) for name in added)
                elif kind == "assistant":
                    self._assistant(entry, responses, pending)
                elif kind == "user":
                    self._results(entry, responses, pending)
        responses.settle()
        return self.session

    def _assistant(self, entry, responses, pending):
        """A response is written in several lines, one per block of its content, each with the response's usage."""
        message = entry.get("message") or {}
        key = (message.get("id"), entry.get("requestId"))
        usage = message.get("usage") or {}
        responses.response(key, sum(usage.get(it) or 0 for it in ("input_tokens", "cache_read_input_tokens", "cache_creation_input_tokens")))
        for block in message.get("content") or []:
            if not isinstance(block, dict) or block.get("type") != "tool_use" or block.get("id") in self.seen_calls:
                continue
            self.seen_calls.add(block.get("id"))
            name = block.get("name") or ""
            tool = royder_tool(name) or CLAUDE_TOOLS.get(name, name)
            call = Call(self.session, len(self.session.calls), parse_time(entry["timestamp"]), tool, block.get("input") or {})
            self.session.calls.append(call)
            self.session.royder_offered |= call.is_royder
            pending[block.get("id")] = call
            responses.made(key, call)

    @staticmethod
    def _results(entry, responses, pending):
        content = (entry.get("message") or {}).get("content")
        if not isinstance(content, list):
            return
        for block in content:
            if not isinstance(block, dict) or block.get("type") != "tool_result":
                continue
            call = pending.pop(block.get("tool_use_id"), None)
            if call is None:
                continue
            call.output = _text_of(block.get("content"))
            call.failed = bool(block.get("is_error"))
            call.answered = parse_time(entry["timestamp"]) if entry.get("timestamp") else None
            responses.result(call)


def _text_of(content):
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "\n".join(it.get("text", "") for it in content if isinstance(it, dict))
    return ""


class CodexRollout:
    """A Codex rollout. Codex runs its tools from JavaScript, through one `exec` tool: each script's `tools.<name>(...)`
    is a call here, and the script's output, which they share, is each one's, at an equal share of its cost."""

    def __init__(self, path, seen_calls):
        self.path = path
        self.seen_calls = seen_calls
        self.session = Session("codex", path)

    def read(self, names):
        """A response's calls are written before its `token_usage_record`."""
        responses = _Responses()
        pending = {}
        calls_of_next_response = []
        with open(self.path, encoding="utf-8", errors="replace") as lines:
            for line in lines:
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError:
                    continue
                kind = entry.get("type")
                payload = entry.get("payload") or {}
                if kind == "session_meta" and not self.session.id:
                    self.session.id = payload.get("id") or ""
                    self.session.folder = payload.get("cwd") or ""
                    self.session.parent = payload.get("parent_thread_id") or payload.get("forked_from_id") or ""
                    self.session.name = names.get(self.session.id) or ""
                elif kind == "compacted":
                    responses.compaction()
                elif kind == "token_usage_record":
                    response = payload.get("response_id")
                    responses.response(response, (payload.get("usage") or {}).get("input_tokens") or 0)
                    for call in calls_of_next_response:
                        responses.made(response, call)
                    calls_of_next_response = []
                elif kind == "response_item":
                    self._item(entry, payload, responses, pending, calls_of_next_response)
        responses.settle()
        return self.session

    def _item(self, entry, payload, responses, pending, calls_of_next_response):
        item = payload.get("type")
        if item == "message" and not self.session.royder_offered:
            self.session.royder_offered = CODEX_ROYDER_PREFIX.rstrip("_") in json.dumps(payload.get("content"))
        elif item in ("custom_tool_call", "function_call"):
            call_id = payload.get("call_id")
            if call_id in self.seen_calls:
                return
            self.seen_calls.add(call_id)
            time = parse_time(entry["timestamp"])
            if payload.get("name") == "exec":
                calls = [Call(self.session, 0, time, tool, arguments) for tool, arguments in script_calls(payload.get("input") or "")]
            else:
                calls = [Call(self.session, 0, time, payload.get("name") or "", {"arguments": payload.get("arguments")})]
            for call in calls:
                call.position = len(self.session.calls)
                call.output_share = 1 / len(calls)
                self.session.calls.append(call)
                self.session.royder_offered |= call.is_royder
            pending[call_id] = calls
            calls_of_next_response.extend(calls)
        elif item in ("custom_tool_call_output", "function_call_output"):
            calls = pending.pop(payload.get("call_id"), [])
            output = readable(_text_of(payload.get("output")))
            for call in calls:
                call.output = output
                call.failed = '"status":"rejected"' in output
                call.answered = parse_time(entry["timestamp"])
                responses.result(call)


def readable(output):
    """A script's output as text: its JSON-escaped line breaks and backslashes undone, as the agent reads them."""
    return output.replace("\\r\\n", "\n").replace("\\n", "\n").replace("\\\\", "\\").replace('\\"', '"')


SCRIPT_CALL = re.compile(r"tools\.(\w+)\s*\(")


def script_calls(script):
    """(tool, arguments) for each `tools.<name>(...)` in a Codex script."""
    for match in SCRIPT_CALL.finditer(script):
        name = match.group(1)
        argument_text = _balanced(script, match.end() - 1)
        tool = royder_tool(name)
        if tool:
            yield tool, {"script": argument_text}
        elif name == "exec_command":
            yield "shell", {"command": _string_after(argument_text, "cmd")}
        elif name == "apply_patch":
            yield "patch", {"patch": _first_string(argument_text)}
        else:
            yield name, {"script": argument_text[:500]}


def _balanced(text, open_at):
    """The text from the bracket at `open_at` to its match, skipping strings and template literals."""
    depth = 0
    index = open_at
    while index < len(text):
        character = text[index]
        if character in "\"'`":
            index = _string_end(text, index)
        elif character in "([{":
            depth += 1
        elif character in ")]}":
            depth -= 1
            if depth == 0:
                return text[open_at:index + 1]
        index += 1
    return text[open_at:]


def _string_end(text, start):
    quote = text[start]
    index = start + 1
    while index < len(text) and text[index] != quote:
        index += 2 if text[index] == "\\" else 1
    return index


def _string_at(text, start):
    end = _string_end(text, start)
    literal = text[start:end + 1]
    if literal.startswith('"'):
        try:
            return json.loads(literal)
        except json.JSONDecodeError:
            pass
    return literal[1:-1].replace("\\\\", "\\").replace("\\'", "'").replace("\\`", "`")


def _first_string(text):
    match = re.search(r"[\"'`]", text)
    return _string_at(text, match.start()) if match else text


def _string_after(text, key):
    match = re.search(r"\b" + key + r"\s*:\s*([\"'`])", text)
    return _string_at(text, match.start(1)) if match else text


def claude_sessions(since):
    seen_calls = set()
    for root in CLAUDE_ROOTS:
        for path in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True):
            if since and datetime.fromtimestamp(os.path.getmtime(path), timezone.utc) < since:
                continue
            session = ClaudeTranscript(root, path, seen_calls).read()
            if session.calls:
                yield session


def codex_thread_names():
    names = {}
    index = os.path.expanduser("~/.codex/session_index.jsonl")
    if os.path.exists(index):
        with open(index, encoding="utf-8") as lines:
            for line in lines:
                entry = json.loads(line)
                names[entry["id"]] = entry.get("thread_name") or ""
    return names


def codex_sessions(since):
    """Not filtered by the file's time: a rollout Codex holds open keeps the time it was created, on Windows."""
    seen_calls = set()
    names = codex_thread_names()
    paths = [path for root in CODEX_ROOTS for path in glob.glob(os.path.join(root, "**", "*.jsonl"), recursive=True)]
    for path in sorted(paths):
        session = CodexRollout(path, seen_calls).read(names)
        session.calls = [it for it in session.calls if not since or it.time >= since]
        if session.calls:
            yield session


def all_sessions(since, agents=("claude", "codex")):
    """The agents' sessions, without the probes' (probe.py) and their subagents': theirs is work done to measure."""
    sessions = []
    if "claude" in agents:
        sessions += claude_sessions(since)
    if "codex" in agents:
        sessions += codex_sessions(since)
    probes = {it.id for it in sessions if it.name.startswith(PROBE_SESSION_PREFIX)}
    return [it for it in sessions if it.id not in probes and it.parent not in probes]
