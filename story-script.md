# Orca Game: Story Script (combined draft)

`{NAME}` = the name the player types (default **Alethi**).
`[Brackets]` = stage directions and dev notes.

---

## 1. Arrival at the dock

[The opening voyage ends. The player presses SPACE and hops from the boat onto the dock. Orca's spirit (Orca sprite) waits near the dock.]

**ORCA:** HALT! Who goes there?!

**PLAYER:** The villagers of the archipelago sent me. Something happened to the four guardians who watch over us, and I'm here to find out what.

**ORCA:** Then you're too late, human. I'm one of those guardians. Or I was.

**ORCA:** I watched over this island for a long time. Now I'm nothing more than a spirit.

**PLAYER:** One of them? What about the other three?

**ORCA:** Fallen, like me. We're joining what's left of our power back into one guardian, but that will take time.

**ORCA:** I don't remember much from that night. Maybe the others do.

**ORCA:** What's your name?

[Name entry: the player types a name. Blank = Alethi.]

**PLAYER:** {NAME}.

**ORCA:** Well, {NAME}, would you like my help with this?

- **[YES]** **ORCA:** Very well! Follow me.
- **[NO, I'd like to investigate on my own]** **ORCA:** Very well. Good luck, {NAME}.

[If NO, skip Orca's lines below while he isn't following. See open questions.]

---

## 2. Al Cachud (dice and the game of 21)

[The player knocks on Al Cachud's door and he comes out.]

**AL CACHUD:** Orca! What happened to you?

**ORCA:** I wish I knew. It seems all four of us have fallen.

**AL CACHUD:** All four? But how?

**ORCA:** That's what {NAME} and I are here to find out.

**AL CACHUD:** A detective? Don't bother. This isn't something you can handle.

**ORCA:** Al Cachud.

**AL CACHUD:** Fine, fine.

**PLAYER:** Do you remember anything at all?

**AL CACHUD:** Not much. I walked the island with my friend, the Antelope, like any other day. When the sun went down he said he was going to the fountain to enjoy the view, so I left him there for the night.

**AL CACHUD:** And today he's just... gone.

**PLAYER:** Then I'll check the fountain and see if anything was left behind.

**AL CACHUD:** If you find anything, bring it straight back. It might jog my memory.

[The player finds the dice at the fountain and brings them back.]

**AL CACHUD:** Our dice! We used to play whenever nothing was happening on the island.

**AL CACHUD:** Tell you what: beat me and I'll tell you everything I know. I hope you can count, human.

[Game of 21. Rules and tips are in the separate document.]

**AL CACHUD:** Haha! Not bad. Glad they didn't send someone stupid.

**PLAYER:** So, did you remember anything?

**AL CACHUD:** A deal's a deal... but honestly, no. You're more fun to play with than he was, though. Every time I won, the Antelope called me a real *Jerk*.

**ORCA:** That wasn't very helpful. Hopefully Iguana knows more. Follow me!

[JERK is added to the clue words.]

---

## 3. Iguana (potion hunt)

[Orca leads the player to the alchemist's house. Iguana is there.]

**IGUANA:** Orca! Long time no see.

**ORCA:** Good to see you too, Iguana. {NAME} and I are trying to figure out what happened to us. Do you remember anything?

**IGUANA:** No, I've been searching the island too. I brewed five special potions to give us a clue, but I dropped them all over the island.

**IGUANA:** Could you find them for me? This letter will help.

**PLAYER:** Interesting. I'll take a closer look.

[The player takes the Clue Letter and plays the potion hunt. After finding all five, they talk to Iguana at the house.]

**PLAYER:** Here are your potions.

**IGUANA:** Thanks! Now let's see...

[Dev: FELL is written on the Clue Letter the player is holding, BELOW the map so it doesn't block the view.]

**IGUANA:** Huh, "fell"? What does that mean?

**PLAYER:** No idea, but I'll keep it in my pocket for now.

**ORCA:** Agreed. Iguana, we're going to see Panther next. Fair warning, {NAME}: Panther can be pretty eccentric.

**IGUANA:** Isn't that the truth! I'll wait here in case my attendant comes back.

---

## 4. Magician and Panther (wand and the hat game)

[Orca leads the player to the Magician's front door.]

**ORCA:** Magician, I know losing Panther has been hard. But do you know anything about what happened?

**MAGICIAN:** [Dev: portrait is a big pixel question mark instead of the PNG placeholder.] I might, but I lost my wand when I passed out in the forest that night.

**MAGICIAN:** Panther's spirit might know where it is. His grave is behind my house. I need that wand to find any clues.

**ORCA:** ...Okay. {NAME}, let's go see Panther's grave.

[The player and Orca walk to the gravestone behind the Magician's house.]

**ORCA:** Panther, {NAME} and I are going around the island trying to find out who did this to us. Do you know where the Magician's wand is? It could help us.

**PANTHER:** Oh, sure! Last I remember, we were behind a tree in the forest next to our house. It's probably still there.

[The player digs up the wand at the dig site and brings it back to the Magician's front door.]

**MAGICIAN:** My wand! Thank you. Now, every trick needs a trade to reveal a secret. Are you ready?

- **[YES]** The hat game starts right away.
- **[NO]** **MAGICIAN:** Come back whenever you're ready. [The player can return any time.]

[After the player wins the hat game.]

**MAGICIAN:** The magic is fulfilled. Now let the secret be revealed!

[Dev, needs implementing: the wand draws in the air with Unity particles, spelling THIEF, then a rough pixel sketch of the Orca statue.]

**ORCA:** That's my statue! Let's go check it out. Thank you, Magician.

---

## 5. Orca statue (anagram)

[The player goes to the Orca statue and arranges FELL + JERK + THIEF into JEFF THE KILLER.]

**ORCA:** Jeff?! That's Al Cachud's real name!

**ORCA:** Wait, he's trying to escape! Quick, back to the dock. We can spot him from the lighthouse!

---

## 6. Lighthouse (mouse trap)

[The player takes the boat to the lighthouse and plays the mouse-trap game. Dev, needs implementing: replace the cube placeholder with Al Cachud's sprite.]

*(The ending after the chase isn't written yet.)*

---

## 7. IFs

if the player chose to explore on their own, modify the script so they do not include the orca but instead it's the player talking to the npcs, and after they find all 3 clues, spawn the orca sprite at the statue, so the player knows to go there, the dialog is as follows:

Orca: Oh hey {NAME}, did you find out anything?

Player: I've gathered 3 words, but I have no idea what to do with them...

Orca: Three words? Theres something on the statue that might fit those words! Take a look!

[goes into the anagram puzzle]

## Other notes

- There are already lines that display on the screen after the lighthouse chase.
- Use antelope
- I've put the spirtes that should show up in a dialog in Assets/Sprites/[animalname_ghost_heads].png , when talking, a popup should appear where the player's [plus Orca's] sprite shows up on the right side of the screen, while the npc's sprite is on the left side. There shouls be a wide dialog box across the bottoms screen that is one layer above the talking sprites. When a sprite (npc or player) is talking, they should bob up and down. if an ghost head is not there, just use their normal sprites. Also, unify all talking dialogs, right now there are about 3 different ones, change them all to the one i described earlier. every dialog besides the magician and alch lines should leave the background as the game, keep the magician and alch dialogs background as the stage as they are now.

