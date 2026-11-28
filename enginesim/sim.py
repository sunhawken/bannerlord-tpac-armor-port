#!/usr/bin/env python3
"""sim.py - machine-oriented control tool for the persistent Bannerlord engine sim (built for Claude / scripts; humans do not need it).

Every call prints ONE JSON document on stdout and exits 0 (ok) / 1 (a check failed or a command errored) / 2 (sim not running / bad usage). No prompts, no interactive state.

  sim.py status                         -> {"running":true,"ready":true,"pid":...,"uptime_s":...}
  sim.py up [--char C] [--modules M]    boot the sim ONCE on a hidden desktop (~70 s), returns when it answers ping
  sim.py down                           stop it
  sim.py run "state; script on; wield; wait 1; bring 1.3; freeze; attack right 1.2; wait 1.5; hp"
                                        run commands in order (client-side `wait <s>`), results = list of {cmd, ok, text[, file]}; `shot <name> ...` returns the png path
  sim.py test <suite|all> [--keep]      scripted checks with evidence; suites: smoke sprint voice jump attacks hit give   (boots the sim first if it is down, `--keep` leaves it up)
  sim.py log [--tail N] [--grep RE]     lines of Modules\\CirithSim\\cirithsim.log
  sim.py build [--restart]              dotnet build the DLL (+ restart the sim so the new DLL is loaded)
  sim.py cmds                           list the engine-side commands

Engine-side commands (src/SimCommands.cs): ping state script[on|off] cam az el dist h | camoff | shot name [az el dist h] | move forward|back|left|right sec | attack right|left|up|down sec | block sec |
stop | jump | sprint [off] | wield [off] | give | voice <Type> | bring dist | freeze [n] | heal | hp | reload | log text | quit
"""
import os, sys, json, time, re, subprocess, glob, argparse

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = r'T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord'
MOD = os.path.join(GAME, 'Modules', 'CirithSim')
CMD, OUT, READY, LOG = (os.path.join(MOD, n) for n in ('simcmd.txt', 'simout.txt', 'simready.txt', 'cirithsim.log'))
SHOTS = os.path.join(HERE, 'out', 'shots')
DEFAULT_MODULES = 'Bannerlord.Harmony*Native*SandBoxCore*CustomBattle*CirithSim'


def emit(obj, code=0):
    print(json.dumps(obj, ensure_ascii=False, indent=None, separators=(',', ':')))
    sys.exit(code)


def pids():
    r = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq Bannerlord.exe', '/FO', 'CSV', '/NH'], capture_output=True, text=True)
    return [int(l.split('","')[1]) for l in r.stdout.splitlines() if l.startswith('"Bannerlord.exe')]


def ps(cmd, timeout=900):
    return subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', cmd], capture_output=True, text=True, timeout=timeout)


class Sim:
    def __init__(self): self.n = 0

    def send(self, cmd, timeout=25):
        """one engine command -> (ok, text). The engine polls simcmd.txt ~10x/s and appends '<id> ok|err <text>' to simout.txt"""
        self.n += 1; cid = 'c%d_%d' % (int(time.time() * 1000), self.n)
        t0 = time.time()
        while os.path.exists(CMD) and time.time() - t0 < 5: time.sleep(0.05)
        with open(CMD, 'w', newline='') as f: f.write('%s %s\n' % (cid, cmd))
        t0 = time.time()
        while time.time() - t0 < timeout:
            time.sleep(0.08)
            try:
                for line in open(OUT, encoding='utf-8', errors='ignore').read().splitlines()[::-1]:
                    if line.startswith(cid + ' '):
                        _, status, *rest = line.split(' ', 2) if line.count(' ') >= 2 else (cid, 'err', '')
                        return status == 'ok', (rest[0] if rest else '')
            except Exception: pass
        return False, 'timeout: no answer from the engine in %ds (frozen, crashed or not booted?)' % timeout

    def run(self, script, timeout=25):
        res = []
        for raw in [c.strip() for c in script.split(';')]:
            if not raw: continue
            w = raw.split()
            if w[0] == 'wait': time.sleep(float(w[1])); continue
            ok, text = self.send(raw, timeout)
            item = {'cmd': raw, 'ok': ok, 'text': text}
            if ok and w[0] == 'shot' and len(w) > 1:
                bmp = os.path.join(MOD, 'shots', w[1] + '.bmp'); t0 = time.time()
                while not os.path.exists(bmp) and time.time() - t0 < 10: time.sleep(0.1)
                time.sleep(0.4)
                if os.path.exists(bmp):
                    os.makedirs(SHOTS, exist_ok=True); png = os.path.join(SHOTS, w[1] + '.png')
                    try:
                        from PIL import Image; Image.open(bmp).convert('RGB').save(png); item['file'] = png
                    except Exception as e: item['file_error'] = str(e)
            res.append(item)
        return res


def state_dict(text):
    """'k=v k=v ...' from the engine's `state` line"""
    d = {}
    for m in re.finditer(r'(\w+)=(\([^)]*\)|\S+)', text): d[m.group(1)] = m.group(2)
    return d


