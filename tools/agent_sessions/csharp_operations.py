"""What a tool call did with C# source: asked Royder, searched it as text, read it, edited it, or built it.

A shell command can do several of these at once, so a call has a list of operations, often empty: a call that does
none of them with C# isn't C# work here.
"""
import re
from dataclasses import dataclass

CSHARP_FILE = re.compile(r"\.cs\b")
CSHARP_SCOPE = re.compile(r"\.cs\b|(?:-t|--type)[= ]?cs\b|--include=\S*\.cs\b")
HIT_FILE = re.compile(r"(?m)^((?:[A-Za-z]:)?[^:\r\n\"]*?\.cs)(?=:\d|-\d|:|\r?$)")
COMPILE_ERROR = re.compile(r"error (CS\d{4})")
IDENTIFIER = re.compile(r"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$")
DECLARATION_KEYWORDS = {"class", "interface", "record", "struct", "enum", "delegate", "void", "namespace"}
BUILT_IN_TYPES = {"bool", "int", "string", "object", "long", "double", "float", "decimal", "byte", "char", "uint",
                  "ulong", "short", "dynamic"}
OTHER_KEYWORDS = {"public", "private", "protected", "internal", "static", "sealed", "abstract", "partial", "override",
                  "virtual", "async", "readonly", "new", "this", "base", "var", "return", "await", "using", "const"}
ROYDER_REFUSALS = (r"Symbol not found|Ambiguous symbol|No \w+ named '|File not found|Cannot resolve symbol"
                   r"|No resolvable symbol|No syntax node|No solution|Unknown tool")
ROYDER_ANSWERED_NOTHING = re.compile(r"^\W*(?:\{\s*\"error\"|error\b|MCP error|" + ROYDER_REFUSALS + ")", re.IGNORECASE)
ROYDER_ANSWERED_NOTHING_IN_A_SCRIPT = re.compile(r"\"isError\"\s*:\s*true|\"error\"\s*:|" + ROYDER_REFUSALS)
SEARCH_PROGRAMS = {"rg", "grep", "egrep", "fgrep", "select-string", "sls", "findstr"}
READ_PROGRAMS = {"cat", "type", "head", "tail", "get-content", "gc", "less", "more", "bat"}
OPTIONS_WITH_VALUES = {
    "rg": {"-e", "--regexp", "-g", "--glob", "--iglob", "-t", "--type", "-T", "--type-not", "-A", "-B", "-C", "-m",
           "--max-count", "-r", "--replace", "-M", "--max-columns", "-j", "--threads", "-f", "--file", "--sort",
           "--sortr", "--type-add", "--max-depth", "-d", "--context", "--after-context", "--before-context"},
    "grep": {"-e", "--regexp", "-f", "--file", "-A", "-B", "-C", "-m", "--max-count", "--include", "--exclude",
             "--exclude-dir"},
    "select-string": {"-pattern", "-path", "-literalpath", "-include", "-exclude", "-context", "-encoding"},
}


@dataclass
class RoyderQuestion:
    """A call of one of Royder's tools: the symbol names and files it named, and whether it answered nothing."""
    tool: str
    names: list
    files: list
    answered_nothing: bool


@dataclass
class TextSearch:
    """A search of C# source as text. `identifiers` are the symbols it looks for when it looks for nothing else, and
    `for_declaration` whether it looked for their declarations, such as `class Foo`."""
    pattern: str
    identifiers: list
    for_declaration: bool
    files_hit: list


@dataclass
class SourceRead:
    file: str
    whole: bool


@dataclass
class SourceEdit:
    """An edit of a C# file. `renamed` is (old, new) when the edit replaced one identifier with another and did
    nothing else."""
    file: str
    renamed: tuple | None


@dataclass
class Build:
    compile_errors: int


def category_of(operation):
    """A few words for what the operation is, for summing up what C# work went on."""
    if isinstance(operation, RoyderQuestion):
        return "Royder call that answered nothing" if operation.answered_nothing else "Royder call"
    if isinstance(operation, TextSearch):
        return ("text search for a declaration" if operation.for_declaration else "text search for a symbol") \
            if operation.identifiers else "other text search"
    if isinstance(operation, SourceRead):
        return "whole file read" if operation.whole else "part of a file read"
    if isinstance(operation, SourceEdit):
        return "edit replacing a name" if operation.renamed else "edit"
    return "build or test with compile errors" if operation.compile_errors else "build or test"


