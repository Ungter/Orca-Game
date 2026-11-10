# Orca-Game
Game of Orca

## Debug tools

Press **backtick** to toggle the debug window, or **Esc** to close it.
Use **Finish** beside a minigame to complete it without playing through it.
Digging completion uncovers the buried reward for normal pickup.
The optional **Speed hack** toggle keeps the existing 5x movement and noclip:
**WASD** moves, **Shift** rises, and **Ctrl** descends. It stays enabled when
the window closes, until toggled off. Debug options reset with the scene.

## Lighthouse stacker

Solve the dolphin anagram to unlock the lighthouse ferry and stacking game.
Press **E** at the main dock to sail to the lighthouse island. Follow the gold arrow
and distance label to the gold start ring, then press **E** to start stacking.

- Press **Space** to stack the moving section. Overhang is trimmed; a complete miss ends the run.
- Stack **10 sections** to win. The win stays earned, and stacking continues without a height cap.
- Press **R** after a miss to retry, or **Esc** to leave.
- Return to the island's ferry landing and press **E** to sail back. **Esc** cancels a crossing.

The win threshold is configurable on `LighthouseStacker`. The world-model rebuild
menu is **Tools > Orca > Build Boat, Lighthouse Island And Dolphin Counter**.