def ready():
    if not pids(): return False
    if not os.path.exists(READY): return False
    ok, _ = Sim().send('ping', timeout=4)
    return ok


def cmd_status(a):
    p = pids(); emit({'running': bool(p), 'pid': p[0] if p else None, 'ready': ready() if p else False, 'log': LOG})


def cmd_up(a):
    if pids() and ready(): emit({'ok': True, 'note': 'already up', 'pid': pids()[0]})
    if pids(): subprocess.run(['taskkill', '/F', '/IM', 'Bannerlord.exe'], capture_output=True); time.sleep(2)
    t0 = time.time()
    r = ps("cd '%s'; .\\run_engine_sim.ps1 -Persist -Char %s -Timeout 700 -Modules '%s'" % (HERE, a.char, a.modules or DEFAULT_MODULES), timeout=800)
    up = ready()
    emit({'ok': up, 'boot_s': round(time.time() - t0), 'pid': (pids() or [None])[0], 'script_tail': r.stdout.strip().splitlines()[-2:]}, 0 if up else 1)


def cmd_down(a):
    subprocess.run(['taskkill', '/F', '/IM', 'Bannerlord.exe'], capture_output=True); time.sleep(1)
    emit({'ok': not pids()})


def need_sim():
    if not pids() or not ready(): emit({'ok': False, 'error': 'sim not running/ready - run: python sim.py up'}, 2)


def cmd_run(a):
    need_sim(); res = Sim().run(a.script, a.timeout)
    emit({'ok': all(r['ok'] for r in res), 'results': res}, 0 if all(r['ok'] for r in res) else 1)


def cmd_log(a):
    try: lines = open(LOG, encoding='utf-8', errors='ignore').read().splitlines()
    except Exception as e: emit({'ok': False, 'error': str(e)}, 2)
    if a.grep: lines = [l for l in lines if re.search(a.grep, l)]
    emit({'ok': True, 'lines': lines[-a.tail:]})


def cmd_build(a):
    r = subprocess.run(['dotnet', 'build', '-c', 'Release', '-o', 'bin', '--disable-build-servers'], cwd=os.path.join(HERE, 'CirithSim'), capture_output=True, text=True)
    errs = sorted(set(l.strip() for l in r.stdout.splitlines() if ' error ' in l))[:12]
    ok = r.returncode == 0 and not errs
    out = {'ok': ok, 'errors': errs}
    if ok and a.restart: subprocess.run(['taskkill', '/F', '/IM', 'Bannerlord.exe'], capture_output=True); time.sleep(2); a2 = argparse.Namespace(char='cirith_w_main', modules=None); return cmd_up(a2)
    emit(out, 0 if ok else 1)


# ---------------------------------------------------------------- test suites: each returns (name, passed, evidence)
def prep(s, bring=False):
    s.run('script on; wield; wait 1.5; stop; heal')
    if bring: s.run('bring 1.3; freeze; wait 0.3')


def suite_smoke(s):
    r = s.run('ping; state'); st = state_dict(r[1]['text'])
    return [('engine answers', r[0]['ok'], r[0]['text']), ('player alive with a Cirith sword', 'cirith_sword' in st.get('wield', ''), r[1]['text'])]


def suite_sprint(s):
    prep(s); s.run('state'); base = state_dict(s.run('state')[0]['text'])
    s.run('sprint on; move forward 3; wait 1.2'); on = state_dict(s.run('state')[0]['text'])
    s.run('sprint off; stop; wait 1.0'); off = state_dict(s.run('state')[0]['text'])
    boosted = on.get('boosted') == 'True' and float(on.get('maxSpeedMult', 0)) > float(base.get('maxSpeedMult', 1)) * 1.3
    restored = off.get('boosted') == 'False' and abs(float(off.get('maxSpeedMult', 0)) - float(base.get('maxSpeedMult', 0))) < 0.05
    return [('sprint boosts speed ~x1.5 while held', boosted, 'base %s -> sprint %s (boosted=%s)' % (base.get('maxSpeedMult'), on.get('maxSpeedMult'), on.get('boosted'))),
            ('boost is removed on release', restored, 'after release %s (boosted=%s)' % (off.get('maxSpeedMult'), off.get('boosted')))]


def suite_voice(s):
    before = state_dict(s.run('state')[0]['text']); played0 = int(re.search(r'played=(\d+)', before.get('voice(played', 'played=0') if False else s.run('state')[0]['text']).group(1))
    outs = []
    for t in ('Pain', 'Death', 'Grunt', 'Jump', 'Yell'): s.run('voice %s; wait 1.0' % t)
    txt = s.run('state')[0]['text']; played = int(re.search(r'played=(\d+)', txt).group(1))
    lg = [l for l in open(LOG, encoding='utf-8', errors='ignore').read().splitlines() if 'voice:' in l][-12:]
    return [('voice clips are started', played - played0 >= 4, 'played %d -> %d' % (played0, played)), ('no voice errors in the log', not any('err' in l.lower() for l in lg), ' | '.join(lg[-4:]))]


