using UnityEngine;

namespace AlchemistsArsenal.Story
{
    /// <summary>How a shot presents its words.</summary>
    public enum ShotStyle
    {
        /// <summary>A line in the caption bar under the picture, typed out.</summary>
        Caption,
        /// <summary>Large centred title over the picture (the name of the game, THE END).</summary>
        Title,
        /// <summary>A credits card: centred, unhurried, advances on its own.</summary>
        Credits,
    }

    /// <summary>What an actor does while it is on stage.</summary>
    public enum ActorMotion { Still, Breathe, Bob, Float, FadeOut, Tilt, Tremble }

    /// <summary>What the picture itself does during a shot.</summary>
    public enum ShotFx { None, Rain, Motes, Flash, Embers, Fireflies, Shake, Pour }

    /// <summary>Which piece of music plays under a cutscene.</summary>
    public enum StoryMusic { Lullaby, Reveal, Resolution }

    /// <summary>
    /// One person or thing in a shot, placed by its feet on the 0..1 stage (x left
    /// to right, y bottom to top), drawn at the stage's own pixel scale times
    /// <see cref="Scale"/>. It walks in from <see cref="From"/> when that is set.
    /// </summary>
    public sealed class CutsceneActor
    {
        public string Id;
        public Vector2 At;
        public Vector2? From;
        public float Scale = 1f;
        public bool Flip;
        public float Delay;
        public float Rotation;
        public ActorMotion Motion = ActorMotion.Breathe;
        public Color Tint = Color.white;
    }

    /// <summary>
    /// One beat: a place, who is in it, one line (or none), and how the picture
    /// moves. Every shot moves (a slow push-in at least), so no beat is a still
    /// slide with a text wall under it.
    /// </summary>
    public sealed class Shot
    {
        public string Scene = "black";
        public CutsceneActor[] Actors = new CutsceneActor[0];
        /// <summary>Who speaks; empty for Nell's narration.</summary>
        public string Speaker = "";
        public string Line = "";
        /// <summary>A second, smaller line (a title's subtitle).</summary>
        public string Sub = "";
        /// <summary>Animalese pitch for the speaker's voice.</summary>
        public float Pitch = 1f;
        public ShotStyle Style = ShotStyle.Caption;
        public ShotFx Fx = ShotFx.None;
        public float ZoomFrom = 1f, ZoomTo = 1.05f;
        public Vector2 PanFrom, PanTo;
        /// <summary>When above 0, the shot moves on by itself this long after its text is in.</summary>
        public float Hold;
    }

    /// <summary>A scene of the story, played by the cutscene screen from start to end.</summary>
    public sealed class Cutscene
    {
        public string Id;
        public string Title;
        public StoryMusic Music;
        public Shot[] Shots;
    }
}
