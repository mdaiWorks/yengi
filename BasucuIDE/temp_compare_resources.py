import xml.etree.ElementTree as ET
from pathlib import Path

base = Path(r'C:/Users/YILDIZ/Desktop/mdaiAgnet/Resources')
tr = {e.attrib.get('name') for e in ET.parse(str(base / 'Strings.resx')).getroot().findall('data') if e.attrib.get('name')}
en = {e.attrib.get('name') for e in ET.parse(str(base / 'Strings.en.resx')).getroot().findall('data') if e.attrib.get('name')}
missing = sorted(tr - en)
extra = sorted(en - tr)
print(f'missing_in_en={len(missing)}')
for key in missing[:300]:
    print(key)
print(f'extra_in_en={len(extra)}')
for key in extra[:100]:
    print(key)
