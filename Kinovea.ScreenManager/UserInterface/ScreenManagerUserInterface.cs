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
        #endregion
        
        #region Properties
        public bool CommonControlsVisible 
        {
            get { return !splitScreensPanel.Panel2Collapsed; }
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

        // More than two screens: grid of cells (3: one row, 4: 2+2, 5: 3 on top and 2 below).
        private Panel pnlGrid = new Panel();
        private List<Panel> gridCells = new List<Panel>();
        private int gridCount;
        private const int gridGap = 3;
        private static readonly int[][] gridRows = new int[][] {
            new int[] { }, new int[] { 1 }, new int[] { 2 }, new int[] { 3 }, new int[] { 2, 2 }, new int[] { 3, 2 }, new int[] { 3, 3 } };
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

                if (screenList.Count <= 2)
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
            }
        }
        public void SetFullScreen(bool isFullScreen)
        {
            thumbnailViewerContainer.SetFullScreen(isFullScreen);
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
        }
        
        #region Screen management
        private void PrepareLeftScreen(UserControl screenUI)
        {
            if (screenUI == null)
                return;

            splitScreens.Panel1Collapsed = false;
            splitScreens.Panel1.AllowDrop = true;
            splitScreens.Panel1.Controls.Add(screenUI);
            screenUI.Dock = DockStyle.Fill;
        }
        private void PrepareRightScreen(UserControl screenUI)
        {
            if (screenUI == null)
                return;

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

            while (gridCells.Count < screenList.Count)
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
                gridCells[i].Visible = i < screenList.Count;
            }

            for (int i = 0; i < screenList.Count; i++)
            {
                UserControl screenUI = screenList[i].UI;
                if (screenUI == null)
                    continue;

                screenUI.Dock = DockStyle.Fill;
                gridCells[i].Controls.Add(screenUI);
            }

            gridCount = screenList.Count;
            LayoutGrid();
            pnlGrid.ResumeLayout();
        }

        /// <summary>
        /// Cells of equal size in each row, rows of equal height.
        /// </summary>
        private void LayoutGrid()
        {
            if (gridCount < 3)
                return;

            int[] rows = gridCount < gridRows.Length ? gridRows[gridCount] : new int[] { (gridCount + 1) / 2, gridCount / 2 };
            Rectangle area = pnlGrid.ClientRectangle;
            int cell = 0;
            int top = area.Top;
            for (int r = 0; r < rows.Length; r++)
            {
                int bottom = area.Top + (int)Math.Round((double)area.Height * (r + 1) / rows.Length);
                int height = bottom - top - (r < rows.Length - 1 ? gridGap : 0);
                int left = area.Left;
                for (int c = 0; c < rows[r] && cell < gridCount; c++)
                {
                    int right = area.Left + (int)Math.Round((double)area.Width * (c + 1) / rows[r]);
                    int width = right - left - (c < rows[r] - 1 ? gridGap : 0);
                    gridCells[cell].Bounds = new Rectangle(left, top, Math.Max(1, width), Math.Max(1, height));
                    left = right;
                    cell++;
                }

                top = bottom;
            }
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
