#region License
/*
Copyright © Joan Charmant 2008.
jcharmant@gmail.com 
 
This file is part of Kinovea.

Kinovea is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License version 2 
as published by the Free Software Foundation.

Kinovea is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Kinovea. If not, see http://www.gnu.org/licenses/.
*/
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Windows.Forms;

using Kinovea.Camera;
using Kinovea.ScreenManager.Languages;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    public partial class ScreenManagerUserInterface : UserControl
    {
        #region Events
        public event EventHandler<FileLoadAskedEventArgs> FileLoadAsked;
        public event EventHandler AutoLaunchAsked;

        /// <summary>(Swim 2.14.0) A screen was dragged onto another: the two change places.</summary>
        public event EventHandler<SwimSwapEventArgs> SwimSwapAsked;
        #endregion
        
        #region Properties
        public bool CommonControlsVisible 
        {
            // In full screen the row is hidden but still drives the screens as before (see SetFullScreen).
            get { return fullScreen ? !memoCommonControlsCollapsed : !splitScreensPanel.Panel2Collapsed; }
        }

        public bool IsTopBottomLayout
        {
            get { return splitScreens.Orientation == Orientation.Horizontal; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000;  // Turn on WS_EX_COMPOSITED
                return cp;
            }
        }
        public bool Closing { get; set; }
        #endregion

        #region Members
        bool initialized = false;
        private ThumbnailViewerContainer thumbnailViewerContainer = new ThumbnailViewerContainer();

        // More than two screens: grid of cells, in the rows that give the biggest videos (BestRows).
        private Panel pnlGrid = new Panel();
        private List<Panel> gridCells = new List<Panel>();
        private int gridCount;
        private const int gridGap = 3;

        // (Swim 2.16.0) The panel of a group of screens: one more cell of the grid after the screens (null without one).
        private Control swimExtraCell;

        // Full screen: only the images of the screens, joined in the grid (the row of the common controls and the borders hidden).
        private bool fullScreen;
        private bool memoCommonControlsCollapsed;

        // (Swim 2.14.0) Big screens: the screens show only their images in the window (the common controls stay).
        private bool bigScreens;
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        #endregion
        
        public ScreenManagerUserInterface()
        {
            log.Debug("Constructing ScreenManagerUserInterface.");
            InitializeComponent();

            BackColor = Color.White;
            Dock = DockStyle.Fill;
            
            InitializeThumbnailsContainer();

            thumbnailViewerContainer.BringToFront();
            pnlScreens.BringToFront();
            pnlScreens.Dock = DockStyle.Fill;

            splitScreens.Orientation = WindowManager.ActiveWindow.DualScreenVerticalLayout ? Orientation.Horizontal : Orientation.Vertical;
            float num = splitScreens.Orientation == Orientation.Vertical ? splitScreens.Width : splitScreens.Height;
            splitScreens.SplitterDistance = (int)(num * WindowManager.ActiveWindow.DualScreenSplitterRatio);

            InitializeGrid();

            initialized = true;
        }
        
        #region Public methods
        public void RefreshUICulture()
        {
            thumbnailViewerContainer.RefreshUICulture();
        }
        public void ShowCommonControls(bool show, Pair<Type, Type> types, CommonControlsPlayers cctrlsPlayers, CommonControlsCapture cctrlsCapture)
        {
            if (fullScreen)
                memoCommonControlsCollapsed = !show;
            else
                splitScreensPanel.Panel2Collapsed = !show;

            if (types == null)
                return;

            splitScreensPanel.Panel2.Controls.Clear();

            if (types.First == typeof(PlayerScreen))
                splitScreensPanel.Panel2.Controls.Add(cctrlsPlayers);
            else
                splitScreensPanel.Panel2.Controls.Add(cctrlsCapture);
        }
        public void ToggleCommonControls()
        {
            if (fullScreen)
                memoCommonControlsCollapsed = !memoCommonControlsCollapsed;
            else
                splitScreensPanel.Panel2Collapsed = !splitScreensPanel.Panel2Collapsed;
        }
        public void OrganizeScreens(List<AbstractScreen> screenList)
        {
            if(screenList.Count == 0)
            {
                pnlScreens.Visible = false;
                thumbnailViewerContainer.Visible = true;
                this.AllowDrop = true;
                ClearLeftScreen();
                ClearRightScreen();
                ClearGrid(true);
                pnlGrid.Visible = false;
                splitScreens.Visible = true;

                if (!Closing)
                    thumbnailViewerContainer.UnhideContent();
            }
            else
            {
                pnlScreens.Visible = true;
                thumbnailViewerContainer.Visible = false;
                this.AllowDrop = false;
                
                thumbnailViewerContainer.HideContent();

                splitScreens.Panel1.Controls.Clear();
                splitScreens.Panel2.Controls.Clear();

                // Full screen: the grid also for one or two screens, the images joined without gaps. (Swim 2.16.0) The panel of a
                // group of screens is one more cell of the grid.
                int cells = screenList.Count + (swimExtraCell != null ? 1 : 0);
                if (cells <= 2 && !fullScreen)
                {
                    ClearGrid(false);
                    pnlGrid.Visible = false;
                    splitScreens.Visible = true;

                    PrepareLeftScreen(screenList[0].UI);
                
                    if(screenList.Count == 2)
                        PrepareRightScreen(screenList[1].UI);
                    else
                        ClearRightScreen();
                }
                else
                {
                    splitScreens.Visible = false;
                    PrepareGrid(screenList);
                    pnlGrid.Visible = true;
                    pnlGrid.BringToFront();
                    LayoutGrid();
                }

                UncoverScreens();
            }
        }
        /// <summary>
        /// Full screen: the row of the common controls and the borders hidden, the screens in the grid without gaps
        /// (OrganizeScreens is called after this). Leaving it puts the row back as it was.
        /// </summary>
        public void SetFullScreen(bool isFullScreen)
        {
            thumbnailViewerContainer.SetFullScreen(isFullScreen);
            if (isFullScreen == fullScreen)
                return;

            if (isFullScreen)
                memoCommonControlsCollapsed = splitScreensPanel.Panel2Collapsed;

            fullScreen = isFullScreen;
            splitScreensPanel.Panel2Collapsed = fullScreen || memoCommonControlsCollapsed;
            splitScreensPanel.BorderStyle = fullScreen ? BorderStyle.None : BorderStyle.FixedSingle;
            UncoverScreens();
        }
        /// <summary>
        /// (Swim 2.14.0) Big screens on or off: the screens show only their images (set by the screen manager, which then organizes
        /// the screens again).
        /// </summary>
        public void SetBigScreens(bool on)
        {
            bigScreens = on;
        }

        /// <summary>(Swim 2.16.0) The panel of a group of screens in one more cell of the grid (null: none); OrganizeScreens places it.</summary>
        public void SetSwimExtraCell(Control cell)
        {
            swimExtraCell = cell;
        }

        /// <summary>
        /// (Swim 2.14.0) A bar of the main window (its toolbar) can lie over the top of the screens: the screens then start below it, so
        /// that the top of each screen (its row with the name of the file) and the top of the videos are seen. Nothing in full screen,
        /// where the bars of the main window are hidden.
        /// </summary>
        private void UncoverScreens()
        {
            try
            {
                SplitterPanel panel = splitScreensPanel.Panel1;
                Form form = FindForm();
                if (form == null || !panel.IsHandleCreated || !form.IsHandleCreated)
                    return;

                Rectangle own = panel.RectangleToScreen(panel.ClientRectangle);
                int covered = 0;
                foreach (Control c in form.Controls)
                {
                    ToolStrip strip = c as ToolStrip;
                    if (strip == null || strip is StatusStrip || !strip.Visible || !strip.IsHandleCreated || strip.Contains(this))
                        continue;

                    Rectangle r = strip.RectangleToScreen(strip.ClientRectangle);
                    if (r.Top <= own.Top && r.Bottom > own.Top && r.Right > own.Left && r.Left < own.Right)
                        covered = Math.Max(covered, r.Bottom - own.Top);
                }

                covered = Math.Max(0, Math.Min(covered, own.Height / 4));
                if (panel.Padding.Top != covered)
                    panel.Padding = new Padding(0, covered, 0, 0);
            }
            catch (Exception e)
            {
                log.Error("Error while placing the screens under the bars of the window.", e);
            }
        }
        public void ToggleDualScreenOrientation()
        {
            float ratio = (float)splitScreens.SplitterDistance / (splitScreens.Orientation == Orientation.Vertical ? splitScreens.Width : splitScreens.Height);
            splitScreens.Orientation = (splitScreens.Orientation == Orientation.Vertical) ? Orientation.Horizontal : Orientation.Vertical;
            splitScreens.SplitterDistance = (int)(ratio * (splitScreens.Orientation == Orientation.Vertical ? splitScreens.Width : splitScreens.Height));

            WindowManager.ActiveWindow.DualScreenVerticalLayout = splitScreens.Orientation == Orientation.Horizontal;
            WindowManager.SaveActiveWindow();
        }
        public string GetBrowserStatusString()
        {
            return thumbnailViewerContainer.GetStatusString();
        }
        #endregion

        private void InitializeThumbnailsContainer()
        {
            thumbnailViewerContainer.Dock = DockStyle.Fill;
            thumbnailViewerContainer.Visible = true;
            thumbnailViewerContainer.FileLoadAsked += (s,e) => {
                if(FileLoadAsked != null)
                    FileLoadAsked(this, e);
            };
            
            this.Controls.Add(thumbnailViewerContainer);
        }
        protected override void OnLoad(EventArgs e)
        {
            log.DebugFormat("In ScreenManager OnLoad");
            if (WindowManager.ActiveWindow.ScreenList.Count > 0)
                AutoLaunchAsked(this, EventArgs.Empty);
        }
        private void pnlScreens_Resize(object sender, EventArgs e)
        {
            // Reposition Common Controls panel so it doesn't take more space than necessary.
            splitScreensPanel.SplitterDistance = pnlScreens.Height - 50;
            UncoverScreens();
        }
        
        #region Screen management
        private void PrepareLeftScreen(UserControl screenUI)
        {
            if (screenUI == null)
                return;

            // (Swim 2.16.0) Out of the grid the screen has its minimum size again.
            PlayerScreenUserInterface player = screenUI as PlayerScreenUserInterface;
            if (player != null)
                player.SwimInGrid(false);

            splitScreens.Panel1Collapsed = false;
            splitScreens.Panel1.AllowDrop = true;
            splitScreens.Panel1.Controls.Add(screenUI);
            screenUI.Dock = DockStyle.Fill;
        }
        private void PrepareRightScreen(UserControl screenUI)
        {
            if (screenUI == null)
                return;

            PlayerScreenUserInterface player = screenUI as PlayerScreenUserInterface;
            if (player != null)
                player.SwimInGrid(false);

            splitScreens.Panel2Collapsed = false;
            splitScreens.Panel2.AllowDrop = true;
            splitScreens.Panel2.Controls.Add(screenUI);
            screenUI.Dock = DockStyle.Fill;
        }
        private void ClearLeftScreen()
        {
            foreach (Control c in splitScreens.Panel1.Controls)
                c.Dispose();

            splitScreens.Panel1.Controls.Clear();
            splitScreens.Panel1Collapsed = true;
            splitScreens.Panel1.AllowDrop = false;
        }
        private void ClearRightScreen()
        {
            foreach (Control c in splitScreens.Panel2.Controls)
                c.Dispose();

            splitScreens.Panel2.Controls.Clear();
            splitScreens.Panel2Collapsed = true;
            splitScreens.Panel2.AllowDrop = false;
        }

        private void InitializeGrid()
        {
            pnlGrid.Dock = DockStyle.Fill;
            pnlGrid.BackColor = SystemColors.ControlDark;
            pnlGrid.Visible = false;
            pnlGrid.Resize += (s, e) => LayoutGrid();
            splitScreensPanel.Panel1.Controls.Add(pnlGrid);
        }

        /// <summary>
        /// Put the screens in the cells of the grid (more than two screens).
        /// </summary>
        private void PrepareGrid(List<AbstractScreen> screenList)
        {
            pnlGrid.SuspendLayout();
            int total = screenList.Count + (swimExtraCell != null ? 1 : 0);

            while (gridCells.Count < total)
            {
                int index = gridCells.Count;
                Panel cell = new Panel();
                cell.BackColor = Color.White;
                cell.AllowDrop = true;
                cell.DragOver += DroppableArea_DragOver;
                cell.DragDrop += (s, e) => Drop(e, index);
                gridCells.Add(cell);
                pnlGrid.Controls.Add(cell);
            }

            for (int i = 0; i < gridCells.Count; i++)
            {
                gridCells[i].Controls.Clear();
                gridCells[i].Visible = i < total;
            }

            for (int i = 0; i < screenList.Count; i++)
            {
                UserControl screenUI = screenList[i].UI;
                if (screenUI == null)
                    continue;

                // (Swim 2.16.0) The screen takes the size of its cell: nothing of its image cut by its minimum width.
                PlayerScreenUserInterface player = screenUI as PlayerScreenUserInterface;
                if (player != null)
                    player.SwimInGrid(true);

                screenUI.Dock = DockStyle.Fill;
                gridCells[i].Controls.Add(screenUI);
            }

            if (swimExtraCell != null)
            {
                swimExtraCell.Dock = DockStyle.Fill;
                gridCells[screenList.Count].Controls.Add(swimExtraCell);
            }

            gridCount = total;
            LayoutGrid();
            pnlGrid.ResumeLayout();
        }

        /// <summary>
        /// Cells of equal size in each row, rows of equal height.
        /// Full screen: also one or two screens (two side by side, or one above the other with the vertical layout),
        /// and no gap between the cells: the images are joined.
        /// </summary>
        private void LayoutGrid()
        {
            if (gridCount < (fullScreen ? 1 : 3))
                return;

            // (Swim 2.16.0) Big screens too: the images joined, nothing between them.
            int gap = fullScreen || bigScreens ? 0 : gridGap;
            Rectangle area = pnlGrid.ClientRectangle;
            if (LayoutGridFitted(area, gap))
                return;

            int[] rows = gridCount == 2 && IsTopBottomLayout ? new int[] { 1, 1 } : BestRows(gridCount, area, gap);
            int cell = 0;
            int top = area.Top;
            for (int r = 0; r < rows.Length; r++)
            {
                int bottom = area.Top + (int)Math.Round((double)area.Height * (r + 1) / rows.Length);
                int height = bottom - top - (r < rows.Length - 1 ? gap : 0);
                int left = area.Left;
                for (int c = 0; c < rows[r] && cell < gridCount; c++)
                {
                    int right = area.Left + (int)Math.Round((double)area.Width * (c + 1) / rows[r]);
                    int width = right - left - (c < rows[r] - 1 ? gap : 0);
                    gridCells[cell].Bounds = new Rectangle(left, top, Math.Max(1, width), Math.Max(1, height));
                    left = right;
                    cell++;
                }

                top = bottom;
            }
        }

        /// <summary>
        /// (Swim 2.16.0) The cells in the shape of their videos (the controls of a screen under its image), joined: the images of all the
        /// rows the same height, the rows in the middle of the window; no black between the images and nothing cut (the panel of a group
        /// of screens in the shape of the first video). Of one, two or three rows the one whose smallest image is the biggest (three 16:9
        /// videos in a 16:9 window: two above and one below, the panel of a group beside it). Not for two screens one above the other, nor
        /// with a screen without a video (the cells of equal size then).
        /// </summary>
        private bool LayoutGridFitted(Rectangle area, int gap)
        {
            if (gridCount < 1 || (gridCount == 2 && IsTopBottomLayout) || area.Width < 20 || area.Height < 20)
                return false;

            List<double> aspects = new List<double>();
            List<int> controls = new List<int>();
            List<int> minimums = new List<int>();
            double first = 0;
            for (int i = 0; i < gridCount; i++)
            {
                Control c = gridCells[i].Controls.Count > 0 ? gridCells[i].Controls[0] : null;
                PlayerScreenUserInterface player = c as PlayerScreenUserInterface;
                if (player != null)
                {
                    if (player.SwimImageAspect <= 0)
                        return false;
                    aspects.Add(player.SwimImageAspect);
                    controls.Add(fullScreen || bigScreens ? 0 : player.SwimControlsHeight);
                    minimums.Add(fullScreen || bigScreens ? 0 : player.SwimMinimumHeight);
                    if (first <= 0)
                        first = player.SwimImageAspect;
                }
                else if (c != null && c == swimExtraCell)
                {
                    aspects.Add(0);
                    controls.Add(0);
                    minimums.Add(0);
                }
                else
                {
                    return false;
                }
            }

            for (int i = 0; i < aspects.Count; i++)
            {
                if (aspects[i] <= 0)
                    aspects[i] = first > 0 ? first : 16.0 / 9;
            }

            // The images of every row the same height (the rows in the middle of the window, the shorter ones with room at their sides):
            // as high as the widest row and the height of the window allow.
            int[] bestRows = null;
            double bestHeight = 0, bestMin = -1;
            for (int rowCount = 1; rowCount <= Math.Min(gridCount, 3); rowCount++)
            {
                int[] rows = new int[rowCount];
                for (int r = 0; r < rowCount; r++)
                    rows[r] = gridCount / rowCount + (r < gridCount % rowCount ? 1 : 0);

                double fixedHeight = gap * (rowCount - 1), height = double.MaxValue, smallest = double.MaxValue;
                int cell = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    double sum = 0;
                    int most = 0;
                    for (int c = 0; c < rows[r]; c++, cell++)
                    {
                        sum += aspects[cell];
                        most = Math.Max(most, controls[cell]);
                    }

                    fixedHeight += most;
                    height = Math.Min(height, (area.Width - gap * (rows[r] - 1)) / Math.Max(0.1, sum));
                }

                height = Math.Min(height, (area.Height - fixedHeight) / rowCount);
                if (height < 10)
                    continue;

                for (int i = 0; i < gridCount; i++)
                    smallest = Math.Min(smallest, height * height * aspects[i]);

                if (bestRows == null || smallest > bestMin * 1.02)
                {
                    bestRows = rows;
                    bestHeight = height;
                    bestMin = smallest;
                }
            }

            if (bestRows == null)
                return false;

            // Around the joined images: black when only the images are shown, the colour of the window else.
            pnlGrid.BackColor = fullScreen || bigScreens ? Color.Black : SystemColors.ControlDark;

            // The rows with their heights, all of them in the middle of the window.
            int image = Math.Max(1, (int)Math.Floor(bestHeight));
            List<int> rowHeights = new List<int>();
            int index = 0;
            int total = gap * (bestRows.Length - 1);
            for (int r = 0; r < bestRows.Length; r++)
            {
                // At least the height of its screens (their keyframes and controls are laid out in it): a lower row would push the
                // bottom of a screen out of its cell. The image then has a little room above and below it, never cut.
                int most = 0, least = 0;
                for (int c = 0; c < bestRows[r]; c++, index++)
                {
                    most = Math.Max(most, controls[index]);
                    least = Math.Max(least, minimums[index]);
                }

                int rowHeight = Math.Max(image + most, least);
                rowHeights.Add(rowHeight);
                total += rowHeight;
            }

            int top = area.Top + Math.Max(0, (area.Height - total) / 2);
            index = 0;
            for (int r = 0; r < bestRows.Length; r++)
            {
                List<int> widths = new List<int>();
                double x = 0;
                int previous = 0;
                for (int c = 0; c < bestRows[r]; c++)
                {
                    x += image * aspects[index + c];
                    int edge = (int)Math.Round(x);
                    widths.Add(Math.Max(1, edge - previous));
                    previous = edge;
                }

                int rowWidth = widths.Sum() + gap * (bestRows[r] - 1);
                int left = area.Left + Math.Max(0, (area.Width - rowWidth) / 2);
                for (int c = 0; c < bestRows[r]; c++, index++)
                {
                    gridCells[index].Bounds = new Rectangle(left, top, widths[c], rowHeights[r]);
                    left += widths[c] + gap;
                }

                top += rowHeights[r] + gap;
            }

            return true;
        }

        /// <summary>
        /// (Swim 2.14.0) The rows of the grid that give the biggest videos. For each way to put the screens in one, two or three rows
        /// (the extra screens in the first rows), the image of each screen is fitted in its cell (its controls take their height, none
        /// when it shows only its image; 16:9 for a screen without a video), and of these the rows whose smallest image is the biggest
        /// (then the biggest images in all). Three 16:9 videos in a 16:9 full screen: two above and one below, each 2.25 times bigger
        /// than in one row; in the window with the controls of each screen the single row stays.
        /// </summary>
        private int[] BestRows(int count, Rectangle area, int gap)
        {
            if (count <= 1)
                return new int[] { Math.Max(1, count) };

            int[] best = null;
            double bestMin = -1, bestTotal = -1;
            for (int rowCount = 1; rowCount <= Math.Min(count, 3); rowCount++)
            {
                int[] rows = new int[rowCount];
                for (int r = 0; r < rowCount; r++)
                    rows[r] = count / rowCount + (r < count % rowCount ? 1 : 0);

                double smallest = double.MaxValue, total = 0;
                int cell = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    double height = (double)area.Height / rowCount - (r < rowCount - 1 ? gap : 0);
                    for (int c = 0; c < rows[r]; c++)
                    {
                        double width = (double)area.Width / rows[r] - (c < rows[r] - 1 ? gap : 0);
                        double aspect = 16.0 / 9;
                        int controls = 0;
                        PlayerScreenUserInterface player = cell < gridCells.Count && gridCells[cell].Controls.Count > 0 ?
                            gridCells[cell].Controls[0] as PlayerScreenUserInterface : null;
                        if (player != null)
                        {
                            if (player.SwimImageAspect > 0)
                                aspect = player.SwimImageAspect;
                            controls = fullScreen || bigScreens ? 0 : player.SwimControlsHeight;
                        }

                        double h = Math.Max(0, height - controls);
                        double w = Math.Min(width, h * aspect);
                        double image = w * (w / aspect);
                        smallest = Math.Min(smallest, image);
                        total += image;
                        cell++;
                    }
                }

                // A clearly bigger smallest image, or about the same and more in all.
                bool better = best == null || smallest > bestMin * 1.02 || (smallest >= bestMin * 0.98 && total > bestTotal * 1.02);
                if (better)
                {
                    best = rows;
                    bestMin = smallest;
                    bestTotal = total;
                }
            }

            return best;
        }

        private void ClearGrid(bool dispose)
        {
            foreach (Panel cell in gridCells)
            {
                if (dispose)
                {
                    foreach (Control c in cell.Controls.Cast<Control>().ToList())
                        c.Dispose();
                }

                cell.Controls.Clear();
                cell.Visible = false;
            }

            gridCount = 0;
        }
        #endregion
        
        #region DragDrop
        private void DroppableArea_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.All;
        }
        private void ScreenManagerUserInterface_DragDrop(object sender, DragEventArgs e)
        {
            Drop(e, -1);
        }
        private void splitScreens_Panel1_DragDrop(object sender, DragEventArgs e)
        {
            Drop(e, 0);
        }
        private void splitScreens_Panel2_DragDrop(object sender, DragEventArgs e)
        {
            Drop(e, 1);
        }
        private void Drop(DragEventArgs e, int target)
        {
            // (Swim 2.14.0) A screen dragged onto another (by its top row or its grip): the two change places.
            if (e.Data.GetDataPresent(typeof(SwimScreenDrag)))
            {
                // (Swim 2.16.0) Not onto the panel of a group of screens.
                if (swimExtraCell != null && target >= 0 && target < gridCells.Count && gridCells[target].Controls.Contains(swimExtraCell))
                    return;

                SwimScreenDrag drag = e.Data.GetData(typeof(SwimScreenDrag)) as SwimScreenDrag;
                if (drag != null && target >= 0 && target != drag.Index && SwimSwapAsked != null)
                    SwimSwapAsked(this, new SwimSwapEventArgs(drag.Index, target));
                return;
            }

            if(e.Data.GetDataPresent(typeof(CameraSummary)))
            {
                CameraSummary summary = (CameraSummary)e.Data.GetData(typeof(CameraSummary));
                if(summary != null)
                    CameraTypeManager.LoadCamera(summary, target);
            }
            else if(e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string filename = (string)e.Data.GetData(DataFormats.StringFormat);
                FileLoadAsked(this, new FileLoadAskedEventArgs(filename, target));
            }
            else if(e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                Array fileArray = (Array)e.Data.GetData(DataFormats.FileDrop);
                if (fileArray != null)
                {
                   string filename = fileArray.GetValue(0).ToString();
                   FileLoadAsked(this, new FileLoadAskedEventArgs(filename, target));
                }
            }
        }
        #endregion

        private void splitScreens_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (!initialized)
                return;

            float denom = splitScreens.Orientation == Orientation.Vertical ? splitScreens.Width : splitScreens.Height;
            WindowManager.ActiveWindow.DualScreenSplitterRatio = (float)splitScreens.SplitterDistance / denom;
            WindowManager.SaveActiveWindow();
        }
    }
}