def suite_jump(s):
    prep(s); s.run('script on; wait 0.3'); z0 = None; zmax = -1e9; seen = set()
    s.run('jump')
    for _ in range(25):
        st = state_dict(s.run('state')[0]['text']); m = re.search(r'\(([-\d.]+),([-\d.]+),([-\d.]+)\)', st.get('pos', '(0,0,0)')); z = float(m.group(3)) if m else 0
        z0 = z if z0 is None else z0; zmax = max(zmax, z); seen.add(st.get('action', '')); seen.add(st.get('act0', '')); time.sleep(0.05)
    jumped = (zmax - z0) > 0.15 or any('jump' in x.lower() for x in seen)
    ctrl = state_dict(s.run('state')[0]['text']).get('weaponAnim')
    return [('she leaves the ground / the engine jump action plays (the unique animations must not block it)', jumped, 'rise %.2f m, actions seen: %s' % (zmax - z0, sorted(seen)))]


def suite_attacks(s):
    prep(s, bring=True); keys = []
    for d in ('right', 'left', 'up', 'down'):
        s.run('attack %s 0.9; wait 1.3; stop; wait 0.4' % d)
    lg = [l for l in open(LOG, encoding='utf-8', errors='ignore').read().splitlines() if 'attack clip' in l]
    names = sorted(set(re.sub(r'.*attack clip: ', '', l) for l in lg[-40:]))
    return [('attack clips are recognised', len(names) >= 2, 'clips: %s' % names)]


def suite_hit(s):
    prep(s, bring=True); hp0 = s.run('hp')[0]['text']; h0 = float(re.search(r'hp=(\d+)', hp0).group(1)) if re.search(r'hp=(\d+)', hp0) else -1
    for d in ('right', 'left', 'up', 'down'): s.run('bring 1.3; attack %s 0.9; wait 1.4; stop; wait 0.3' % d)
    hp1 = s.run('hp')[0]['text']; m = re.search(r'hp=(\d+)', hp1); h1 = float(m.group(1)) if m else -1
    ev = [l for l in open(LOG, encoding='utf-8', errors='ignore').read().splitlines() if 'hitbox:' in l or 'trigger Pain' in l][-6:]
    return [('swinging at a frozen enemy 1.3 m away damages it', h1 >= 0 and h1 < h0, 'enemy hp %s -> %s | %s' % (h0, h1, ' | '.join(ev)))]


def suite_give(s):
    prep(s); r = s.run('wield off; wait 0.8; give; wait 1.5; state'); st = state_dict(r[-1]['text'])
    return [('Ctrl+G equivalent puts the sword in her hand', 'cirith_sword' in st.get('wield', ''), r[-1]['text'])]


SUITES = {'smoke': suite_smoke, 'sprint': suite_sprint, 'voice': suite_voice, 'jump': suite_jump, 'attacks': suite_attacks, 'hit': suite_hit, 'give': suite_give}


def cmd_test(a):
    booted = False
    if not (pids() and ready()):
        a2 = argparse.Namespace(char='cirith_w_main', modules=None)
        try: cmd_up(a2)
        except SystemExit: pass
        booted = True
    if not ready(): emit({'ok': False, 'error': 'sim failed to boot'}, 2)
    names = list(SUITES) if a.suite == 'all' else a.suite.split(',')
    s = Sim(); report = []; allok = True
    for n in names:
        if n not in SUITES: emit({'ok': False, 'error': 'unknown suite ' + n, 'suites': list(SUITES)}, 2)
        t0 = time.time()
        try: checks = SUITES[n](s)
        except Exception as e: checks = [('suite crashed', False, '%s: %s' % (type(e).__name__, e))]
        ok = all(c[1] for c in checks); allok &= ok
        report.append({'suite': n, 'pass': ok, 's': round(time.time() - t0, 1), 'checks': [{'check': c[0], 'pass': bool(c[1]), 'evidence': c[2]} for c in checks]})
    if booted and not a.keep: subprocess.run(['taskkill', '/F', '/IM', 'Bannerlord.exe'], capture_output=True)
    emit({'ok': allok, 'report': report}, 0 if allok else 1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter); sp = ap.add_subparsers(dest='c', required=True)
    sp.add_parser('status').set_defaults(f=cmd_status)
    u = sp.add_parser('up'); u.add_argument('--char', default='cirith_w_main'); u.add_argument('--modules'); u.set_defaults(f=cmd_up)
    sp.add_parser('down').set_defaults(f=cmd_down)
    r = sp.add_parser('run'); r.add_argument('script'); r.add_argument('--timeout', type=int, default=25); r.set_defaults(f=cmd_run)
    t = sp.add_parser('test'); t.add_argument('suite'); t.add_argument('--keep', action='store_true'); t.set_defaults(f=cmd_test)
    l = sp.add_parser('log'); l.add_argument('--tail', type=int, default=40); l.add_argument('--grep'); l.set_defaults(f=cmd_log)
    b = sp.add_parser('build'); b.add_argument('--restart', action='store_true'); b.set_defaults(f=cmd_build)
    sp.add_parser('cmds').set_defaults(f=lambda a: emit({'engine_commands': __doc__.split('Engine-side commands')[1].strip()}))
    a = ap.parse_args(); a.f(a)


if __name__ == '__main__':
    main()
