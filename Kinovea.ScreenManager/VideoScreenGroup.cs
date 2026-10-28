using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.14.0) A group of screens: videos of the same race from several cameras (the same swimmer), analysed as one. The analyst
    /// chooses its screens (the button «Ομάδα» of the common controls). The swim analysis of the group is the one of its lead, the
    /// screen whose video keeps the race (the one of the analysis when the group was made, else its first screen): a mark made on any
    /// screen of the group goes to that race at the same moment, through the synchronisation of the screens (their time origins);
    /// one round button in the middle of the group instead of one on each of them. The screen manager sets it, on the UI thread.
    /// </summary>
    public static class VideoScreenGroup
    {
        private static readonly List<PlayerScreenUserInterface> members = new List<PlayerScreenUserInterface>();

        /// <summary>Raised on the UI thread when the group is made, changed or undone.</summary>
        public static event EventHandler Changed;

        /// <summary>Two screens or more: their analysis is one.</summary>
        public static bool Active
        {
            get { return members.Count >= 2 && Lead != null; }
        }

        /// <summary>The screens of the group, in the order of the screens.</summary>
        public static ReadOnlyCollection<PlayerScreenUserInterface> Members
        {
            get { return members.AsReadOnly(); }
        }

        /// <summary>The screen whose video keeps the race of the group (null without a group).</summary>
        public static PlayerScreenUserInterface Lead { get; private set; }

        /// <summary>The screen is in the group (and there is a group).</summary>
        public static bool Contains(PlayerScreenUserInterface screen)
        {
            return screen != null && Active && members.Contains(screen);
        }

        /// <summary>
        /// The group: its screens in the order of the screens and its lead (one of them; else the first). Fewer than two screens: no
        /// group. Raises Changed when something changed.
        /// </summary>
        public static void Set(IList<PlayerScreenUserInterface> screens, PlayerScreenUserInterface lead)
        {
            List<PlayerScreenUserInterface> next = screens != null ? screens.Where(s => s != null && !s.IsDisposed).Distinct().ToList() : new List<PlayerScreenUserInterface>();
            if (next.Count < 2)
                next.Clear();
            PlayerScreenUserInterface nextLead = next.Count == 0 ? null : (lead != null && next.Contains(lead) ? lead : next[0]);
            if (nextLead == Lead && next.SequenceEqual(members))
                return;

            members.Clear();
            members.AddRange(next);
            Lead = nextLead;
            EventHandler handler = Changed;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }

        /// <summary>
        /// The frame of the lead at the moment of a frame of a screen of the group: the same time from the time origin of each video
        /// (the point where the screens are synchronised, e.g. the start signal), the speed of each camera taken into account.
        /// </summary>
        public static long LeadTimestamp(PlayerScreenUserInterface screen, long timestamp)
        {
            if (screen == null || Lead == null || screen == Lead)
                return timestamp;

            return Lead.SwimTimestampFromOrigin(screen.SwimTimeFromOrigin(timestamp));
        }
    }
}