def operations_of(call):
    if call.is_royder:
        return [royder_question(call)]
    arguments = call.arguments
    if call.tool == "read":
        path = arguments.get("file_path") or ""
        return [SourceRead(path, arguments.get("offset") is None and arguments.get("limit") is None)] if CSHARP_FILE.search(path) else []
    if call.tool == "grep":
        return grep_tool_search(arguments, call.output)
    if call.tool == "edit":
        return tool_edits(arguments)
    if call.tool == "write":
        path = arguments.get("file_path") or ""
        return [SourceEdit(path, None)] if CSHARP_FILE.search(path) else []
    if call.tool == "patch":
        return patch_edits(arguments.get("patch") or "")
    if call.tool == "shell":
        return shell_operations(arguments.get("command") or "", call.output)
    return []


def royder_question(call):
    arguments = call.arguments
    text = arguments.get("script") or ""
    names = [value for key in ("symbolName", "query", "namespaceName") if (value := arguments.get(key))]
    names += [value for key in ("queries", "namespaceNames") for value in arguments.get(key) or [] if isinstance(value, str)]
    names += [it.get("symbolName") for it in arguments.get("symbols") or [] if isinstance(it, dict) and it.get("symbolName")]
    files = [value for key in ("filePath",) if (value := arguments.get(key))]
    files += [value for value in arguments.get("filePaths") or [] if isinstance(value, str)]
    names += re.findall(r"\b(?:symbolName|query|namespaceName)[\"']?\s*:\s*[\"'`]([^\"'`]+)", text)
    files += re.findall(r"\bfilePath[\"']?\s*:\s*[\"'`]([^\"'`]+)", text)
    if call.session.agent == "codex":
        answered_nothing = call.failed or bool(ROYDER_ANSWERED_NOTHING_IN_A_SCRIPT.search(call.output[:3000]))
    else:
        answered_nothing = call.failed or bool(ROYDER_ANSWERED_NOTHING.search(call.output[:300]))
    return RoyderQuestion(call.tool.removeprefix("royder."), [str(it) for it in names], files, answered_nothing)


def grep_tool_search(arguments, output):
    pattern = arguments.get("pattern") or ""
    scope = " ".join(str(arguments.get(key) or "") for key in ("path", "glob", "type"))
    hits = files_hit(output)
    if not (CSHARP_SCOPE.search(scope) or arguments.get("type") == "cs" or hits):
        return []
    return [text_search(pattern, hits)]


def text_search(pattern, hits):
    identifiers, for_declaration = identifiers_sought(pattern)
    return TextSearch(pattern, identifiers, for_declaration, hits)


def files_hit(output):
    return list(dict.fromkeys(match.strip() for match in HIT_FILE.findall(output)))


def identifiers_sought(pattern):
    """The symbols a search pattern looks for, when more than half of its alternatives look for one symbol each; else
    none. And whether any of those looks for its declaration."""
    alternatives = re.split(r"\\?\|", pattern)
    symbols = [it for it in map(_symbol_sought, alternatives) if it]
    if len(symbols) * 2 <= len(alternatives):
        return [], False
    return list(dict.fromkeys(name for name, _ in symbols)), any(declaration for _, declaration in symbols)


def _symbol_sought(alternative):
    """(name, whether it looks for its declaration) when the alternative looks for one symbol: its name, possibly
    qualified, possibly with modifiers or a declaration keyword before it, such as `class Foo`, or a type, such as
    `static bool IsFoo`, and a call's parenthesis or a generic's angle bracket after it."""
    text = re.sub(r"\\s[+*]?|\\b|\\<|\\>", " ", alternative)
    text = re.sub(r"\\(.)", r"\1", text)
    words = [it.strip(".(<>,;:=)") for it in re.sub(r"\.\*|\.\+|[\^$?*+\[\]{}]", " ", text).split()]
    words = [it for it in words if it and it not in OTHER_KEYWORDS]
    declaration = any(it in DECLARATION_KEYWORDS or it in BUILT_IN_TYPES for it in words)
    names = [it for it in words if it not in DECLARATION_KEYWORDS and it not in BUILT_IN_TYPES]
    if len(names) == 1 and looks_like_a_symbol(names[0]):
        return names[0], declaration
    if len(names) == 2 and all(map(looks_like_a_symbol, names)) and re.search(r"\w(?:\s|\\s)", alternative):
        return names[1], True
    return None


