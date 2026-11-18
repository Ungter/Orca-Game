// Every spoken line of the story (story-script.md), in one place. {NAME} becomes the name the
// player typed. Lines that Orca says while he travels with the player have a version without him
// for players who chose to investigate alone (StoryDirector.OrcaFollowing).
public static class StoryScript
{
    private static DialogueLine Player(string text) => DialogueLine.Say(Speaker.Player, text);
    private static DialogueLine Orca(string text) => DialogueLine.Say(Speaker.Orca, text);
    private static DialogueLine Cachud(string text) => DialogueLine.Say(Speaker.AlCachud, text);
    private static DialogueLine Iguana(string text) => DialogueLine.Say(Speaker.Iguana, text);
    private static DialogueLine Magician(string text) => DialogueLine.Say(Speaker.Magician, text);
    private static DialogueLine Panther(string text) => DialogueLine.Say(Speaker.Panther, text);

    private static bool WithOrca => StoryDirector.OrcaFollowing;

    // 1. Arrival at the dock. The last line ends in AcceptHelp's choice.
    public static readonly DialogueLine[] Arrival =
    {
        Orca("HALT! Who goes there?!"),
        Player("The villagers of the archipelago sent me. Something happened to the four guardians who watch over us, and I'm here to find out what."),
        Orca("Then you're too late, human. I'm one of those guardians. Or I was."),
        Orca("I watched over this island for a long time. Now I'm nothing more than a spirit."),
        Player("One of them? What about the other three?"),
        Orca("Fallen, like me. We're joining what's left of our power back into one guardian, but that will take time."),
        Orca("I don't remember much from that night. Maybe the others do."),
        new DialogueLine(Speaker.Orca, "What's your name?", true),
        Player("{NAME}."),
        Orca("Well, {NAME}, would you like my help with this?"),
    };
    public static readonly string[] AcceptHelp = { "YES", "NO, I'd like to investigate on my own" };
    public static readonly DialogueLine[] HelpAccepted = { Orca("Very well! Follow me.") };
    public static readonly DialogueLine[] HelpDeclined = { Orca("Very well. Good luck, {NAME}.") };

    // 2. Al Cachud, the dice and the game of 21.
    public static DialogueLine[] CachudMeeting => WithOrca
        ? new[]
        {
            Cachud("Orca! What happened to you?"),
            Orca("I wish I knew. It seems all four of us have fallen."),
            Cachud("All four? But how?"),
            Orca("That's what {NAME} and I are here to find out."),
            Cachud("A detective? Don't bother. This isn't something you can handle."),
            Orca("Al Cachud."),
            Cachud("Fine, fine."),
        }
        : new[]
        {
            Cachud("Who are you?"),
            Player("I'm {NAME}. Something happened to the four guardians, and I'm here to find out what."),
            Cachud("A detective? Don't bother. This isn't something you can handle."),
            Player("I'll be the judge of that."),
            Cachud("Fine, fine."),
        };

    public static readonly DialogueLine[] CachudStory =
    {
        Player("Do you remember anything at all?"),
        Cachud("Not much. I walked the island with my friend, the Antelope, like any other day. When the sun went down he said he was going to the fountain to enjoy the view, so I left him there for the night."),
        Cachud("And today he's just... gone."),
        Player("Then I'll check the fountain and see if anything was left behind."),
        Cachud("If you find anything, bring it straight back. It might jog my memory."),
    };

    public static readonly DialogueLine[] CachudWaiting =
    {
        Cachud("Found anything at the fountain yet? If you do, bring it straight back."),
    };

    public static readonly DialogueLine[] CachudDiceReturned =
    {
        Cachud("Our dice! We used to play whenever nothing was happening on the island."),
        Cachud("Tell you what: beat me and I'll tell you everything I know. I hope you can count, human."),
    };

    public static readonly DialogueLine[] CachudRematch = { Cachud("Back for another round? I hope you can count, human.") };

    // After winning the game of 21. JerkLine reveals the clue word.
    public const string JerkLine = "A deal's a deal... but honestly, no. You're more fun to play with than he was, though. Every time I won, the Antelope called me a real JERK.";
    public static DialogueLine[] CachudBeaten => new[]
    {
        Cachud("Haha! Not bad. Glad they didn't send someone stupid."),
        Player("So, did you remember anything?"),
        Cachud(JerkLine),
        WithOrca ? Orca("That wasn't very helpful. Hopefully Iguana knows more. Follow me!")
            : Player("That wasn't very helpful. Hopefully someone else on this island knows more."),
    };
    public static readonly DialogueLine[] CachudBeatenAgain = { Cachud("Haha! Again? Fine, fine, you win.") };

