#!/bin/bash
# run_clothtest.sh <label> [CK_ZERO list]  - clone dress with mutated cook blob, run the motion test, save a strip of frames as out/ct_<label>.png
cd /c/Users/Trey/Desktop/BannerlordGallery/src/tool
CK_ZERO="$2" CK_PATCH="" powershell -NoProfile -Command "dotnet bin\Release\net8.0\tool.dll 'T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Native\AssetPackages' clothclone ..\..\enginesim\CirithSim\module\AssetPackages\cloth_test.tpac cirith_clothtest | Select-String 'zeroed|wrote'"
cd ../../enginesim
CIRITH_MOTION=1 powershell -NoProfile -ExecutionPolicy Bypass -File run_engine_sim.ps1 -Char cirith_t_dress -Timeout 220 2>&1 | grep -E 'rror|timeout'
python - <<PY
from PIL import Image
idx=[3,7,10,12,15,20]; W=6; s=Image.new('RGB',(320*W,180))
for i,k in enumerate(idx): s.paste(Image.open(f'out/shots/mot_{k:02d}.png').crop((640,150,1280,750)).resize((320,180)),(i*320,0))
s.save('out/ct_$1.png')
PY
