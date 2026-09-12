using System;
using UnityEngine;
using AlchemistsArsenal.Systems;

namespace AlchemistsArsenal.UI.Stations
{
    /// <summary>
    /// One crafting station's panel. The morning screen owns the shell (top bar,
    /// rail, order dock) and nothing else; each station owns its own layout, its own
    /// minigame state and its own day reset.
    ///
    /// This split exists because the four stations had grown into one 570-line
    /// screen class where a change to the bottling needle meant editing the same
    /// file as the counter's forecast. A station now only has to know about its own
    /// controls and the one <see cref="Changed"/> callback it raises when it has
    /// moved the order's quality.
    /// </summary>
    public abstract class StationPanel
    {
        /// <summary>The station's root rect, stretched inside the shell's centre column.</summary>
        public RectTransform Root { get; private set; }

        /// <summary>Short uppercase name for the rail.</summary>
        public abstract string RailName { get; }

        /// <summary>Icon for the rail button.</summary>
        public abstract Sprite RailIcon { get; }

        /// <summary>Has the player finished what this station is for today?</summary>
        public virtual bool Complete => false;

        /// <summary>True when the station's centre must show the world behind the UI.</summary>
        public virtual bool ShowsWorld => false;

        /// <summary>Raise after anything that moves quality, so the shell redraws the dock.</summary>
        protected Action Changed { get; private set; }

        public void Build(Transform parent, Action onChanged)
        {
            Changed = onChanged;
            Root = UIFactory.Root(parent, GetType().Name);
            BuildContent(Root);
        }

        protected abstract void BuildContent(RectTransform root);

        /// <summary>A brand new morning: wipe per-day state and rebuild anything generated.</summary>
        public virtual void NewDay() { }

        /// <summary>The player switched to this tab.</summary>
        public virtual void OnEnter() { }

        /// <summary>The player switched away.</summary>
        public virtual void OnExit() { }

        /// <summary>Per-frame, only while this station is the active tab.</summary>
        public virtual void Tick() { }

        /// <summary>Redraw anything that reflects the order (called when quality changes).</summary>
        public virtual void Refresh() { }

        protected static ActiveOrder Order =>
            CraftingManager.Instance != null ? CraftingManager.Instance.CurrentOrder : null;

        protected static bool HasOrder => Order != null;

        /// <summary>Destroy every child of a container before rebuilding it.</summary>
        protected static void Clear(Transform container)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);
        }
    }
}
