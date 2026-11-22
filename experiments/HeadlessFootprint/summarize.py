"""Summarize the completed cases using only the recorded evidence. No process is launched."""
import collections
import json
from datetime import datetime
from pathlib import Path
ROOT=Path(__file__).resolve().parent
def jsonfile(path):
    return json.loads(path.read_text(encoding="utf-8"))
def lines(path):
    return [json.loads(s) for s in path.read_text(encoding="utf-8").splitlines()]
def first(data,k):
    return next((r for r in data if r["kind"]==k),None)
def summary(case):
    event=lines(case/"events.jsonl")
    result=jsonfile(case/"result.json")
    start=first(event,"engine_start")["at"]
    end=first(event,"engine_exit")["at"]
    fs_path=case/"filesystem-events.jsonl"
    fs=lines(fs_path) if fs_path.exists() else []
    r={"engine_pid":first(event,"engine_start")["data"],"engine_exit":result["returncode"],
       "indexed_s":next((x["at"]-start for x in event if x["kind"]=="stdout" and x["data"].startswith("INDEXED ")),None),
       "query":[x["data"] for x in event if x["kind"]=="stdout" and
                (x["data"].startswith(("USAGES ","PROJECTS ")) or ".cs:" in x["data"])],
       "processes":[(p["pid"],p["ppid"],p["name"]) for p in result["ever_seen"]],
       "at_exit_and_after":[(round(x["at"]-end,3),[p["pid"] for p in x["data"]]) for x in event if x["kind"]=="survivors"],
       "listeners":[(x["data"]["pid"],x["data"]["local"]) for x in event if x["kind"]=="bound_port"],
       "closed_seconds_relative_to_engine_exit":[(x["data"],round(x["at"]-end,3)) for x in event if x["kind"]=="listener_closed"],
       "connections":[x["data"] for x in event if x["kind"]=="connected"]}
    rem=result.get("removed") or {}
    if rem:
        tree=ROOT/"worktrees"/case.name
        rel=case.name.lower()
        def within(path):return path.lower()==rel or path.lower().startswith(rel+"\\")
        descendants=[(t,a,p) for t,k,a,p in fs if k=="worktrees-parent" and within(p)]
        r["removal"]={"git_exit":rem["exit"],"from_start_to_go":first(event,"remove_go")["at"]-start,
                      "from_start_to_git_return":rem["finished"]-start,
                      "path_existed_then":rem["root_exists"],"residual_at_return":rem["remaining"],
                      "path_exists_at_end":result["root_exists"],
                      "listed_after":f"worktree {tree.as_posix()}\n" in rem["worktree_list"],
                      "events_after_git_return":[(round(t-rem["finished"],3),a,p) for t,a,p in descendants if t>rem["finished"]],
                      "trace_end_s_after_git":end-rem["finished"]+max(x["at"]-end for x in event if x["kind"]=="survivors")}
        git=case/"git-events.jsonl"
        if git.exists():
            r["git"]=[(x["event"],x["time"]) for x in lines(git) if
                      (x["event"]=="start" and "remove" in x.get("argv",[])) or
                      (x["event"]=="exit" and x.get("code")==255)]
    diff=case/"metadata-diff.json"
    if diff.exists() and not rem:
        d=jsonfile(diff)
        r["footprint"]={k: {mode:len(s[mode]) for mode in
                            ("added","modified","removed","before_errors","after_errors")} for k,s in d.items()}
        r["files_added_in_worktree"]=sorted(d["worktree"]["added"])
        r["state_categories"]=dict(collections.Counter(p.split("\\")[0] for p in d["state"]["added"]))
        r["external_notifications_during_engine"]={k:sorted(set(p for t,group,a,p in fs if group==k and start<=t<=end))
                                              for k in d if k not in ("state","worktree")}
        r["temporary_worktree_files"]=sorted(set(p for t,g,a,p in fs if g=="worktree" and p.endswith("~")))
    before=case/"settings-before.json"
    if before.exists():
        r["settings_equal"]=jsonfile(before)==jsonfile(case/"settings-after.json")
    return r
if __name__=="__main__":
    results={d.name:summary(d) for d in sorted((ROOT/"cases").iterdir()) if (d/"result.json").exists()}
    print(json.dumps(results,indent=2))
