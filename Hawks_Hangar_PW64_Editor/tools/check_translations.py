"""Checks the language files of the editor (src/PW64Editor.App/Languages/*.json).

Collects every English text of the editor's controls (XAML attributes, L.T/L.F/L.K calls in the
code, the text categories) and of the messages of the core library (CoreText.T/F/K calls), and
lists, per language file, the texts that have no translation yet, the translations that are no
longer used and the translations whose placeholders ({0}, {1:N0}, ...) differ from the English
text. Run from any folder:

    python tools/check_translations.py
"""
import glob
import json
import os
import re
import xml.etree.ElementTree as ET

src = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src')
root = os.path.join(src, 'PW64Editor.App')
core = os.path.join(src, 'PW64Editor.Core')
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
keys={}
def add(k,src):
    if k and re.search('[A-Za-z]',k) and k != "Hawk's Hangar": keys.setdefault(k,src)
for f in glob.glob(os.path.join(root, '**', '*.xaml'), recursive=True):
    t=ET.parse(f).getroot()
    for el in t.iter():
        for a in ('Text','Content','Header','Title','ToolTip'):
            v=el.get(a)
            if v is not None and not v.startswith('{'): add(v, os.path.basename(f))
        for a,v in el.attrib.items():
            for m in re.finditer(r"ConverterParameter='([^']*)'",v): add(m.group(1), os.path.basename(f))
lit=r'"(?:[^"\\]|\\.)*"'
def unesc(s):
    return s[1:-1].encode('utf-8').decode('unicode_escape').encode('latin1').decode('utf-8')
for f in glob.glob(os.path.join(root, '**', '*.cs'), recursive=True) + glob.glob(os.path.join(core, '**', '*.cs'), recursive=True):
    if os.sep + 'obj' + os.sep in f or os.sep + 'bin' + os.sep in f: continue
    s=open(f).read()
    for m in re.finditer(r'\b(?:L|CoreText)\.(?:T|F|K|TIn)\(\s*(?:[A-Za-z_.]+\s*,\s*)?(' + lit + r'(?:\s*\+\s*' + lit + r')*)', s):
        parts=re.findall(lit, m.group(1)); add(''.join(unesc(p) for p in parts), os.path.basename(f))
    for m in re.finditer(r'ConfirmUnsavedChangesAsync\((' + lit + r')\)', s): add(unesc(m.group(1)), 'actions')
extra=["Bronze","Silver","Gold","Menus and screens","In flight","Badges","Other","Not assigned yet","Levels","Tutorial","Score sheets",
"Title screen","File select","Options","Pilot select","Pilot names","Vehicle and class select","Test summary","Results","Replay and pause menu","Photos","Ending",
"In-flight messages","Rings, targets and balloons","General messages","Vehicle names","Pause menu","Pause menu with photo option",
"name","description","hint","Perfect score message","Perfect score requirement","Gold badge requirement","Silver badge requirement","Bronze badge requirement",
"Level {0} {1}","Mission {0} {1}","Tutorial page {0}","{0} mission {1}, sheet {2}","{0}, sheet {1}","Score sheet {0}, all levels","Score sheet {0}, level {1}","Message {0}",
"Custom texts","Added text, not used by the game yet","Bonus games",
"creating a new project","opening another project","closing the project","closing","opening the game files","opening the restore points","exporting the patch"]
for e in extra: add(e,'catalog')

for path in sorted(glob.glob(os.path.join(root, 'Languages', '*.json'))):
    table = json.load(open(path, encoding='utf-8'))
    missing = [k for k in keys if not table.get(k)]
    unused = [k for k in table if k not in keys]
    placeholder = re.compile(r'\{\d+(?::[^}]*)?\}')
    wrong = [k for k in table if sorted(placeholder.findall(k)) != sorted(placeholder.findall(table[k]))]
    print(f'{os.path.basename(path)}: {len(missing)} missing, {len(unused)} unused, {len(wrong)} with wrong placeholders')
    for k in missing:
        print('  missing:', repr(k))
    for k in unused:
        print('  unused: ', repr(k))
    for k in wrong:
        print('  wrong placeholders:', repr(k))