    // 3. Iguana at the alchemist's house and the potion hunt.
    public static DialogueLine[] IguanaMeeting => WithOrca
        ? new[]
        {
            Iguana("Orca! Long time no see."),
            Orca("Good to see you too, Iguana. {NAME} and I are trying to figure out what happened to us. Do you remember anything?"),
            Iguana("No, I've been searching the island too. I brewed five special potions to give us a clue, but I dropped them all over the island."),
            Iguana("Could you find them for me? This letter will help."),
        }
        : new[]
        {
            Iguana("Oh! A visitor? Who are you?"),
            Player("I'm {NAME}. I'm trying to find out what happened to the guardians. Do you remember anything?"),
            Iguana("No, I've been searching the island too. I brewed five special potions to give us a clue, but I dropped them all over the island."),
            Iguana("Could you find them for me? This letter will help."),
        };

    public static readonly DialogueLine[] LetterGiven =
    {
        Player("Interesting. I'll take a closer look."),
        Iguana("Hold the letter up with its number, then press E to start the hunt."),
    };
    public static readonly DialogueLine[] LetterNoRoom = { Iguana("I have a letter for you, but your bag is full! Come back for it.") };
    public static readonly DialogueLine[] KeepLooking = { Iguana("My five potions are still out there. Keep looking!") };

    // Before FELL is written on the letter...
    public static readonly DialogueLine[] PotionsFound =
    {
        Player("Here are your potions."),
        Iguana("Thanks! Now let's see..."),
    };

    // ...and after.
    public static DialogueLine[] ClueRead => WithOrca
        ? new[]
        {
            Iguana("Huh, \"fell\"? What does that mean?"),
            Player("No idea, but I'll keep it in my pocket for now."),
            Orca("Agreed. Iguana, we're going to see Panther next. Fair warning, {NAME}: Panther can be pretty eccentric."),
            Iguana("Isn't that the truth! I'll wait here in case my attendant comes back."),
        }
        : new[]
        {
            Iguana("Huh, \"fell\"? What does that mean?"),
            Player("No idea, but I'll keep it in my pocket for now."),
            Iguana("Maybe the Magician knows. Fair warning: Panther, his old friend, can be pretty eccentric."),
            Iguana("I'll wait here in case my attendant comes back."),
        };

    public static readonly DialogueLine[] IguanaFarewell = { Iguana("Good luck, {NAME}. I'll wait here in case my attendant comes back.") };

    // 4. The Magician, Panther's grave and the hat trick.
    public static DialogueLine[] MagicianMeeting => WithOrca
        ? new[]
        {
            Orca("Magician, I know losing Panther has been hard. But do you know anything about what happened?"),
            Magician("I might, but I lost my wand when I passed out in the forest that night."),
            Magician("Panther's spirit might know where it is. His grave is behind my house. I need that wand to find any clues."),
            Orca("...Okay. {NAME}, let's go see Panther's grave."),
        }
        : new[]
        {
            Player("Hello? I'm {NAME}. I'm looking into what happened to the guardians. Do you know anything?"),
            Magician("I might, but I lost my wand when I passed out in the forest that night."),
            Magician("Panther's spirit might know where it is. His grave is behind my house. I need that wand to find any clues."),
            Player("...Okay. Then I'll go see Panther's grave."),
        };

    public static readonly DialogueLine[] MagicianWaiting =
    {
        Magician("Panther's grave is behind my house. His spirit might know where my wand is."),
    };

    // Each ends in the Ready choice.
    public static readonly DialogueLine[] WandReturned =
    {
        Magician("My wand! Thank you. Now, every trick needs a trade to reveal a secret. Are you ready?"),
    };
    public static readonly DialogueLine[] TrickOffer = { Magician("Every trick needs a trade to reveal a secret. Are you ready?") };
    public static readonly string[] Ready = { "YES", "NO" };
    public static readonly DialogueLine[] NotReady = { Magician("Come back whenever you're ready.") };

    public static readonly DialogueLine[] TrickWon = { Magician("The magic is fulfilled. Now let the secret be revealed!") };

    // After the wand writes THIEF and sketches the statue in the air.
    public static DialogueLine[] SecretRevealed => WithOrca
        ? new[] { Orca("That's my statue! Let's go check it out. Thank you, Magician.") }
        : new[] { Player("That's the Orca statue! I should go check it out. Thank you, Magician.") };

    public static DialogueLine[] PantherGrave => new[]
    {
        WithOrca
            ? Orca("Panther, {NAME} and I are going around the island trying to find out who did this to us. Do you know where the Magician's wand is? It could help us.")
            : Player("Panther? I'm {NAME}. I'm trying to find out who did this to you. Do you know where the Magician's wand is? It could help."),
        Panther("Oh, sure! Last I remember, we were behind a tree in the forest next to our house. It's probably still there."),
    };

    // 5. The Orca statue. Players who went alone find Orca waiting there once they hold all
    // three words.
    public static readonly DialogueLine[] StatueMeeting =
    {
        Orca("Oh hey {NAME}, did you find out anything?"),
        Player("I've gathered 3 words, but I have no idea what to do with them..."),
        Orca("Three words? There's something on the statue that might fit those words! Take a look!"),
    };

    public static readonly DialogueLine[] KillerNamed =
    {
        Orca("Jeff?! That's Al Cachud's real name!"),
        Orca("Wait, he's trying to escape! Quick, back to the dock. We can spot him from the lighthouse!"),
    };
}