def looks_like_a_symbol(word):
    """A C# name as code writes it, such as `Foo`, `fooBar`, `_foo` or `Foo.Bar`: not a plain word, nor a constant."""
    return bool(IDENTIFIER.match(word)) and not word.isupper() and (word != word.lower() or "_" in word or "." in word)


def tool_edits(arguments):
    path = arguments.get("file_path") or ""
    if not CSHARP_FILE.search(path):
        return []
    edits = arguments.get("edits") or [arguments]
    return [SourceEdit(path, renaming(it.get("old_string") or "", it.get("new_string") or "")) for it in edits]


def renaming(old, new):
    """(old name, new name) when `new` is `old` with one name replaced throughout: whole identifiers, or the same words
    of them, such as `Foo` in `FooFactory` and `_foo`."""
    old_tokens, new_tokens = re.findall(r"\w+|\S", old), re.findall(r"\w+|\S", new)
    if len(old_tokens) != len(new_tokens):
        return None
    replaced = [_replaced_words(a, b) for a, b in zip(old_tokens, new_tokens) if a != b]
    if not replaced or len({(a.lower(), b.lower()) for a, b in replaced}) != 1:
        return None
    pair = replaced[0]
    return pair if all(IDENTIFIER.match(it) for it in pair) else None


def _replaced_words(old, new):
    """The words of identifier `old` that `new` replaces, words as camel case and underscores divide them."""
    old_words, new_words = _words_of(old), _words_of(new)
    while old_words and new_words and old_words[0] == new_words[0]:
        old_words, new_words = old_words[1:], new_words[1:]
    while old_words and new_words and old_words[-1] == new_words[-1]:
        old_words, new_words = old_words[:-1], new_words[:-1]
    return ("".join(old_words), "".join(new_words)) if old_words and new_words else (old, new)


def _words_of(identifier):
    return re.findall(r"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])|_", identifier)


def patch_edits(patch):
    """Edits of a Codex patch, file by file: `*** Update File:` and `*** Add File:` sections."""
    edits = []
    for match in re.finditer(r"^\*\*\* (?:Update|Add) File: (.+?)\r?$(.*?)(?=^\*\*\* |\Z)", patch, re.M | re.S):
        path, body = match.group(1).strip(), match.group(2)
        if not CSHARP_FILE.search(path):
            continue
        removed = "\n".join(line[1:] for line in body.splitlines() if line.startswith("-"))
        added = "\n".join(line[1:] for line in body.splitlines() if line.startswith("+"))
        edits.append(SourceEdit(path, renaming(removed, added) if removed else None))
    return edits


def shell_operations(command, output):
    operations = []
    command = without_here_documents(command)
    mentions_csharp = bool(CSHARP_SCOPE.search(command))
    for segment, piped in segments(command):
        segment = re.sub(r"^[\s(&@]*(?:\$[\w:]+\s*=\s*)?[\s(&@]*", "", segment)
        words = split_words(segment)
        if not words:
            continue
        program = re.sub(r"\.exe$", "", words[0].lower().replace("\\", "/").rsplit("/", 1)[-1])
        if program == "git" and len(words) > 1 and words[1] == "grep":
            program, words = "grep", words[1:]
        if program in SEARCH_PROGRAMS and (piped or "-v" in words):
            continue
        if program in SEARCH_PROGRAMS:
            hits = files_hit(output)
            if mentions_csharp or hits:
                operations.append(text_search(search_pattern(program, words[1:]), hits))
        elif program in READ_PROGRAMS or program == "sed" and "-n" in words and not _in_place(words):
            operations += [SourceRead(path, program in ("cat", "type", "get-content", "gc") and not _ranged(command))
                           for path in words[1:] if CSHARP_FILE.search(path) and not path.startswith("-")]
        elif program in ("sed", "perl") and _in_place(words) or "-replace" in segment.lower() and mentions_csharp:
            operations += _shell_replacements(segment, words)
        elif program in ("dotnet", "msbuild") and any(it in ("build", "test", "msbuild") for it in words[1:3]) or program == "msbuild":
            operations.append(Build(len(set(COMPILE_ERROR.findall(output)))))
    return operations


