"""Merge Cobertura reports and enforce the Core/Rendering line-coverage gates."""
from collections import defaultdict
from pathlib import Path
import glob
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
THRESHOLDS = {
    "Penelopa.Core": 0.90,
    "Penelopa.Rendering": 0.85,
}
# New simulation code must not hide behind the assembly average. Prefixes are
# matched against the normalized coverage filename.
PER_FILE_THRESHOLDS = {
    ("Penelopa.Core", "Simulation/"): 0.90,
    ("Penelopa.Core", "Services/DocumentSerializer.cs"): 0.90,
}

report_paths = glob.glob(str(ROOT / "artifacts" / "coverage" / "**" / "coverage.cobertura.xml"), recursive=True)
if not report_paths:
    print("No coverage.cobertura.xml files found under artifacts/coverage", file=sys.stderr)
    sys.exit(1)

# A line can appear in several test assemblies; max(hits) is enough for a
# union-style line coverage gate.
line_hits = {}
for report_path in report_paths:
    root = ET.parse(report_path).getroot()
    for package in root.iter("package"):
        assembly = package.get("name")
        if assembly not in THRESHOLDS:
            continue
        for class_element in package.iter("class"):
            # Different test projects report paths with or without the
            # assembly directory prefix; normalize so the same source line
            # is merged only once.
            filename = (class_element.get("filename") or "").replace("\\", "/")
            filename = re.sub(r"^(Penelopa\.Core|Penelopa\.Rendering|Penelopa)/", "", filename)
            for line in class_element.iter("line"):
                number = int(line.get("number"))
                hits = int(line.get("hits", "0"))
                key = (assembly, filename, number)
                line_hits[key] = max(line_hits.get(key, 0), hits)

stats = defaultdict(lambda: [0, 0])
file_stats = defaultdict(lambda: [0, 0])
for (assembly, filename, _number), hits in line_hits.items():
    stats[assembly][1] += 1
    file_stats[(assembly, filename)][1] += 1
    if hits > 0:
        stats[assembly][0] += 1
        file_stats[(assembly, filename)][0] += 1

failed = False
for assembly, threshold in THRESHOLDS.items():
    covered, total = stats.get(assembly, [0, 0])
    rate = covered / total if total else 0.0
    status = "ok" if rate + 1e-9 >= threshold else "FAIL"
    print(f"{assembly}: {rate:.2%} line coverage ({covered}/{total}), required {threshold:.0%} -> {status}")
    if rate + 1e-9 < threshold:
        failed = True

for (assembly, prefix), threshold in PER_FILE_THRESHOLDS.items():
    matched = [
        (filename, counts)
        for (candidate_assembly, filename), counts in sorted(file_stats.items())
        if candidate_assembly == assembly
        and (filename == prefix or filename.startswith(prefix))
    ]
    for filename, (covered, total) in matched:
        rate = covered / total if total else 0.0
        status = "ok" if rate + 1e-9 >= threshold else "FAIL"
        print(f"  {filename}: {rate:.2%} ({covered}/{total}), required {threshold:.0%} -> {status}")
        if rate + 1e-9 < threshold:
            failed = True

if failed:
    sys.exit(1)
