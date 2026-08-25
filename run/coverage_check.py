#!/usr/bin/env python3
"""Assert two-way coverage between run/tickets.json and the specs.

Reads requirement ids from docs/PRD.md and fixture ids from eval/golden/*.json
at run time -- never from a hardcoded list -- and asserts:
  1. every R-id in the PRD is owned by >= 1 ticket
  2. every G-id in eval/golden is graded by >= 1 ticket
  3. no ticket cites an R-id or G-id that does not exist
  4. ticket ids are unique and every depends_on points at a real ticket
Exit 0 on success, 1 on any violation.
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent


def prd_requirement_ids():
    text = (ROOT / "docs" / "PRD.md").read_text()
    # Requirements are defined as bold list markers: - **R-01** ...
    return set(re.findall(r"\*\*(R-\d{2})\*\*", text))


def fixture_ids():
    ids = set()
    for path in sorted((ROOT / "eval" / "golden").glob("*.json")):
        ids.add(json.loads(path.read_text())["id"])
    return ids


def main():
    tickets = json.loads((ROOT / "run" / "tickets.json").read_text())["tickets"]
    reqs, fixtures = prd_requirement_ids(), fixture_ids()
    problems = []

    ticket_ids = [t["id"] for t in tickets]
    for dup in {i for i in ticket_ids if ticket_ids.count(i) > 1}:
        problems.append(f"duplicate ticket id {dup}")

    owned, graded = {}, {}
    for t in tickets:
        for r in t["owns_requirements"]:
            owned.setdefault(r, []).append(t["id"])
            if r not in reqs:
                problems.append(f"{t['id']} owns {r}, which is not a requirement in docs/PRD.md")
        for g in t["grades_fixtures"]:
            graded.setdefault(g, []).append(t["id"])
            if g not in fixtures:
                problems.append(f"{t['id']} grades {g}, which is not a fixture in eval/golden/")
        for dep in t.get("depends_on", []):
            if dep not in ticket_ids:
                problems.append(f"{t['id']} depends on {dep}, which is not a ticket")

    for r in sorted(reqs - set(owned)):
        problems.append(f"requirement {r} is owned by no ticket")
    for g in sorted(fixtures - set(graded)):
        problems.append(f"fixture {g} is graded by no ticket")

    if problems:
        print(f"FAIL — {len(problems)} coverage problem(s):")
        for p in problems:
            print(f"  - {p}")
        return 1

    print(
        f"OK — {len(tickets)} tickets; {len(reqs)} requirements all owned; "
        f"{len(fixtures)} fixtures all graded; no dangling ids"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
