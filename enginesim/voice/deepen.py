"""deepen.py [semitones] - pitch the ORIGINAL generated clips (voice/orig) down and write them to ModuleData/voice (formants shift with the pitch = a naturally deeper voice, duration unchanged).
usage: python deepen.py 3     (3 semitones down; 0 = restore the originals)"""
import sys, os, glob, subprocess, shutil
st = float(sys.argv[1]) if len(sys.argv) > 1 else 3.0
ff = shutil.which('ffmpeg') or r'C:\Users\Trey\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.1-full_build\bin\ffmpeg.exe'
here = os.path.dirname(os.path.abspath(__file__)); out = os.path.join(here, '..', 'CirithSim', 'module', 'ModuleData', 'voice')
ratio = 2 ** (-st / 12.0)
for f in sorted(glob.glob(os.path.join(here, 'orig', '*.wav'))):
    d = os.path.join(out, os.path.basename(f))
    if st == 0: shutil.copy(f, d); continue
    subprocess.run([ff, '-y', '-loglevel', 'error', '-i', f, '-af', f'rubberband=pitch={ratio:.5f}:formant=shifted:pitchq=quality,loudnorm=I=-16:TP=-1.5:LRA=11', '-ar', '44100', '-ac', '1', '-c:a', 'pcm_s16le', d], check=True)
print('pitched', st, 'semitones down (ratio', round(ratio, 3), ')')
