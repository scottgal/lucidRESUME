"""Inspect agreement and errors; do not hide disagreement behind an average score."""
import json
import statistics
import sys

for path in sys.argv[1:]:
    report = json.load(open(path))
    rows = report["rows"]
    correct = sum(row["expected"] == row["predicted"] for row in rows)
    false_support = [row["id"] for row in rows if row["expected"] != "entailment" and row["predicted"] == "entailment"]
    label = "false coverage" if report.get("task") == "coverage" else "false support"
    model = report.get("model") or (rows[0].get("model") if rows else None)
    name = report["provider"] + (f" ({model})" if model else "")
    print(f"{name}: {correct}/{len(rows)} relation matches; {label}: {false_support}")
    if report.get("task") == "coverage":
        presence = sum((row["expected"] == "entailment") == (row["predicted"] == "entailment") for row in rows)
        print(f"  Coverage hypothesis supported vs unsupported: {presence}/{len(rows)}")
    print(f"  Median per-case time: {statistics.median(row['elapsedMs'] for row in rows):.0f}ms; {report['timing']}")
    if rows and rows[0].get("action") is not None:
        print(f"  Actions: {sum(row['expectedAction'] == row['action'] for row in rows)}/{len(rows)}")
    for row in rows:
        if row["expected"] != row["predicted"]:
            print(f"  {row['id']}: expected {row['expected']}, got {row['predicted']}")
