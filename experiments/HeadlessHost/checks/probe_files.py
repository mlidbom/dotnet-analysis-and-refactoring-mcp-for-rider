import os
import subprocess

from .engine import HERE, ROOT


PROJECT = HERE / "checks/fixture"
PROJECT_FILE = PROJECT / "LiveProbe.csproj"
PROBE = PROJECT / "HeadlessLiveProbe"
NAMESPACE = PROJECT_FILE.stem + "." + PROBE.name
TYPE = "LiveRoyderProbeSubject"
AFTER = "LiveRoyderProbeChanged"
IMPORT = "LiveRoyderProbeImport"
BEFORE_METHOD = "OriginalProbeMethod"
AFTER_METHOD = "RenamedProbeMethod"
SOURCE = PROBE / (TYPE + ".cs")
MOVED = PROBE / "Destination" / SOURCE.name
CALLER = PROBE / "ProbeCaller.cs"


def write_type(path, name=TYPE, namespace=NAMESPACE, body=""):
    path.parent.mkdir(exist_ok=True)
    path.write_text(f"namespace {namespace};\n\ninternal sealed class {name}\n{{\n{body}}}\n", encoding="utf-8")


class ProbeFiles:
    """Own a new fixture directory. A private Git index restores exactly the prepared source bytes."""

    def __init__(self, check, evidence):
        self.check = check
        objects = evidence / "fixture-objects"
        objects.mkdir()
        self.git_env = {**os.environ, "GIT_INDEX_FILE": str(evidence / "fixture.index"),
                        "GIT_OBJECT_DIRECTORY": str(objects)}
        self.baseline = {}

    def __enter__(self):
        if PROBE.exists():
            raise FileExistsError(f"Refusing to reuse an existing fixture: {PROBE}")
        PROBE.mkdir()
        try:
            if self.check != "add":
                write_type(SOURCE)
            if self.check == "rename":
                write_type(SOURCE, body=f"   internal int {BEFORE_METHOD}() => 7;\n")
                write_type(CALLER, "ProbeCaller", body=f"   int Call() => new {TYPE}().{BEFORE_METHOD}();\n")
            if self.check == "fix-usings":
                write_type(SOURCE, body=f"   internal {IMPORT} Create() => new();\n")
                write_type(PROBE / "Dependency" / (IMPORT + ".cs"), IMPORT, NAMESPACE + ".Dependency")
            if self.check in {"move-file", "move-type"}:
                write_type(MOVED.parent / "Anchor.cs", "LiveRoyderProbeAnchor", NAMESPACE + ".Destination")
            if self.check == "move-type":
                write_type(CALLER, "ProbeCaller", body=f"   internal {TYPE} Create() => new();\n")
            self.baseline = {p: p.read_bytes() for p in PROBE.rglob("*.cs")}
            self.git("read-tree", "--empty")
            if self.baseline:
                self.git("-c", "core.autocrlf=false", "add", "-f", "--", *self.baseline)
        except BaseException:
            self._cleanup()
            raise
        return self

    def git(self, *args):
        subprocess.run(["git", "-C", ROOT, *args], env=self.git_env, check=True)

    def __exit__(self, *_):
        try:
            if self.baseline:
                self.git("-c", "core.autocrlf=false", "restore", "--worktree", "--", *self.baseline)
                assert all(p.read_bytes() == original for p, original in self.baseline.items())
        finally:
            self._cleanup()

    def _cleanup(self):
        # A moved or newly added source is absent from the fixture's Git index.
        for path in {SOURCE, MOVED, CALLER, PROBE / "Dependency" / (IMPORT + ".cs"), MOVED.parent / "Anchor.cs"}:
            path.unlink(missing_ok=True)
        for folder in (MOVED.parent, PROBE / "Dependency", PROBE):
            if folder.exists():
                folder.rmdir()