def search_pattern(program, words):
    program = "select-string" if program == "sls" else "grep" if program in ("egrep", "fgrep") else program
    with_values = OPTIONS_WITH_VALUES.get(program, set())
    patterns, positional = [], []
    index = 0
    while index < len(words):
        word = words[index]
        lowered = word.lower()
        if lowered in ("-e", "--regexp", "-pattern"):
            patterns.append(words[index + 1] if index + 1 < len(words) else "")
            index += 2
            continue
        if lowered in with_values:
            index += 2
            continue
        if not word.startswith("-") and not word.startswith("/"):
            positional.append(word)
        index += 1
    return "|".join(patterns) if patterns else positional[0] if positional else ""


def _in_place(words):
    return any(re.fullmatch(r"-\w*i\w*(\.\w+)?|--in-place", it) for it in words[1:3])


def _ranged(segment):
    return bool(re.search(r"-TotalCount|-Tail|-Head|-First|-Skip|-Index|\[\s*-?\d+\s*\.\.", segment, re.IGNORECASE))


def _shell_replacements(segment, words):
    files = [it for it in words if CSHARP_FILE.search(it) and not it.startswith("-")]
    pairs = re.findall(r"s([/|#])(?:\\b)?([A-Za-z_]\w*)(?:\\b)?\1([A-Za-z_]\w*)\1", segment)
    pairs = [(old, new) for _, old, new in pairs]
    pairs += re.findall(r"-replace\s+['\"](?:\\b)?([A-Za-z_]\w*)(?:\\b)?['\"]\s*,\s*['\"]([A-Za-z_]\w*)['\"]", segment)
    renamed = pairs[0] if len(pairs) == 1 else None
    return [SourceEdit(path, renamed) for path in files or ["(files the command found)"]]


HERE_DOCUMENT = re.compile(r"<<-?\s*(['\"]?)(\w+)\1([^\n]*)\n.*?\n[ \t]*\2[ \t]*(?=\r?\n|$)", re.S)
HERE_STRING = re.compile(r"@'\r?\n.*?\r?\n'@|@\"\r?\n.*?\r?\n\"@", re.S)


def without_here_documents(command):
    """The command without the bodies of its here-documents, `<<'EOF'` to `EOF`, and of PowerShell's here-strings:
    they are a program's input, such as a Python script, not commands."""
    return HERE_STRING.sub("''", HERE_DOCUMENT.sub(r"\3", command))


def segments(command):
    """The command's simple commands, split at `&&`, `||`, `;`, `|` and line breaks outside quotes, each with whether
    a pipe feeds it: a search fed by a pipe filters what came before it, and searches no source."""
    parts, current, quote, piped = [], [], None, False
    index = 0
    while index < len(command):
        character = command[index]
        if quote:
            current.append(character)
            if character == quote:
                quote = None
        elif character in "\"'":
            quote = character
            current.append(character)
        elif character == "&" and command[index:index + 2] != "&&":
            current.append(character)
        elif character in ";|\n&":
            parts.append(("".join(current), piped))
            current = []
            operator = command[index:index + 2]
            piped = character == "|" and operator != "||"
            if operator in ("&&", "||"):
                index += 1
        else:
            current.append(character)
        index += 1
    parts.append(("".join(current), piped))
    return [(text.strip(), was_piped) for text, was_piped in parts if text.strip()]


def split_words(segment):
    """Words, outside quotes split at white space, with their quotes removed; backslashes kept, as in Windows paths."""
    return [it[1:-1] if len(it) >= 2 and it[0] == it[-1] and it[0] in "\"'" else it
            for it in re.findall(r"\"[^\"]*\"|'[^']*'|\S+", segment)]
