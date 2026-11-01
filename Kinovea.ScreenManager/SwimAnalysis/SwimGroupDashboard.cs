#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.16.0) The panel of a group of screens in the grid of the screens, in the place of one more screen: the data of the start or the
    /// turn of the moment of the video of the analysis (SwimAnalysisManager.PaintGroupDashboard), drawn again when that video shows another
    /// frame.
    /// </summary>
    internal sealed class SwimGroupDashboard : Control
    {
        private readonly SwimAnalysisManager manager;
        private long followed = long.MinValue;

        public SwimGroupDashboard(SwimAnalysisManager manager)
        {
            this.manager = manager;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = SwimTheme.Back;
            Dock = DockStyle.Fill;
        }

        /// <summary>The video of the analysis shows this frame: drawn again when it is another one.</summary>
        public void Follow(long timestamp)
        {
            if (timestamp == followed)
                return;
            followed = timestamp;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                manager.PaintGroupDashboard(e.Graphics, ClientSize, followed != long.MinValue ? followed : manager.AnalysisTimestamp);
            }
            catch (Exception)
            {
                e.Graphics.Clear(SwimTheme.Back);
            }
        }
    }
}
