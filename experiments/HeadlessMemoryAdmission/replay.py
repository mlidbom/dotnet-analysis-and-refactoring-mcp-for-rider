"""Replay recorder ten-second summaries; output endpoint decisions and intervals in local time."""
import argparse, collections, csv, datetime as dt, json, pathlib, re

def timestamp(text):
    return dt.datetime.fromisoformat(text)

def replay(rows, physical_mib, window, use_commit, thresholds, nominal_samples=False):
    previous_end = None
    green_samples = 0
    green_since = None
    for row in rows:
        start, end = map(timestamp, (row["from"], row["to"]))
        gap = previous_end is None or (start - previous_end).total_seconds() > 5 or start <= previous_end
        if gap:
            green_samples = 0
            green_since = None
        physical = 100 * (1 - row['leastAvailableMB'] / physical_mib)
        commit = 100 * row['mostCommittedGB'] / row['commitLimitGB']
        tight_physical, tight_commit = physical >= thresholds['MemoryYellowPercent'], commit >= thresholds['CommitRedPercent']
        pressure = tight_physical or (use_commit and tight_commit)
        green_samples = 0 if pressure else green_samples + row['samples']
        green_since = None if pressure else (green_since or start)
        green_seconds = (end-green_since).total_seconds() if green_since else 0
        enough_history = (green_samples if nominal_samples else green_seconds) >= window
        state = ("pressure" if pressure else "admit" if enough_history else "accumulating-green")
        yield dict(start=start,end=end, samples=row['samples'],gap=gap,phys=physical,commit=commit,
                   avail=row['leastAvailableMB'],committed=row['mostCommittedGB'],limit=row['commitLimitGB'],
                   tight_physical=tight_physical,tight_commit=tight_commit,state=state,green_samples=green_samples,green_seconds=green_seconds)
        previous_end = end

def runs(results, key):
    groups = []
    for row in results:
        decision = key(row)
        if not groups or row['gap'] or decision != groups[-1][0]: groups.append((decision,[]))
        groups[-1][1].append(row)
    return groups

def stats(rows):
    def count(key): return sum(r['samples'] for r in rows if key(r))
    return dict(buckets=len(rows), samples=count(lambda r:True), states=dict(collections.Counter(r['state'] for r in rows)),
                rough_blocked_sample_seconds=count(lambda r:r['state']!='admit'),
                physical_pressure_buckets=sum(r['tight_physical'] for r in rows),
                commit_only_buckets=sum(r['tight_commit'] and not r['tight_physical'] for r in rows),
                both_pressure_buckets=sum(r['tight_commit'] and r['tight_physical'] for r in rows),
                min_available_mib=min(r['avail'] for r in rows), max_commit_ratio=max(r['commit'] for r in rows))

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('log',type=pathlib.Path)
    ap.add_argument('--physical-mib',required=True,type=float,help='Usable RAM for the logged machine and day')
    ap.add_argument('--thresholds-file',type=pathlib.Path,default=pathlib.Path.home()/'.claude/src/MachineStalls/Warning/WarningThresholds.cs')
    ap.add_argument('--output',type=pathlib.Path,default=pathlib.Path.cwd())
    a=ap.parse_args()
    data=[json.loads(line) for line in a.log.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
    source=a.thresholds_file.read_text(encoding='utf-8-sig')
    thresholds={}
    for name in ('MemoryYellowPercent','CommitRedPercent'):
        match=re.search(r'public double '+name+r'\s*\{[^}]+\}\s*=\s*([\d.]+);',source)
        if not match: raise ValueError(f"Check recorder source: unable to read {name} from {a.thresholds_file}")
        thresholds[name]=float(match.group(1))
    all_rows=list(replay(data,a.physical_mib,30,True,thresholds))
    with (a.output/'replay-buckets.csv').open('w',newline='',encoding='utf8') as f:
        w=csv.DictWriter(f,fieldnames=list(all_rows[0]));w.writeheader();w.writerows(all_rows)
    # No state is imputed between the recorded intervals.
    with (a.output/'refusal-intervals.csv').open('w',newline='',encoding='utf8') as f:
        w=csv.writer(f);w.writerow(['first_refused_endpoint','last_refused_endpoint','next_observed_admitted_endpoint','refused_checks','pressure_checks','recovering_checks'])
        groups=runs(all_rows,lambda r:r['state']!='admit')
        for i,(refused,items) in enumerate(groups):
            if not refused: continue
            next_time=groups[i+1][1][0]['end'] if i+1<len(groups) and not groups[i+1][0] and not groups[i+1][1][0]['gap'] else ''
            w.writerow([items[0]['end'],items[-1]['end'],next_time,len(items),sum(r['state']=='pressure' for r in items),sum(r['state']=='accumulating-green' for r in items)])
    def select(rs, lo, hi): return [r for r in rs if lo<=r['end'].hour<hi]
    report={}
    for name,lo,hi in [('day',0,24),('morning',8,9),('evening',19,21)]:
        rs=select(all_rows,lo,hi)
        result=stats(rs)
        result['windows']={}
        result['nominal_30']=sum(r['state']!='admit' for r in select(list(replay(data,a.physical_mib,30,True,thresholds,True)),lo,hi))
        for use_commit in (True,False):
            for window in (0,10,30,60,120):
                vals=select(list(replay(data,a.physical_mib,window,use_commit,thresholds)),lo,hi)
                result['windows'][f"{'both' if use_commit else 'ram'}_{window}"]=sum(r['state']!='admit' for r in vals)
        result['refused']=[(g[0]['end'].isoformat(),g[-1]['end'].isoformat(),len(g)) for denied,g in runs(rs,lambda r:r['state']!='admit') if denied]
        result['worst_free']=min(rs,key=lambda r:r['avail'])
        result['worst_commit']=max(rs,key=lambda r:r['commit'])
        result['brief_green_runs']=[(g[0]['start'].isoformat(),sum(r['samples'] for r in g)) for good,g in runs(rs,lambda r:not r['tight_commit'] and not r['tight_physical']) if good and sum(r['samples'] for r in g)<=120]
        report[name]=result
    report['gaps']=[(prev['end'],row['start']) for prev,row in zip(all_rows,all_rows[1:]) if row['gap']]
    report['thresholds']=thresholds
    (a.output/'replay-results.json').write_text(json.dumps(report,default=str,indent=2),encoding='utf8')
if __name__=='__main__':main()
