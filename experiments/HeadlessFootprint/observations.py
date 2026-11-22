"""Record metadata before/after an engine run and Win32 directory changes during it.
Only metadata is read outside the scratch folder. Notifications have no writer PID."""
import json
import ctypes
from ctypes import wintypes
import os
import threading
import time
import win32con
import win32file
import pywintypes
from pathlib import Path


def snapshot(directory):
    result, errors = {}, []
    todo = [directory]
    while todo:
        parent = todo.pop()
        try:
            with os.scandir(parent) as entries:
                for entry in entries:
                    try:
                        s = entry.stat(follow_symlinks=False)
                        rel = os.path.relpath(entry.path,directory)
                        if s.st_file_attributes & (win32con.FILE_ATTRIBUTE_REPARSE_POINT | win32con.FILE_ATTRIBUTE_DIRECTORY):
                            if entry.is_dir(follow_symlinks=False) and not s.st_file_attributes & win32con.FILE_ATTRIBUTE_REPARSE_POINT:
                                todo.append(entry.path)
                        else: result[rel] = (s.st_size, s.st_mtime_ns)
                    except OSError as ex: errors.append(str(ex))
        except OSError as ex: errors.append(str(ex))
    return result,errors


class Observations:
    def __init__(self,tree,state,evidence,parent_only=False,expanded=False):
        self.roots={"worktree":tree,"state":state,"cli-environment":state.parents[2]/"environment",
                    "jetbrains-local":Path(os.environ["LOCALAPPDATA"])/"JetBrains",
                    "jetbrains-roaming":Path(os.environ["APPDATA"])/"JetBrains"}
        if parent_only:
            self.roots={"worktrees-parent":tree.parent}
        if expanded:
            for key,env,child in (("local-msbuild","LOCALAPPDATA","Microsoft/MSBuild"),
                                   ("roaming-msbuild","APPDATA","Microsoft/MSBuild"),("roaming-nuget","APPDATA","NuGet"),
                                   ("local-nuget","LOCALAPPDATA","NuGet")):
                root=Path(os.environ[env])/child
                if root.is_dir():self.roots[key]=root
        self.path=evidence
        self.handles=[]
        self.events=[]
        self.threads=[]
        self.stopping=False
        self.before={}
        self.errors={}
        for key,root in self.roots.items():
            self.before[key], self.errors[key]=snapshot(root)
            self.watch(key,root)

    def watch(self,key,root):
        # Used only for footprint cases, never on a worktree under removal.
        h=win32file.CreateFile(str(root),1,win32con.FILE_SHARE_READ|win32con.FILE_SHARE_WRITE|win32con.FILE_SHARE_DELETE,
                               None,win32con.OPEN_EXISTING,win32con.FILE_FLAG_BACKUP_SEMANTICS,None)
        self.handles.append(h)
        def watch():
            try:
                while not self.stopping:
                    changes=win32file.ReadDirectoryChangesW(h,65536,True,1|2|8|16|64,None,None)
                    if not changes: self.events.append((time.time(),key,0,"notification overflow or empty batch"))
                    for action,path in changes:
                        self.events.append((time.time(),key,action,path))
            except pywintypes.error as ex:
                if not self.stopping: self.events.append((time.time(),key,0,str(ex)))
        t=threading.Thread(target=watch,daemon=True);t.start();self.threads.append(t)

    def finish(self):
        self.stopping=True
        # Cancel pending synchronous directory reads on this observer's own threads before closing their handles.
        kernel=ctypes.WinDLL("kernel32",use_last_error=True)
        kernel.OpenThread.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD]
        kernel.OpenThread.restype=wintypes.HANDLE
        kernel.CancelSynchronousIo.argtypes=[wintypes.HANDLE]
        kernel.CloseHandle.argtypes=[wintypes.HANDLE]
        for thread in self.threads:
            while thread.is_alive():
                h=kernel.OpenThread(1,False,thread.native_id)
                if h:
                    kernel.CancelSynchronousIo(h)
                    kernel.CloseHandle(h)
                thread.join(.1)
        for h in self.handles: h.Close()
        results={}
        for key,root in self.roots.items():
            after,errors=snapshot(root)
            before=self.before[key]
            results[key]={"root":str(root),"before_file_count":len(before),"after_file_count":len(after),
                          "before_errors":self.errors[key],"after_errors":errors,
                          "added":{k:after[k] for k in after.keys()-before.keys()},
                          "removed":{k:before[k] for k in before.keys()-after.keys()},
                          "modified":{k:[before[k],after[k]] for k in before.keys() & after.keys() if before[k]!=after[k]}}
        (self.path/"metadata-diff.json").write_text(json.dumps(results,indent=2),encoding="utf-8")
        with (self.path/"filesystem-events.jsonl").open("w",encoding="utf-8") as f:
            for ev in self.events: f.write(json.dumps(ev)+"\n")
