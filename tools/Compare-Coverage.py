"""Compare executable source lines and method branch counts in two Cobertura files."""
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def read_report(path):
    report = ET.parse(path).getroot()
    sources = [source.text.replace('\\', '/').rstrip('/') for source in report.findall('./sources/source')]
    lines, branches = {}, {}
    for cls in report.findall('./packages/package/classes/class'):
        filename = cls.get('filename').replace('\\', '/')
        for source in sources:
            if filename.lower().startswith(source.lower() + '/'):
                filename = filename[len(source) + 1:]
                break
        # Absolute source names should also compare across recovery worktrees.
        for directory in ('scripts/', 'scenes/', 'addons/'):
            marker = '/' + directory
            if marker in filename:
                filename = filename[filename.index(marker) + 1:]
                break
        for line in cls.findall('./lines/line'):
            key = (filename, int(line.get('number')))
            lines[key] = lines.get(key, False) or int(line.get('hits')) > 0
        for method in cls.findall('./methods/method'):
            for line in method.findall('./lines/line'):
                match = re.search(r'\((\d+)/(\d+)\)', line.get('condition-coverage', ''))
                if match:
                    key = (filename, cls.get('name'), method.get('name'), method.get('signature'), int(line.get('number')))
                    branches[key] = tuple(map(int, match.groups()))
    return report.attrib, lines, branches


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('before')
parser.add_argument('after')
parser.add_argument('--output', required=True)
args = parser.parse_args()
before, old_lines, old_branches = read_report(args.before)
after, new_lines, new_branches = read_report(args.after)
lost = sorted(k for k, hit in old_lines.items() if hit and not new_lines.get(k, False))
gained = sorted(k for k, hit in new_lines.items() if hit and not old_lines.get(k, False))
changed_branches = [dict(file=k[0], cls=k[1], method=k[2], signature=k[3], line=k[4],
                         before=old_branches.get(k), after=new_branches.get(k))
                    for k in sorted(old_branches.keys() | new_branches.keys())
                    if old_branches.get(k) != new_branches.get(k)]
result = dict(
    collectorBefore=before, collectorAfter=after,
    distinctSourceLinesBefore=dict(covered=sum(old_lines.values()), valid=len(old_lines)),
    distinctSourceLinesAfter=dict(covered=sum(new_lines.values()), valid=len(new_lines)),
    lostLines=[dict(file=f, line=n) for f, n in lost],
    gainedLines=[dict(file=f, line=n) for f, n in gained],
    changedMethodBranches=changed_branches,
)
Path(args.output).write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in result.items() if k not in ('collectorBefore', 'collectorAfter', 'changedMethodBranches')}, indent=2))
