"""Pflegt Strings.resx (en) und Strings.de.resx aus strings.tsv (key<TAB>en<TAB>de)."""
import html, pathlib

root = pathlib.Path(__file__).resolve().parent.parent / "src/VsLike.Core/Localization"
rows = [l.split("\t") for l in (root / "strings.tsv").read_text(encoding="utf-8").splitlines() if l.strip() and not l.startswith("#")]
header = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'''
for idx, name in ((1, "Strings.resx"), (2, "Strings.de.resx")):
    body = "".join(f'  <data name="{html.escape(r[0])}" xml:space="preserve"><value>{html.escape(r[idx])}</value></data>\n' for r in rows)
    (root / name).write_text(header + body + "</root>\n", encoding="utf-8")
print(f"{len(rows)} strings")
