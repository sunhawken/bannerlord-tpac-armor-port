CIRITH SWORD + POSE EDITOR
==========================
1. Double-click  "Start Grip Editor.bat"  (needs Python; opens http://127.0.0.1:8790/ - separate from your gallery). Press Ctrl+F5 if you see an old version.
2. The model is the EXACT mesh the game uses (angled arms, slid hands, wrist sleeves, trims). Pick a pose: Idle / Walk / Run / Sprint.
3. SWORD: drag the orange ball (moves the sword) or the blue ball at the blade tip (turns the blade), or use the sliders.
4. BODY: the green balls are her joints (shoulder, elbow, wrist, hand, hips, spine, neck, head, knee, ankle, toe, both sides).
   - Pause on a frame, drag a ball to bend that joint.
   - "Whole animation" = the bend is added to every frame of the pose. "Just around this moment" = a smooth bend centred on the frame you are on
     (the "moment width" slider sets how many frames it spreads over).
   - Click a ball to select it and fine-tune X/Y/Z; the list under it shows every joint change in the pose (click to jump to it, x deletes it).
5. "Save edits" downloads grip_edits.json - send it back. "Reset this pose" clears the sword + joint changes of the current pose. Your work is also kept in this browser.

What happens with the file: transplant\apply_grip_edits.py grip_edits.json bakes your joint changes into the four clips (retarget_anim.py) and writes the sword offsets for the DLL.
