using UnityEngine;

namespace AlchemistsArsenal.Story
{
    /// <summary>
    /// <b>The Kettle-Charm</b> — the story, as it is played.
    ///
    /// <para><b>Who.</b> Nell Ashgrove keeps a potion shop at the edge of the
    /// Whispering Woods and writes this diary. Tam is her younger sibling (they).
    /// Grandmother Ysolde raised them both, taught Nell the craft, and went up the
    /// mountain to the Coven's Peak years ago and never came down. Sister Veil is
    /// the Coven's envoy, and a customer. Mira Thorn is a hedge-herbalist who was
    /// Ysolde's friend.</para>
    ///
    /// <para><b>The setup.</b> The Coven sends for Tam: a place at its school on
    /// the Peak. The night before Tam leaves, they fight, and Nell, furious, speaks
    /// Grandmother's kettle-charm crooked. Tam falls asleep at the table and does
    /// not wake. Veil tells Nell it is a sleep-curse only the Matriarch can lift,
    /// with a cure whose ingredients lie along five roads. Nell arms heroes.</para>
    ///
    /// <para><b>The clues</b> (fair play; each road gives one): the trees of the
    /// Woods whisper Ysolde's name; the dying Woodwose says "she kept the kettle, so
    /// they could not find the child"; a scorched page from the Cinder Peaks in
    /// Grandmother's hand describes a sleep-ward "for when they come for the
    /// child"; children sleep in the Frostbite ice with a violet Coven thread on
    /// every wrist, as Tam had; in the Venom Swamp Mira admits Ysolde went up the
    /// mountain to stop the Coven taking children. At the Counter, Veil's patience
    /// thins as Nell gets closer.</para>
    ///
    /// <para><b>The twist.</b> The Matriarch is what the Coven made of Ysolde.
    /// Nell's crooked words never cursed Tam: they set off the ward Ysolde hid in
    /// the kettle, which put Tam to sleep where the Coven could not take them.
    /// The "cure" Veil sent Nell to gather is the key that breaks that ward.</para>
    ///
    /// <para><b>The end.</b> Freed, Ysolde fades: "You didn't break my charm, Nell.
    /// You finished it." The ward lifts only when the one who spoke it lets the
    /// anger go. Nell pours the cure into the fire and forgives herself, and Tam
    /// wakes. Veil arrives for the child and finds every hero Nell armed standing
    /// in the doorway. The Coven is bigger than one Matriarch.</para>
    ///
    /// <para><b>What it is about:</b> the thing she blamed herself for was an act
    /// of love, hers and her grandmother's.</para>
    /// </summary>
    public static class StoryScript
    {
        // Stage heights (0 = bottom of the picture) where the floors are.
        private const float ShopFloor = 0.111f, BedFloor = 0.148f;
        // The heroes' fight sprites pivot at their centre: at 2x, 20 px up from the floor.
        private const float HeroFloor = ShopFloor + 20f / 108f;

        private static CutsceneActor A(string id, float x, float y, ActorMotion motion = ActorMotion.Breathe,
            bool flip = false, float? fromX = null, float scale = 1f, float delay = 0f, float rotation = 0f, Color? tint = null) =>
            new CutsceneActor
            {
                Id = id, At = new Vector2(x, y), From = fromX.HasValue ? new Vector2(fromX.Value, y) : (Vector2?)null,
                Motion = motion, Flip = flip, Scale = scale, Delay = delay, Rotation = rotation, Tint = tint ?? Color.white,
            };

        private static Shot Say(string scene, string speaker, string line, float pitch, params CutsceneActor[] actors) =>
            new Shot { Scene = scene, Speaker = speaker, Line = line, Pitch = pitch, Actors = actors };

        private static Shot Tell(string scene, string line, params CutsceneActor[] actors) =>
            new Shot { Scene = scene, Line = line, Pitch = 0.95f, Actors = actors };

