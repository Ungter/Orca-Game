using System;
using System.Collections.Generic;
using UnityEngine;

// Who appears in the dialogue box, and the art each one uses (Resources/DialoguePortraits).
// A ghost head wins over the character's normal sprite; with neither, DialoguePortraits falls
// back to art found in the scene or in Resources.
[CreateAssetMenu(menuName = "Orca/Dialogue Portraits", fileName = "DialoguePortraits")]
public class DialoguePortraitSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public Speaker speaker;
        public Texture2D ghostHead;
        public Sprite sprite;
        [Tooltip("The sprite looks to the left of the picture; it is mirrored on the left of the screen.")]
        public bool spriteFacesLeft;
    }

    public List<Entry> entries = new List<Entry>();

    public Entry Find(Speaker speaker)
    {
        foreach (Entry entry in entries)
            if (entry != null && entry.speaker == speaker) return entry;
        return null;
    }
}
