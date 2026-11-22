"""Read named file handles of this run's process identities with Windows Process Snapshotting."""
import ctypes as c
from ctypes import wintypes as w
from pathlib import Path
import time
import win32file, win32con, pywintypes
import psutil

k=c.WinDLL("kernel32",use_last_error=True)
def api(name, result, *args):
    fun=getattr(k,name);fun.restype=result;fun.argtypes=args;return fun
open_process=api("OpenProcess",w.HANDLE,w.DWORD,w.BOOL,w.DWORD)
close=api("CloseHandle",w.BOOL,w.HANDLE)
capture=api("PssCaptureSnapshot",w.DWORD,w.HANDLE,w.DWORD,w.DWORD,c.POINTER(w.HANDLE))
make_marker=api("PssWalkMarkerCreate",w.DWORD,c.c_void_p,c.POINTER(w.HANDLE))
walk=api("PssWalkSnapshot",w.DWORD,w.HANDLE,c.c_int,w.HANDLE,c.c_void_p,w.DWORD)
free_marker=api("PssWalkMarkerFree",w.DWORD,w.HANDLE)
free_snap=api("PssFreeSnapshot",w.DWORD,w.HANDLE,w.HANDLE)
current=api("GetCurrentProcess",w.HANDLE)
duplicate=api("DuplicateHandle",w.BOOL,w.HANDLE,w.HANDLE,w.HANDLE,c.POINTER(w.HANDLE),w.DWORD,w.BOOL,w.DWORD)
file_type=api("GetFileType",w.DWORD,w.HANDLE)
file_path=api("GetFinalPathNameByHandleW",w.DWORD,w.HANDLE,w.LPWSTR,w.DWORD,w.DWORD)
class Entry(c.Structure):
    _fields_=[("Handle",w.HANDLE),("Flags",w.DWORD),("ObjectType",w.DWORD),("CaptureTime",w.FILETIME),
              ("Attributes",w.DWORD),("GrantedAccess",w.DWORD),("HandleCount",w.DWORD),("PointerCount",w.DWORD),
              ("PagedPoolCharge",w.DWORD),("NonPagedPoolCharge",w.DWORD),("CreationTime",w.FILETIME),
              ("TypeNameLength",w.WORD),("TypeName",c.c_void_p),("ObjectNameLength",w.WORD),("ObjectName",c.c_void_p),
              ("TypeSpecificInformation",c.c_byte*48)]  # x64 Windows SDK PSS_HANDLE_ENTRY union, 136 bytes in all
def string(p,n): return c.wstring_at(p,n//2).rstrip("\0") if p and n else ""

def delete_access(path):
    try:
        h=win32file.CreateFile(str(path),0x10000,7,None,win32con.OPEN_EXISTING,
                               win32con.FILE_FLAG_BACKUP_SEMANTICS,None);h.Close()
        return "granted"
    except pywintypes.error as ex:
        return f"{ex.winerror}: {ex.strerror}"

def handles(proc,item,root):
    if not proc.is_running() or proc.create_time()!=item["created"]:return {"gone":True}
    result={"pid":proc.pid,"name":item["name"],"paths":[]}
    try:result["cwd"]=proc.cwd()
    except (psutil.Error,OSError) as ex:result["cwd_error"]=str(ex)
    h=open_process(0x0400|0x0040|0x0010,False,proc.pid)
    if not h: return {**result,"error":c.get_last_error()}
    snap=w.HANDLE();marker=w.HANDLE()
    try:
        error=capture(h,4|8|16,0,c.byref(snap))
        if error:return {**result,"capture_error":error}
        error=make_marker(None,c.byref(marker))
        if error:return {**result,"marker_error":error}
        suffix=str(root)[2:].lower()
        count=0
        while True:
            entry=Entry()
            error=walk(snap,2,marker,c.byref(entry),c.sizeof(entry))
            if error==259:break
            if error: return {**result,"walk_error":error}
            count+=1
            name=string(entry.ObjectName,entry.ObjectNameLength)
            kind=string(entry.TypeName,entry.TypeNameLength)
            # PSS has no names for many File objects. Query opened paths of disk handles only.
            if not name and (kind in ("", "File")):
                local=w.HANDLE()
                if duplicate(h,entry.Handle,current(),c.byref(local),0,False,0):
                    try:
                        if file_type(local)==1:
                            buffer=c.create_unicode_buffer(32768)
                            length=file_path(local,buffer,len(buffer),8)
                            if 0<length<len(buffer):name=buffer.value;kind="File"
                    finally:close(local)
            off=name.lower().find(suffix)
            if off>=0 and (len(name)==off+len(suffix) or name[off+len(suffix)]=="\\"):
                result["paths"].append({"path":name,"handle":entry.Handle,"type":kind,
                                        "access":hex(entry.GrantedAccess)})
        result["handles_walked"]=count
        return result
    finally:
        if marker:free_marker(marker)
        if snap:free_snap(current(),snap)
        close(h)
def evidence(workers,root):
    return {"at":time.time(),"delete_access":delete_access(root),
            "processes":[handles(proc,item,root) for proc,item in tuple(workers.members.values())]}

if __name__=="__main__":
    # This process' own open, non-delete-shared scratch directory handle tests both names and access.
    import os
    p=Path(__file__).resolve().parent/"environment"
    h=win32file.CreateFile(str(p),1,3,None,3,win32con.FILE_FLAG_BACKUP_SEMANTICS,None)
    me=psutil.Process()
    result=handles(me,{"created":me.create_time(),"name":me.name()},p)
    h.Close()
    print(result)
