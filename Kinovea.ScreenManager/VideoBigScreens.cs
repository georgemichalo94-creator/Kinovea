using System;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.14.0) The big screens: with two or more screens each shows only its image (its own controls hidden, the common controls
    /// of the screens stay), so that the videos fill their places, as in the full screen but in the window. The screen manager sets it
    /// (the button of the common controls); the swim analysis follows it as the full screen: a round button over each video instead of
    /// the bars beside it.
    /// </summary>
    public static class VideoBigScreens
    {
        /// <summary>True while the screens show only their images in the window.</summary>
        public static bool Active { get; private set; }

        /// <summary>Raised on the UI thread when the big screens start or end (Active has the new state).</summary>
        public static event EventHandler ActiveChanged;

        /// <summary>The big screens start (true) or end (false). Called by the screen manager, on the UI thread.</summary>
        public static void Set(bool active)
        {
            if (Active == active)
                return;

            Active = active;
            EventHandler handler = ActiveChanged;
            if (handler != null)
                handler(null, EventArgs.Empty);
        }
    }
}
