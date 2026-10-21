using System;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// The full screen of the videos: the screens shown (1 to 5) fill the whole monitor edge to edge, without the menus, the
    /// toolbars and the controls of the players. The code of the full screen sets it; the swim analysis follows it (its bars
    /// on the sides of the video become a round button over the video that the analyst moves or hides).
    /// </summary>
    public static class VideoFullScreen
    {
        /// <summary>True while the videos are in full screen.</summary>
        public static bool Active { get; private set; }

        /// <summary>Raised on the UI thread when the full screen starts or ends (Active has the new state).</summary>
        public static event EventHandler ActiveChanged;

        /// <summary>The full screen starts (true) or ends (false). Called by the code that shows it, on the UI thread.</summary>
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
