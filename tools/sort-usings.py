"""Sortiert using-Direktiven (System zuerst, dann alphabetisch, Aliase zuletzt): python3 tools/sort-usings.py <dateien>"""
import sys

for path in sys.argv[1:]:
    lines = open(path, encoding="utf-8").read().split("\n")
    first = next(i for i, l in enumerate(lines) if l.startswith("using "))
    last = max(i for i, l in enumerate(lines) if l.startswith("using ") and "(" not in l and i < first + 60)
    block = [l for l in lines[first:last + 1] if l.startswith("using ")]
    aliases = sorted([u for u in block if "=" in u], key=str.lower)
    plain = [u for u in block if "=" not in u]
    system = sorted([u for u in plain if u.startswith("using System")], key=lambda u: u.rstrip(";").lower())
    other = sorted([u for u in plain if not u.startswith("using System")], key=lambda u: u.rstrip(";").lower())
    lines[first:last + 1] = system + other + aliases
    open(path, "w", encoding="utf-8").write("\n".join(lines))
