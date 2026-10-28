using System;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (Swim 2.14.0) A screen dragged onto another (by its top row, or by its grip when only its image is shown): what the drag carries,
    /// the index of the screen dragged. Dropped on another screen, the two change places with their videos.
    /// </summary>
    public sealed class SwimScreenDrag
    {
        public readonly int Index;

        public SwimScreenDrag(int index)
        {
            Index = index;
        }
    }

    /// <summary>(Swim 2.14.0) Two screens to change places (their indexes in the list of the screens).</summary>
    public sealed class SwimSwapEventArgs : EventArgs
    {
        public readonly int First;
        public readonly int Second;

        public SwimSwapEventArgs(int first, int second)
        {
            First = first;
            Second = second;
        }
    }
}
