#!/usr/bin/env python3
"""Spec self-consistency checks: re-measure PRD claims against the real artifacts.

Validates the SPEC, not the product. Run: python3 eval/verify_claims.py
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
PRD = (ROOT / "docs/PRD.md").read_text()
MANIFEST = json.loads((ROOT / "eval/golden-manifest.json").read_text())
FIXTURES = [json.loads(f.read_text()) for f in sorted((ROOT / "eval/golden").glob("*.json"))]
fails = []


def check(label, cond):
    if not cond:
        fails.append(label)


# 1. counts in PRD §0 match reality
n_fix, n_beh = len(FIXTURES), len(MANIFEST["behaviors"])
check(f"PRD cites {n_fix} cases", f"({n_fix} cases" in PRD)
check(f"PRD cites {n_beh} behaviors", f"{n_beh} behaviors" in PRD)

# 2. every fixture ID cited in the PRD exists
fix_ids = {f["id"] for f in FIXTURES}
for gid in set(re.findall(r"G-\d{3}", PRD)):
    check(f"cited fixture {gid} exists", gid in fix_ids)

# 3. every DEC cited in requirements exists in the registry
registry = set(re.findall(r"^\| (DEC-\d{3}) \|", PRD, re.M))
for dec in set(re.findall(r"DEC-\d{3}", PRD)):
    check(f"cited {dec} in registry", dec in registry)

# 4. every evidence ID used by fixtures/manifest resolves to PRD registry or DEC
evidence = set(re.findall(r"(BRIEF-\d+|AUD-\d+|DEC-\d{3})", PRD))
used = set()
for f in FIXTURES:
    used.update(f["traces_to"])
for b in MANIFEST["behaviors"]:
    used.update(b["traces_to"])
for e in used:
    check(f"fixture evidence {e} defined in PRD", e in evidence)

# 5. behavior coverage: every behavior covered by >=1 fixture, roles satisfied
for b in MANIFEST["behaviors"]:
    have = {f["case_type"] for f in FIXTURES if b["id"] in f["covers"]}
    check(f"{b['id']} roles {b['required_case_types']}", not (set(b["required_case_types"]) - have))

# 6. requirement index ranges in §2 match R-IDs present
rids = sorted(set(re.findall(r"\*\*R-(\d{2})\*\*", PRD)))
for lo, hi in re.findall(r"R-(\d{2})\.\.R-(\d{2})", PRD):
    present = [r for r in rids if lo <= r <= hi]
    check(f"index range R-{lo}..R-{hi} nonempty", bool(present))
    check(f"index range endpoint R-{hi} exists", hi in rids)

# 7. wireframe screens S1-S7 exist
wf = (ROOT / "docs/ui-wireframes.html").read_text()
for i in range(1, 8):
    check(f"wireframe S{i} present", f"S{i} ·" in wf)

# 8. banned wireframe regressions (fixed at spec review)
check("no host force-start wording", "host may also start solo" not in wf)
check("team spawn on map", "TEAM SPAWN" in wf)

# 9. diagram recoverable + profile store present
r = subprocess.run(
    ["python3", str(Path.home() / ".claude/skills/product-inception/scripts/check_diagram.py"),
     str(ROOT / "docs/architecture.excalidraw"), "--strict"], capture_output=True, text=True)
check("diagram checker passes", r.returncode == 0)
check("diagram has Profile Store", "Profile Store" in r.stdout)

# 10. sub-checks: structural validator + fixture verifier
for cmd in (
    ["python3", str(Path.home() / ".claude/skills/product-inception/scripts/validate_golden.py"), str(ROOT / "eval/golden")],
    ["python3", str(ROOT / "eval/verify_fixtures.py")],
):
    rr = subprocess.run(cmd, capture_output=True, text=True)
    check(f"{Path(cmd[1]).name} passes", rr.returncode == 0)

if fails:
    print("FAIL")
    for f in fails:
        print(" -", f)
    sys.exit(1)
print(f"OK — spec claims verified: {n_fix} fixtures, {n_beh} behaviors, registry/citations/diagram consistent")
