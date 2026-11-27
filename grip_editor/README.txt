CIRITH SWORD GRIP EDITOR
========================
1. Double-click  "Start Grip Editor.bat"  (needs Python; it opens http://127.0.0.1:8790/ - this is separate from your gallery).
2. Pick a pose (Idle / Walk / Run / Sprint). Her body plays her original animation untouched.
3. Move / turn the SWORD with the sliders until it sits where you want it in her left hand:
     Move the sword (cm)    - along her left-hand bone axes
     Turn the sword (deg)   - around the sword's own axes (X across the guard, Y thin side, Z along the blade)
   "Close on hand" shows a close-up; "Copy to all poses" copies the numbers of the current pose to the others.
4. Press  "Save edits"  -> your browser downloads grip_edits.json. Send that file back.
   (Your numbers are also remembered in this browser, and "Load edits..." re-opens a saved file.)

What happens with the file: transplant\apply_grip_edits.py grip_edits.json  writes enginesim\CirithSim\module\ModuleData\cirith_grip_edits.txt,
which the CirithSim DLL reads when the game starts. The sword in the game is placed with exactly the same maths as the preview.
