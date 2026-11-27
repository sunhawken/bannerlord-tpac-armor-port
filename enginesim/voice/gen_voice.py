"""gen_voice.py - generate Cirith's replacement voice lines with ElevenLabs (voice id from the user) and convert them to WAV for the CirithSim DLL.
Key: env ELEVENLABS_API_KEY (never written anywhere). Output: ../CirithSim/module/ModuleData/voice/<type>_<n>.wav (mono 44.1 kHz 16 bit) + voice_manifest.txt (type count).
usage: python gen_voice.py [--only Pain,Death] [--model eleven_v3]"""
import os, sys, json, urllib.request, subprocess, tempfile, shutil
VOICE = 'wYL1PGq4R9ntiLUou261'
KEY = os.environ.get('ELEVENLABS_API_KEY') or sys.exit('ELEVENLABS_API_KEY is not set')
MODEL = 'eleven_v3'
only = None
for i, a in enumerate(sys.argv):
    if a == '--only': only = sys.argv[i + 1].split(',')
    if a == '--model': MODEL = sys.argv[i + 1]
# SkinVoiceManager.VoiceType name -> lines (v3 audio tags give grunts / screams instead of spoken words)
LINES = {
    'Grunt':   ['[grunts] Hah!', '[grunts] Hnh!', '[grunts] Ha!', '[exhales sharply] Hyah!'],
    'Jump':    ['[grunts] Hup!', '[grunts] Hah!', '[exhales] Hnh!'],
    'Yell':    ['[shouts] Hyaaah!', '[shouts] Come on!', '[shouts] Die!', '[screams] Aaaaargh!'],
    'Pain':    ['[groans] Ugh!', '[cries out] Aah!', '[groans] Nngh!', '[gasps] Argh!'],
    'Death':   ['[screams] Aaaaahh...', '[gasps] No... no...', '[groans weakly] Gaaah...'],
    'Stun':    ['[groans] Uhhh...', '[groans] Hnnngh...'],
    'Fear':    ['[gasps] No...', '[frightened] Stay back!'],
    'Climb':   ['[exhales] Hnh.', '[exhales] Hup.'],
    'Focus':   ['[calm] Now.', '[low] Hmm.'],
    'Debacle': ['[shouts] Fall back!', '[shouts] Retreat!'],
    'Victory': ['[shouts] Victory!', '[laughs] Ha ha ha!', '[confident] Is that all?'],
    'Drown':   ['[gasps] Ah!', '[coughs] Hah... hah!'],
}
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'CirithSim', 'module', 'ModuleData', 'voice'); os.makedirs(out, exist_ok=True)
ff = shutil.which('ffmpeg') or r'C:\Users\Trey\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-full_build\bin\ffmpeg.exe'
man = []; total = 0
for typ, lines in LINES.items():
    if only and typ not in only: continue
    n = 0
    for text in lines:
        body = json.dumps({'text': text, 'model_id': MODEL, 'voice_settings': {'stability': 0.35, 'similarity_boost': 0.85, 'style': 0.4}}).encode()
        req = urllib.request.Request(f'https://api.elevenlabs.io/v1/text-to-speech/{VOICE}?output_format=mp3_44100_128', data=body, headers={'xi-api-key': KEY, 'Content-Type': 'application/json', 'Accept': 'audio/mpeg'})
        try: mp3 = urllib.request.urlopen(req, timeout=90).read()
        except Exception as e:
            print(typ, repr(text), 'FAILED', getattr(e, 'code', ''), (e.read()[:160] if hasattr(e, 'read') else e)); continue
        tmp = os.path.join(tempfile.gettempdir(), 'cirith_voice.mp3'); open(tmp, 'wb').write(mp3)
        wav = os.path.join(out, f'{typ}_{n}.wav')
        subprocess.run([ff, '-y', '-loglevel', 'error', '-i', tmp, '-ac', '1', '-ar', '44100', '-c:a', 'pcm_s16le', '-af', 'silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse', wav], check=True)
        n += 1; total += len(text); print(typ, n, repr(text), os.path.getsize(wav), 'bytes')
    if n: man.append(f'{typ} {n}')
if not only: open(os.path.join(out, 'voice_manifest.txt'), 'w').write('\n'.join(man) + '\n')
print('characters used about', total)
