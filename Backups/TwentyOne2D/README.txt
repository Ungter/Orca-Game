The flat, arcade-board version of Al Cachud's twenty-one game, saved before the game moved to a
3D table (Buckshot Roulette style). These files sit outside Assets so Unity does not compile them.

To restore it, copy the four scripts back over Assets/ and the two tests over Assets/Editor/,
and delete Assets/TwentyOneTable.cs (and its .meta). The scene needs no changes: TwentyOneGame
keeps the same script GUID and the same tile sprites.
