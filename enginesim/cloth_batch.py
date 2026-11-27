"""cloth_batch.py <label> "name:ZEROLIST;name2:ZEROLIST;..."  - pack N cloned-dress variants (different cook-blob mutations), run ONE game session that swaps her body item per variant and slides/stops her,
then build out/cb_<label>.png (one row of 4 frames per variant).  ZEROLIST = comma list of cook arrays to zero (A..I, aabb, restlen, edges, sets, batches2) or '-' for none."""
import os, re, subprocess, sys, glob
from PIL import Image, ImageDraw
HERE = os.path.dirname(os.path.abspath(__file__)); TOOL = os.path.join(HERE, '..', 'src', 'tool'); MOD = os.path.join(HERE, 'CirithSim', 'module')
label = sys.argv[1]; specs = [x.split(':') for x in sys.argv[2].split(';') if x]
for f in glob.glob(os.path.join(MOD, 'AssetPackages', 'cloth_var_*.tpac')) + [os.path.join(MOD, 'AssetPackages', 'cloth_test.tpac')]:
    if os.path.exists(f): os.remove(f)
items = []
G = r'S:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\AssetPackages'
for i, (name, z) in enumerate(specs):
    env = dict(os.environ); env['CK_ZERO'] = '' if z == '-' else z; env['CK_PATCH'] = ''
    out = os.path.join(MOD, 'AssetPackages', f'cloth_var_{i}.tpac')
    r = subprocess.run(['dotnet', os.path.join(TOOL, 'bin', 'Release', 'net8.0', 'tool.dll'), G, 'clothclone', out, f'cirith_clothvar_{i}'], env=env, capture_output=True, text=True, cwd=TOOL)
    print(i, name, 'zero=', z, '->', 'ok' if 'wrote' in r.stdout else r.stdout[-200:] + r.stderr[-200:])
    items.append(f'  <Item id="cirith_dressvar_{i}" name="Cloth Var {i}" mesh="cirith_clothvar_{i}" culture="Culture.vlandia" subtype="body_armor" weight="3" difficulty="0" appearance="1" Type="BodyArmor">\n    <ItemComponent><Armor body_armor="10" leg_armor="5" covers_body="true" covers_legs="true" material_type="Cloth" /></ItemComponent>\n    <Flags />\n  </Item>')
ip = os.path.join(MOD, 'ModuleData', 'cirith_items.xml'); x = open(ip).read()
x = re.sub(r'  <!-- CLOTHVARS -->.*?  <!-- /CLOTHVARS -->\n', '', x, flags=re.S); x = x.replace('</Items>', '  <!-- CLOTHVARS -->\n' + '\n'.join(items) + '\n  <!-- /CLOTHVARS -->\n</Items>'); open(ip, 'w').write(x)
env = dict(os.environ); env['CIRITH_MOTION'] = '1'; env['CIRITH_VARIANTS'] = ','.join(f'cirith_dressvar_{i}' for i in range(len(specs)))
subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(HERE, 'run_engine_sim.ps1'), '-Char', 'cirith_t_dress', '-Timeout', str(120 + 12 * len(specs))], env=env, cwd=HERE, capture_output=True)
rows = []
for i, (name, z) in enumerate(specs):
    fr = [os.path.join(HERE, 'out', 'shots', f'v{i:02d}_{k}.png') for k in range(4)]
    if all(os.path.exists(f) for f in fr): rows.append((name, [Image.open(f).crop((640, 150, 1280, 750)).resize((300, 170)) for f in fr]))
    else: print('missing frames for', name)
sheet = Image.new('RGB', (1200 + 160, 170 * max(len(rows), 1)), (20, 20, 20)); d = ImageDraw.Draw(sheet)
for r, (name, fr) in enumerate(rows):
    d.text((6, r * 170 + 6), name, fill=(255, 255, 0))
    for c, im in enumerate(fr): sheet.paste(im, (160 + c * 300, r * 170))
sheet.save(os.path.join(HERE, 'out', f'cb_{label}.png')); print('sheet ->', f'out/cb_{label}.png', len(rows), 'variants')
