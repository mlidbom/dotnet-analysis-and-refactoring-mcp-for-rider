"""Run the stock host on a fresh private worktree. Logs are in cases/CASE."""
import argparse
import hashlib
import ipaddress
import json
import os
import queue
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path
import psutil
from prepare import HOME, HOST, JB, NO_WINDOW, PROGRAM, REPO, ROOT, SOLUTION, private_environment, run, worktree

now = time.time
def save(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")

class WorkerTree:
    def __init__(self, process, log, baseline):
        self.root = psutil.Process(process.pid)
        self.log = log
        self.members = {}
        self.baseline = baseline
        self.listen = []
        self.forbidden = []
        self.active_listeners = set()
        self.connections = set()
        self.stop = threading.Event()
        self.update(self.root)
        self.thread = threading.Thread(target=self.monitor, daemon=True)
        self.thread.start()

    def update(self, proc):
        if proc.pid in self.members or proc.pid in self.baseline:
            return
        try:
            with proc.oneshot():
                item = {"pid":proc.pid,"ppid":proc.ppid(),"name":proc.name(),
                        "created":proc.create_time(),"cmd":proc.cmdline(),"first_seen":now()}
                try: item["cwd"]=proc.cwd()
                except (psutil.NoSuchProcess,psutil.AccessDenied): pass
            self.members[proc.pid] = (proc, item)
            self.log("process", item)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass

    def monitor(self):
        while not self.stop.is_set():
            members = list(self.members.values())
            for proc, item in members:
                try:
                    if not proc.is_running():
                        continue
                    for child in proc.children(): self.update(child)
                except (psutil.NoSuchProcess,psutil.AccessDenied): pass
            ids = set(self.members)
            try:
                active = set()
                for conn in psutil.net_connections(kind="inet"):
                    if conn.pid in ids and conn.laddr and conn.raddr and conn.status == "ESTABLISHED":
                        key=(conn.pid,conn.laddr.ip,conn.laddr.port,conn.raddr.ip,conn.raddr.port)
                        if key not in self.connections:
                            self.connections.add(key)
                            self.log("connected",key)
                    if conn.pid in ids and conn.laddr and (conn.status=="LISTEN" or
                            (conn.type.name=="SOCK_DGRAM" and not conn.raddr)):
                        active.add((conn.pid,conn.laddr.ip,conn.laddr.port))
                        data = {"pid":conn.pid,"name":self.members[conn.pid][1]["name"],"local":list(conn.laddr),
                                "status":conn.status,"socket_type":str(conn.type),"observed":now()}
                        if not any(x["pid"]==conn.pid and x["local"]==data["local"] for x in self.listen):
                            self.listen.append(data)
                            self.log("bound_port", data)
                            if (conn.laddr.port in {*range(23751,23761),23841,23842,23900,23910}
                                    or not ipaddress.ip_address(conn.laddr.ip.split("%")[0]).is_loopback):
                                self.forbidden.append(data)
                for closed in self.active_listeners-active:
                    self.log("listener_closed",closed)
                self.active_listeners=active
            except (psutil.AccessDenied, OSError) as ex:
                self.log("socket_monitor_error", str(ex))
            self.stop.wait(.04)

    def live(self):
        return [item for proc,item in tuple(self.members.values()) if proc.is_running()]

    def kill_owned(self, children=True):
        owned = list(self.members.values())
        for proc,item in reversed(owned):
            if children or proc.pid == self.root.pid:
                try:
                    if proc.is_running() and proc.create_time()==item["created"]:
                        proc.kill()
                except psutil.NoSuchProcess: pass


def probe(case, phase, prop, release_roslyn=False, settings_fixture=False):
    if (ROOT/"RESTRICTED-PORT-STOP.json").exists():
        raise RuntimeError("A reserved port or non-loopback listener was found; stop.")
    tree = worktree(case)
    evidence = ROOT/"cases"/case
    evidence.mkdir(parents=True)
    state = evidence/"state"
    sentinels=[]
    if settings_fixture:
        workspace=tree/".idea"/(".idea."+Path(SOLUTION).stem)/".idea"/"workspace.xml"
        workspace.parent.mkdir(parents=True)
        workspace.write_text('<project version="4"><component name="FootprintFixture"/></project>\n',encoding="utf-8")
        personal=tree/(SOLUTION+".DotSettings.user")
        personal.write_text('<wpf:ResourceDictionary xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation"/>\n',encoding="utf-8")
        sentinels=[workspace,personal]
    def settings_state():
        return {str(p.relative_to(tree)):{"sha256":hashlib.sha256(p.read_bytes()).hexdigest(),
                                          "mtime_ns":p.stat().st_mtime_ns} for p in sentinels}
    if sentinels:save(evidence/"settings-before.json",settings_state())
    env = private_environment()
    for key, folder in {"JET_LOCAL_APPDATA_DIR":"local","JET_ROAMING_APPDATA_DIR":"roaming",
                       "RESHARPER_HOST_LOG_DIR":"logs","TEMP":"temp","TMP":"temp"}.items():
        (state/folder).mkdir(parents=True,exist_ok=True)
        env[key] = str(state/folder)
    if prop: env["DisablePackageAssetsCache"]="true"
    else: env.pop("DisablePackageAssetsCache", None)
    command = ["dotnet","exec","--depsfile",str(JB/"inspectcode.deps.json"),
               str(HOST/"bin/Debug/net10.0-windows/HeadlessHost.dll"),str(tree/SOLUTION),
               str(state/"solution-caches"), "-" if phase=="indexing" else PROGRAM, "--listen"]
    save(evidence/"command.json", {"argv":command,"property":prop,"worktree":str(tree),"cwd":str(evidence)})
    lock = threading.Lock()
    events = (evidence/"events.jsonl").open("w",encoding="utf-8",buffering=1)
    def log(kind, data):
        with lock:
            events.write(json.dumps({"at":now(),"kind":kind,"data":data},ensure_ascii=False)+"\n")
    def child(args, **kw):
        return subprocess.Popen(args, creationflags=NO_WINDOW,cwd=evidence,
                                stdin=subprocess.PIPE, stdout=subprocess.PIPE,stderr=subprocess.PIPE,**kw)
    removal = phase in ("startup","indexing","idle","query")
    if removal:
        git_env=private_environment()
        git_env["GIT_TRACE2_EVENT"]=str(evidence/"git-events.jsonl")
        remover = child(["pwsh","-NoLogo","-NoProfile","-File",str(ROOT/"remove-worktree.ps1"),str(tree)],
                        env=git_env,text=True,encoding="utf-8",errors="replace")
        if remover.stdout.readline().strip()!="ARMED": raise RuntimeError(remover.stderr.read())
    observer = None
    if phase == "normal" or removal:
        from observations import Observations
        observer = Observations(tree,state,evidence,parent_only=removal,expanded=settings_fixture)
    existed=set(psutil.pids())
    process = child(command, env=env,text=True,encoding="utf-8",errors="replace",bufsize=1)
    log("engine_start",process.pid)
    messages = queue.Queue()
    threads = []
    for channel,stream in (("stdout",process.stdout),("stderr",process.stderr)):
        def pump(channel=channel,stream=stream):
            for row in stream:
                row=row.rstrip("\r\n")
                log(channel,row)
                messages.put((channel,row))
        thread=threading.Thread(target=pump,daemon=True);thread.start();threads.append(thread)
    workers = WorkerTree(process,log,existed)
    issued = False
    removal_thread = None
    removed = threading.Event()
    evidence_after_remove = {}
    def remove():
        from directory_handles import evidence as observed_handles
        if phase=="idle":
            save(evidence/"directory-handles-before.json",observed_handles(workers,tree))
        # pwsh is already running and has checked that both paths are scratch paths.
        log("remove_go",str(tree))
        out,err=remover.communicate(input="go\n",timeout=45)
        residual=[str(p.relative_to(tree)) for p in tree.rglob("*")] if tree.exists() else []
        ended=now()
        if remover.returncode or tree.exists():
            save(evidence/"directory-handles-after.json",observed_handles(workers,tree))
            if release_roslyn:
                from directory_handles import delete_access
                matches=[(proc,item) for proc,item in tuple(workers.members.values())
                         if proc.is_running() and proc.create_time()==item["created"] and
                         any("jetbrains.roslyn.worker" in a.lower() for a in item["cmd"])
                         and Path(proc.cwd()).resolve()==tree.resolve()]
                if len(matches)!=1:raise ValueError("Expected exactly one owned Roslyn worker with the fixture as cwd")
                roslyn,item=matches[0]
                proof={"before":delete_access(tree),"removed_pid":roslyn.pid,
                       "before_at":now(),"root":str(tree),"engine_before":process.poll()}
                roslyn.kill();roslyn.wait(timeout=5)
                proof.update(after=delete_access(tree),after_at=now(),engine_after=process.poll())
                save(evidence/"release-roslyn.json",proof)
                save(evidence/"handles-after-release.json",observed_handles(workers,tree))
        evidence_after_remove.update(exit=remover.returncode,out=out,err=err,
                    finished=ended,root_exists=tree.exists(),remaining=residual,
                    worktree_list=run("git","-C",REPO,"worktree","list","--porcelain").stdout)
        save(evidence/"removal.json",evidence_after_remove)
        log("remove_end",evidence_after_remove)
        if remover.returncode or tree.exists():
            holders=[]
            for proc,item in list(workers.members.values()):
                try:
                    for entry in proc.open_files():
                        if Path(entry.path).is_relative_to(tree):
                            holders.append({"pid":proc.pid,"path":entry.path,"kind":"file"})
                    cwd=proc.cwd()
                    if Path(cwd).is_relative_to(tree):
                        holders.append({"pid":proc.pid,"path":cwd,"kind":"current directory"})
                except (psutil.NoSuchProcess,psutil.AccessDenied,OSError): pass
            save(evidence/"remaining-open-files.json",holders)
        removed.set()
    def start_remove():
        nonlocal issued, removal_thread
        issued=True
        removal_thread=threading.Thread(target=remove,daemon=True);removal_thread.start()
    deadline=time.monotonic()+150
    start=time.monotonic()
    ready=False
    stopped=False
    exit_sent=False
    stack_capture=None
    stack_files=[]
    def roslyn_loaded():
        markers=list((state/"solution-caches").glob("*/CppSymbolNameCacheCapacity.dat"))
        if not markers:return False
        logpaths=list((state/"logs").rglob("*.log"))
        return (any("Roslyn" in str(p.parent) and "Setup Complete!" in p.read_text(errors="replace") for p in logpaths)
                and any("MsBuildTask" in str(p.parent) and "Reloading has finished" in p.read_text(errors="replace") for p in logpaths))
    try:
        while time.monotonic()<deadline:
            if workers.forbidden:
                save(ROOT/"RESTRICTED-PORT-STOP.json",{"case":case,"engine_pid":process.pid,"listeners":workers.forbidden,
                                           "processes":[v[1] for v in workers.members.values()]})
                stopped=True
                workers.kill_owned()
                break
            if phase=="startup" and not issued and time.monotonic()-start>=.15: start_remove()
            try: channel,row=messages.get(timeout=.03)
            except queue.Empty: channel,row="",""
            if phase=="indexing" and not issued and channel and ("updating caches" in row.lower() or "indexing" in row.lower()):
                start_remove()
            if phase=="indexing" and not issued and stack_capture is None and roslyn_loaded():
                log("indexing_marker","MSBuild reloading finished, Roslyn Setup Complete, symbol-cache capacity file created; before INDEXED")
                stack_files=[(evidence/"engine-stack.txt").open("wb"),(evidence/"stack-stderr.txt").open("wb")]
                stack_capture=subprocess.Popen(["dotnet-stack","report","--process-id",str(process.pid)],
                    cwd=evidence,env=env,creationflags=NO_WINDOW,stdout=stack_files[0],stderr=stack_files[1])
                # Capture the active engine while git starts, with both after the model has loaded.
                time.sleep(.12)
                start_remove()
            if phase=="query" and not issued and row.startswith("PROJECTS "): start_remove()
            if row=="READY" and not ready:
                ready=True
                if phase=="idle" and not issued: start_remove()
                elif phase=="killed":
                    log("kill_engine_only",process.pid)
                    workers.kill_owned(children=False)
                elif not removal:
                    process.stdin.write("exit\n");process.stdin.flush()
            if removed.is_set() and process.poll() is None and now()-evidence_after_remove["finished"]>5:
                if not ready and time.monotonic()-start<25: continue
                if not exit_sent:
                    try: process.stdin.write("exit\n");process.stdin.flush()
                    except (OSError,ValueError): pass
                    exit_sent=True
                if time.monotonic()-start>40: workers.kill_owned(children=False)
            if process.poll() is not None:
                if issued and not removed.is_set(): continue
                break
        if process.poll() is None:
            if not stopped: log("timeout",phase)
            workers.kill_owned(children=False)
        process.wait(timeout=10)
        log("engine_exit",process.returncode)
        for interval in ([.2,1] if stopped else [0,1,4,10]):
            time.sleep(interval);log("survivors",workers.live())
        if stopped:
            workers.kill_owned()
        if removal_thread: removal_thread.join(timeout=45)
        if stack_capture:
            try: stack_capture.wait(timeout=8)
            except subprocess.TimeoutExpired: stack_capture.kill(); stack_capture.wait()
            for f in stack_files: f.close()
        if removal and not issued: remover.communicate(input="abort\n",timeout=5)
        final = {"returncode":process.returncode,"phase":phase,"removed":evidence_after_remove,
                 "root_exists":tree.exists(),"survivors":[x["pid"] for x in workers.live()],
                 "listeners":workers.listen,"ever_seen":[v[1] for v in workers.members.values()]}
        save(evidence/"result.json",final)
        print(json.dumps({k:final[k] for k in ("phase","returncode","root_exists","survivors","listeners")}), flush=True)
    finally:
        workers.kill_owned()
        workers.stop.set();workers.thread.join(timeout=3)
        for thread in threads: thread.join(timeout=1)
        events.close()
        if observer: observer.finish()
        if sentinels:save(evidence/"settings-after.json",settings_state())

if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("case")
    p.add_argument("phase",choices=["startup","indexing","idle","query","normal","killed"])
    p.add_argument("--disable-assets-cache",action="store_true")
    p.add_argument("--release-roslyn",action="store_true",help="Causal control after a failed ordinary idle removal")
    p.add_argument("--settings-fixture",action="store_true",help="Use existing writable, synthetic Rider personal settings files")
    a=p.parse_args()
    if a.release_roslyn and a.phase!="idle":p.error("Use the release control only in an idle removal case.")
    if a.settings_fixture and a.phase!="normal":p.error("Use settings sentinels only in a normal run.")
    probe(a.case,a.phase,a.disable_assets_cache,a.release_roslyn,a.settings_fixture)
