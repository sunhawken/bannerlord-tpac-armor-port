"""venice_areola.py - ask for the Venice.ai API key in a popup (kept in memory only, never written to disk / logs), generate a macro skin-detail swatch with Venice,
and save ONLY the resulting image as ../enginesim/tex/areola_detail.png.  gem_mask.py then paints a larger, very dark areola with that detail onto her atlas.
usage: python venice_areola.py            (popup asks for the key)
       VENICE_API_KEY=... python venice_areola.py   (skips the popup)"""
import base64, io, json, os, sys, urllib.request, urllib.error
from PIL import Image

API = 'https://api.venice.ai/api/v1'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'enginesim', 'tex', 'areola_detail.png')


def ask_key():
    k = os.environ.get('VENICE_API_KEY')
    if k: return k.strip()
    import tkinter as tk
    from tkinter import simpledialog
    root = tk.Tk(); root.withdraw(); root.attributes('-topmost', True)
    k = simpledialog.askstring('Venice.ai API key', 'Paste your Venice.ai API key (used once, in memory only):', show='*', parent=root)
    root.destroy()
    if not k: sys.exit('no key entered')
    return k.strip()


def call(path, key, body=None):
    req = urllib.request.Request(API + path, data=None if body is None else json.dumps(body).encode(), method='GET' if body is None else 'POST',
                                 headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json', 'User-Agent': 'areola-texture-tool/1.0'})
    try:
        with urllib.request.urlopen(req, timeout=180) as r: return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        sys.exit(f'Venice HTTP {e.code}: {e.read().decode()[:300]}')


key = ask_key()
models = call('/models?type=image', key)
ids = [m['id'] for m in models.get('data', [])]
print('image models available:', ids)
pick = next((i for i in ids if 'lustify' in i), None) or next((i for i in ids if 'uncensored' in i), None) or next((i for i in ids if 'flux' in i), None) or (ids[0] if ids else None)
if not pick: sys.exit('no image model available on this key')
print('using model:', pick)
prompt = ('seamless macro skin texture swatch, flat top-down orthographic view, very dark charcoal-black skin with a large round very dark areola circle '
          'and a small softly raised nipple at its centre, fine pores and subtle mottling, matte, even lighting, no person, no background, game texture')
neg = 'text, watermark, face, hands, jewelry, bright colours, pink, red, blur'
body = {'model': pick, 'prompt': prompt, 'negative_prompt': neg, 'width': 1024, 'height': 1024, 'format': 'png', 'safe_mode': False, 'hide_watermark': True, 'return_binary': False}
res = call('/image/generate', key, body)
img = Image.open(io.BytesIO(base64.b64decode(res['images'][0]))).convert('RGB')
os.makedirs(os.path.dirname(OUT), exist_ok=True); img.save(OUT)
print('saved detail swatch', img.size, '->', os.path.normpath(OUT))