        private static Shot WithFx(this Shot s, ShotFx fx) { s.Fx = fx; return s; }
        private static Shot Pan(this Shot s, float fromX, float toX, float zoomFrom = 1.04f, float zoomTo = 1.1f)
        {
            s.PanFrom = new Vector2(fromX, 0f); s.PanTo = new Vector2(toX, 0f); s.ZoomFrom = zoomFrom; s.ZoomTo = zoomTo;
            return s;
        }

        private static Shot Card(string title, string sub, ShotStyle style, float hold, string scene = "black") =>
            new Shot { Scene = scene, Line = title, Sub = sub, Style = style, Hold = hold, ZoomFrom = 1f, ZoomTo = 1.03f };

        private static readonly Color Backlit = new Color(0.1f, 0.08f, 0.13f, 1f);

        // ------------------------------------------------------------- opening

        public static readonly Cutscene Opening = new Cutscene
        {
            Id = "opening", Title = "The Night of the Crooked Charm", Music = StoryMusic.Lullaby,
            Shots = new[]
            {
                Tell("shop_night", "The night before Tam went up the mountain, it rained.",
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor), A("tam", 0.64f, ShopFloor, flip: true))
                    .WithFx(ShotFx.Rain).Pan(0.03f, -0.02f),
                Say("shop_night", "Tam", "They chose me, Nell. The school on the Peak. You can't keep me in this shop forever.", 1.35f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor), A("tam", 0.64f, ShopFloor, ActorMotion.Bob, flip: true))
                    .WithFx(ShotFx.Rain),
                Say("shop_night", "Nell", "Grandmother went up that mountain. She never came down.", 1f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor, ActorMotion.Tremble), A("tam", 0.64f, ShopFloor, flip: true))
                    .WithFx(ShotFx.Rain).Pan(0.04f, 0.06f, 1.06f, 1.14f),
                Say("shop_night", "Tam", "So I stay small because she didn't come back?", 1.35f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor), A("tam", 0.7f, ShopFloor, flip: true, fromX: 0.64f))
                    .WithFx(ShotFx.Rain),
                Tell("shop_night", "I reached for her kettle-charm, the little one that calms a room. I was too angry to say it straight.",
                    A("kettle_glow", 0.26f, 0.26f, ActorMotion.Tremble), A("nell", 0.34f, ShopFloor, fromX: 0.4f), A("tam", 0.7f, ShopFloor, flip: true))
                    .WithFx(ShotFx.Flash).Pan(0.08f, 0.1f, 1.1f, 1.2f),
                Tell("shop_night", "The words came out crooked. Tam laid their head on the table, and did not wake.",
                    A("kettle_glow", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor, ActorMotion.Still), A("tam_asleep", 0.66f, 0.333f, ActorMotion.Breathe))
                    .WithFx(ShotFx.Motes).Pan(-0.04f, -0.08f, 1.05f, 1.12f),
                Say("shop_dawn", "Sister Veil", "A sleep-curse. The Coven knows this kind.", 0.8f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor), A("veil", 0.84f, ShopFloor, flip: true, fromX: 1.12f)),
                Say("shop_dawn", "Sister Veil", "Our Matriarch can lift it. Bring her what the cure needs. Five roads lie between you and the Peak.", 0.8f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor), A("veil", 0.8f, ShopFloor, flip: true, fromX: 0.84f))
                    .Pan(-0.03f, -0.06f),
                Say("shop_dawn", "Nell", "Then I'll brew for anyone who'll walk them.", 1f,
                    A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.44f, ShopFloor, fromX: 0.4f), A("veil", 0.8f, ShopFloor, flip: true))
                    .Pan(0.02f, 0.0f, 1.08f, 1.14f),
                Card("ALCHEMIST'S ARSENAL", "brew in the morning · arm them · bring them home", ShotStyle.Title, 2.6f),
            },
        };

        // ------------------------------------------------------------ woodwose

        // The guardian, slumped where it fell, at three quarters of its arena size so
        // the whole of it fits the frame: arms, body, head, antlers, stacked as its rig stacks them.
        private static CutsceneActor[] FallenWoodwose(ActorMotion head) => new[]
        {
            A("woodwose_arm", 0.576f, 0.353f, ActorMotion.Still, flip: true, scale: 0.75f, tint: new Color(0.68f, 0.68f, 0.72f), rotation: -6f),
            A("woodwose_body", 0.5f, 0.12f, ActorMotion.Breathe, scale: 0.75f),
            A("woodwose_antlers", 0.5f, 0.476f, ActorMotion.Still, scale: 0.75f),
            A("woodwose_head", 0.5f, 0.37f, head, scale: 0.75f),
            A("woodwose_arm", 0.424f, 0.353f, ActorMotion.Still, scale: 0.75f, rotation: 6f),
        };

        public static readonly Cutscene Woodwose = new Cutscene
        {
            Id = "woodwose", Title = "What the Woodwose Remembered", Music = StoryMusic.Lullaby,
            Shots = new[]
            {
                Tell("woods", "The heroes found the old guardian where it fell, its heart-knot going dark.",
                    FallenWoodwose(ActorMotion.Still))
                    .WithFx(ShotFx.Fireflies).Pan(0f, 0f, 1.0f, 1.08f),
                Say("woods", "The Elder Woodwose", "...Ysolde's girl. You have her hands.", 0.45f,
                    FallenWoodwose(ActorMotion.Tremble))
                    .WithFx(ShotFx.Fireflies).Pan(0f, 0.08f, 1.15f, 1.3f),
                Say("woods", "The Elder Woodwose", "She kept the kettle... so they could not find the child.", 0.45f,
                    FallenWoodwose(ActorMotion.Still))
                    .WithFx(ShotFx.Fireflies).Pan(0f, 0.1f, 1.3f, 1.4f),
                Tell("black", "They carried the words home like a stone in a pocket. I wrote them down. I did not understand them yet."),
            },
        };

        // -------------------------------------------------------------- reveal

        public static readonly Cutscene Reveal = new Cutscene
        {
            Id = "reveal", Title = "Behind the Mask", Music = StoryMusic.Reveal,
            Shots = new[]
            {
                Tell("summit", "Her mask broke along the old crack. The face behind it was one I had watched grow old.",
                    A("matriarch_crown", 0.68f, 0.13f, ActorMotion.Still, rotation: -28f), A("mask_l", 0.4f, 0.14f, ActorMotion.Still, rotation: 18f),
                    A("mask_r", 0.6f, 0.16f, ActorMotion.Still, rotation: -12f), A("mask_chin", 0.51f, 0.1f, ActorMotion.Still, rotation: 70f),
                    A("ysolde", 0.5f, 0.22f, ActorMotion.Float))
                    .WithFx(ShotFx.Motes).Pan(0f, 0f, 1.12f, 1.2f),
                Say("summit", "Nell", "...Grandmother?", 1f,
                    A("mask_l", 0.4f, 0.14f, ActorMotion.Still, rotation: 18f), A("mask_r", 0.6f, 0.16f, ActorMotion.Still, rotation: -12f),
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell", 0.22f, 0.12f, fromX: -0.1f))
                    .WithFx(ShotFx.Motes),
                Say("summit", "Grandmother Ysolde", "Nell. You came all this way with their cure in your arms.", 0.75f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell_flask", 0.26f, 0.12f, fromX: 0.22f))
                    .WithFx(ShotFx.Motes).Pan(-0.02f, -0.05f, 1.06f, 1.12f),
                Say("summit", "Grandmother Ysolde", "I came up here to stop them taking children. They made me their Mother instead, and sent Veil down for the one I hid.", 0.75f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell_flask", 0.26f, 0.12f))
                    .WithFx(ShotFx.Motes).Pan(-0.05f, -0.07f, 1.12f, 1.18f),
                Say("summit", "Grandmother Ysolde", "Your charm was never a curse, love. It was my ward. It hid Tam where the Coven could not follow.", 0.75f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell_flask", 0.26f, 0.12f, ActorMotion.Tremble))
                    .WithFx(ShotFx.Motes),
                Say("summit", "Grandmother Ysolde", "And what you carry is not a cure. It is the key that breaks my ward.", 0.75f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell_flask", 0.26f, 0.12f))
                    .WithFx(ShotFx.Flash).Pan(0.06f, 0.08f, 1.15f, 1.24f),
                Say("summit", "Nell", "I said it angry. I thought I did this to them.", 1f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.Float), A("nell_flask", 0.3f, 0.12f, ActorMotion.Tremble, fromX: 0.26f))
                    .WithFx(ShotFx.Motes),
                Say("summit", "Grandmother Ysolde", "You didn't break my charm, Nell. You finished it.", 0.75f,
                    A("ysolde", 0.56f, 0.22f, ActorMotion.FadeOut), A("nell_flask", 0.3f, 0.12f))
                    .WithFx(ShotFx.Motes).Pan(0.03f, 0.03f, 1.1f, 1.04f),
            },
        };

        // -------------------------------------------------------------- ending

        public static readonly Cutscene Ending = new Cutscene
        {
            Id = "ending", Title = "The Kettle, Boiling", Music = StoryMusic.Resolution,
            Shots = new[]
            {
                Tell("shop_night", "The ward would lift only when the one who spoke it let the anger go.",
                    A("kettle_glow", 0.26f, 0.26f, ActorMotion.Still), A("nell_flask", 0.4f, ShopFloor, fromX: 0.5f), A("tam_asleep", 0.66f, 0.333f))
                    .Pan(0f, 0.04f),
                Tell("shop_night", "So I poured their cure into the fire, and let go of it with both hands.",
                    A("kettle_glow", 0.26f, 0.26f, ActorMotion.Still), A("cure", 0.31f, 0.2f, ActorMotion.Tilt, scale: 1f), A("nell", 0.38f, ShopFloor))
                    .WithFx(ShotFx.Pour).Pan(0.08f, 0.12f, 1.12f, 1.22f),
                Say("bedroom", "Nell", "Go wherever you want. Just come home and tell me about it.", 1f,
                    A("nell", 0.36f, BedFloor, ActorMotion.Breathe))
                    .WithFx(ShotFx.Motes).Pan(-0.02f, -0.05f),
                Say("bedroom_awake", "Tam", "...Nell? Is the kettle on?", 1.35f,
                    A("nell", 0.36f, BedFloor, ActorMotion.Bob))
                    .Pan(-0.05f, -0.08f, 1.08f, 1.16f),
                Say("doorway", "Sister Veil", "The Coven is more than one Matriarch, witch.", 0.8f,
                    A("veil", 0.5f, 0.3f, ActorMotion.Still, scale: 0.75f),
                    A("hero_knight", 0.41f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                    A("hero_herbalist", 0.48f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit, delay: 0.15f),
                    A("hero_merchant", 0.55f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit, delay: 0.3f),
                    A("hero_rookie", 0.62f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit, delay: 0.45f))
                    .Pan(0f, 0f, 1.02f, 1.1f),
                Say("doorway", "Nell", "And this shop is more than one witch.", 1f,
                    A("veil", 0.5f, 0.3f, ActorMotion.Still, scale: 0.75f),
                    A("hero_knight", 0.41f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                    A("hero_herbalist", 0.48f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                    A("hero_merchant", 0.55f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                    A("hero_rookie", 0.62f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                    A("nell", 0.18f, ShopFloor, fromX: -0.1f))
                    .Pan(-0.03f, 0.02f, 1.1f, 1.14f),
                Card("THE END", "...for now", ShotStyle.Title, 2.8f),
            },
        };

        // ------------------------------------------------------------- credits

        public static readonly Cutscene Credits = new Cutscene
        {
            Id = "credits", Title = "Credits", Music = StoryMusic.Resolution,
            Shots = new[]
            {
                Card("ALCHEMIST'S ARSENAL", "The Kettle-Charm", ShotStyle.Credits, 2.4f),
                Card("NELL ASHGROVE", "who kept the shop, and the kettle", ShotStyle.Credits, 2.2f),
                Card("TAM", "who wanted to go somewhere", ShotStyle.Credits, 2.2f),
                Card("GRANDMOTHER YSOLDE", "who hid a child inside a charm", ShotStyle.Credits, 2.2f),
                Card("SISTER VEIL · MIRA THORN · THE ELDER WOODWOSE", "and every hero who walked the five roads", ShotStyle.Credits, 2.4f),
                Card("MADE IN-HOUSE", "the art, the music, the voices and the story were made for this game", ShotStyle.Credits, 2.4f),
                Card("THANK YOU FOR PLAYING", "The shop is still open.", ShotStyle.Credits, 2.8f),
            },
        };

        // --------------------------------------------------------------- diary

        private static Shot Picture(string scene, ShotFx fx, params CutsceneActor[] actors) =>
            new Shot { Scene = scene, Fx = fx, Actors = actors, ZoomFrom = 1f, ZoomTo = 1.06f };

        /// <summary>The illustration on the left page of a diary entry.</summary>
        public static Shot DiaryPicture(string entryId) => entryId switch
        {
            "diary_00" => Picture("shop_night", ShotFx.Motes,
                A("kettle_glow", 0.26f, 0.26f, ActorMotion.Still), A("nell", 0.4f, ShopFloor, ActorMotion.Still), A("tam_asleep", 0.66f, 0.333f)),
            "diary_ww" => Picture("woods", ShotFx.Fireflies),
            "diary_perfect" => Picture("shop_dawn", ShotFx.None,
                A("kettle", 0.26f, 0.26f, ActorMotion.Still), A("nell_flask", 0.44f, ShopFloor)),
            "diary_woodwose" => Picture("woods", ShotFx.Fireflies, FallenWoodwose(ActorMotion.Still)),
            "diary_cinder" => Picture("peak", ShotFx.Embers),
            "diary_frost" => Picture("bedroom", ShotFx.Motes, A("nell", 0.36f, BedFloor, ActorMotion.Still)),
            "diary_swamp" => Picture("shop_night", ShotFx.None, A("kettle_glow", 0.26f, 0.26f, ActorMotion.Tremble)),
            "diary_veil" => Picture("shop_dawn", ShotFx.None,
                A("nell", 0.4f, ShopFloor), A("veil", 0.8f, ShopFloor, flip: true)),
            "diary_mask" => Picture("summit", ShotFx.Motes,
                A("mask_l", 0.4f, 0.14f, ActorMotion.Still, rotation: 18f), A("mask_r", 0.6f, 0.16f, ActorMotion.Still, rotation: -12f),
                A("ysolde", 0.5f, 0.22f, ActorMotion.Float)),
            "diary_end" => Picture("bedroom_awake", ShotFx.None, A("nell", 0.36f, BedFloor)),
            "diary_after" => Picture("doorway", ShotFx.None,
                A("veil", 0.5f, 0.3f, ActorMotion.Still, scale: 0.75f),
                A("hero_knight", 0.41f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                A("hero_herbalist", 0.48f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                A("hero_merchant", 0.55f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit),
                A("hero_rookie", 0.62f, HeroFloor, ActorMotion.Still, scale: 2f, tint: Backlit)),
            _ => Picture("black", ShotFx.Motes),
        };

        public static Cutscene ById(string id) => id switch
        {
            "opening" => Opening,
            "woodwose" => Woodwose,
            "reveal" => Reveal,
            "ending" => Ending,
            "credits" => Credits,
            _ => null,
        };
    }
}
