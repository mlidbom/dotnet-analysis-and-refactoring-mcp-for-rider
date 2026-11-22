"""Disposable, same-named solutions from the current commit, with one identifiable probe per tree."""
from contextlib import contextmanager
import hashlib
import os
from pathlib import Path
import subprocess


ROOT = Path(__file__).resolve().parents[2]
PROJECT = Path("tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools")
SOURCE = PROJECT / "RoutingProbe.cs"
IMPORT = "using RoutingFixture.Dependencies;"


def git(directory, *args):
    return subprocess.check_output(["git", "-C", str(directory), *map(str, args)], cwd=ROOT)


class Worktree:
    def __init__(self, root, marker):
        self.root = root
        self.solution = root / "ReSharperMcp.sln"
        self.source = root / SOURCE
        self.marker = marker
        self.files = {
            self.source: f"namespace RoutingFixture;\ninternal sealed class {marker}\n{{\n   internal RoutingRequiredType Create() => new();\n}}\n".encode(),
            root / PROJECT / "RoutingDependency.cs": b"namespace RoutingFixture.Dependencies;\ninternal sealed class RoutingRequiredType;\n",
        }
        self.unique = root / PROJECT / f"{marker}Only.cs"
        self.files[self.unique] = f"namespace RoutingFixture;\ninternal sealed class {marker}Only;\n".encode()
        self.created = []

    def seed(self):
        for file, contents in self.files.items():
            with file.open("xb") as stream:
                self.created.append(file)
                stream.write(contents)

    def snapshot(self):
        """Include every tracked file and nonignored addition. Each nested Git tree is audited separately."""
        names = git(self.root, "ls-files", "-coz", "--exclude-standard").split(b"\0")
        result = {}
        for name in filter(None, names):
            path = self.root / os.fsdecode(name)
            if path.is_dir() and not path.is_symlink():
                if (path / ".git").exists():
                    continue
                raise RuntimeError(f"Unexpected directory in Git's file list: {path}")
            if path.is_symlink():
                content = os.fsencode(os.readlink(path))
            elif path.exists():
                content = path.read_bytes()
            else:
                content = b"<deleted>"
            result[str(path)] = hashlib.sha256(content).hexdigest()
        return result


@contextmanager
def private_worktrees(run):
    bare = run / "repo.git"
    revision = git(ROOT, "rev-parse", "HEAD").decode().strip()
    git(ROOT, "init", "--bare", "--quiet", bare)
    git(bare, "config", "core.longpaths", "true")
    git(bare, "config", "core.symlinks", "false")
    git(bare, "config", "core.hooksPath", str(run / "no-hooks"))
    git(bare, "fetch", "--quiet", "--no-tags", "--depth=1", ROOT, revision)
    outer = Worktree(run / "outer", "OuterRoutingMarker")
    peer = Worktree(run / "peer", "PeerRoutingMarker")
    nested = Worktree(outer.root / ".claude/worktrees/inner", "NestedRoutingMarker")
    added = []
    try:
        for tree in (outer, peer, nested):
            git(bare, "worktree", "add", "--quiet", "--detach", tree.root, revision)
            added.append(tree)
            tree.seed()
        yield outer, peer, nested
    finally:
        for tree in reversed(added):
            if (not tree.root.resolve().is_relative_to(run.resolve())
                    or Path(git(tree.root, "rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip()).resolve() != bare.resolve()):
                raise RuntimeError(f"Unexpected private worktree: {tree.root}")
            for path in tree.created:
                if path.exists():
                    if path.read_bytes() != tree.files[path]:
                        raise RuntimeError(f"Keeping a changed private probe for inspection: {path}")
                    path.unlink()
            git(bare, "worktree", "remove", tree.root)
