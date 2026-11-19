#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.26.2) The work space of the main window (the list on the left and the screens) laid out under the bars of the menus and of the
    /// buttons, never under them: the tops of the list (its tabs «Φάκελοι», «Συντομεύσεις», «Κάμερες») and of the screens in sight.
    /// Checked when the window is shown, when a bar changes size or comes back, when a control is added to the window; what was found
    /// and done goes to the log of the program (the types, names and places of the controls: nothing of the videos). And «Κάμερες» on
    /// the toolbar and in Προβολή: the list of the cameras in one click.
    /// </summary>
    internal sealed partial class SwimMainMenu
    {
        /// <summary>The tip of «Κάμερες» (the toolbar) and of «Λίστα καμερών» (Προβολή).</summary>
        private const string CamerasTip = "Η λίστα αριστερά στις κάμερες: διπλό κλικ σε μια κάμερα για ζωντανή εικόνα.";

        private bool workSpaceHooked;
        private bool toolbarHooked;
        private bool keepingWorkSpace;
        private bool workSpacePending;

        /// <summary>Laid out again, the view stayed under the bars: not again at every change of a bar (only when asked, with a «when»).</summary>
        private bool workSpaceStuck;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        /// <summary>The checks of the work space, hooked once: the window shown (and once more after it), the bars, the controls added.</summary>
        private void HookWorkSpace()
        {
            if (strip == null)
                return;

            Form form = strip.FindForm();
            if (form == null)
                return;

            if (!workSpaceHooked)
            {
                workSpaceHooked = true;
                form.Shown += (s, e) =>
                {
                    KeepWorkSpaceUnderBars("shown");
                    LaterWorkSpace(form, "idle");
                };
                form.ControlAdded += (s, e) => LaterWorkSpace(form, null);
                strip.SizeChanged += (s, e) => LaterWorkSpace(form, null);
                strip.VisibleChanged += (s, e) => LaterWorkSpace(form, null);
            }

            if (!toolbarHooked && toolbar != null)
            {
                toolbarHooked = true;
                toolbar.SizeChanged += (s, e) => LaterWorkSpace(form, null);
                toolbar.VisibleChanged += (s, e) => LaterWorkSpace(form, null);
            }
        }

        /// <summary>
        /// The check after what is being done now (the layout of the window, a control being added), never inside it (a layout in the
        /// middle of the layout of the window): one at a time.
        /// </summary>
        private void LaterWorkSpace(Form form, string when)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return;

            if (when == null)
            {
                if (workSpacePending)
                    return;
                workSpacePending = true;
            }

            try
            {
                form.BeginInvoke((Action)(() =>
                {
                    if (when == null)
                        workSpacePending = false;
                    KeepWorkSpaceUnderBars(when);
                }));
            }
            catch (InvalidOperationException)
            {
                if (when == null)
                    workSpacePending = false;
            }
        }

        /// <summary>
        /// The main view of the window (the one that fills what the bars leave, not the MDI area behind it) laid out after the bars and
        /// the panels next to it. A bar before it in the order of the controls is laid out after it, over its top: the view is then put
        /// first again (the panels next to it just after it, in their order) and the window laid out again; a view under the bars is
        /// laid out again too. «when» (shown, idle, cameras, a test): what was found goes to the log; else only what was done.
        /// What was done: null (nothing), "order+layout" or "layout".
        /// </summary>
        internal string KeepWorkSpaceUnderBars(string when)
        {
            if (keepingWorkSpace || strip == null)
                return null;

            Form form = strip.FindForm();
            if (form == null || form.IsDisposed)
                return null;

            Control view = WorkSpace(form);
            if (view == null)
                return null;

            keepingWorkSpace = true;
            try
            {
                string found = when != null ? Describe(form, view) : null;
                int at = form.Controls.GetChildIndex(view);
                bool misplaced = false;
                List<Control> sides = new List<Control>();
                foreach (Control c in form.Controls)
                {
                    if (c == view || !c.Visible || c.Dock == DockStyle.None || c.Dock == DockStyle.Fill)
                        continue;

                    if (form.Controls.GetChildIndex(c) < at)
                        misplaced = true;
                    if ((c.Dock == DockStyle.Left || c.Dock == DockStyle.Right) && !(c is ToolStrip))
                        sides.Add(c);
                }

                string done = null;
                if (misplaced)
                {
                    form.SuspendLayout();
                    try
                    {
                        form.Controls.SetChildIndex(view, 0);
                        for (int i = 0; i < sides.Count; i++)
                            form.Controls.SetChildIndex(sides[i], i + 1);
                    }
                    finally
                    {
                        form.ResumeLayout(false);
                    }

                    done = "order";
                }

                bool under = view.Top < BarsBottom(form, view);
                if (misplaced || (under && (!workSpaceStuck || when != null)))
                {
                    form.PerformLayout();
                    done = done == null ? "layout" : done + "+layout";
                    workSpaceStuck = view.Top < BarsBottom(form, view);
                    if (workSpaceStuck)
                        done += " (still under the bars)";
                }

                if (found != null || done != null)
                {
                    StringBuilder b = new StringBuilder("Swim work space (");
                    b.Append(when ?? "check").Append(')');
                    if (found != null)
                        b.Append(": ").Append(found);
                    if (done != null)
                        b.Append(" | done: ").Append(done).Append(" -> ").Append(Describe(form, view));

                    // (2.26.3) What was done as a warning: the log keeps it without the debug logs of the program.
                    if (done != null)
                        log.Warn(b.ToString());
                    else
                        log.Info(b.ToString());
                }

                return done;
            }
            catch (Exception e)
            {
                log.Error("Could not check the work space under the bars.", e);
                return null;
            }
            finally
            {
                keepingWorkSpace = false;
            }
        }

        /// <summary>The main view of the window: its first control that fills what the bars leave (not the MDI area).</summary>
        private static Control WorkSpace(Form form)
        {
            foreach (Control c in form.Controls)
            {
                if (c.Dock == DockStyle.Fill && !(c is MdiClient))
                    return c;
            }

            return null;
        }

        /// <summary>The bottom of the bars at the top of the window (the visible ones laid out at its top).</summary>
        private static int BarsBottom(Form form, Control view)
        {
            int bottom = form.DisplayRectangle.Top + form.Padding.Top;
            foreach (Control c in form.Controls)
            {
                if (c != view && c.Visible && c.Dock == DockStyle.Top)
                    bottom = Math.Max(bottom, c.Bottom);
            }

            return bottom;
        }

        /// <summary>
        /// What the log is told: the window, its controls in their order (type, name, dock, place), the work space and the list on the
        /// left, its tabs and, when the window is in front, what is at its first tab on the screen.
        /// </summary>
        private static string Describe(Form form, Control view)
        {
            StringBuilder b = new StringBuilder();
            b.AppendFormat(CultureInfo.InvariantCulture, "window {0}x{1} {2}{3}; bars to {4}; controls:", form.ClientSize.Width,
                form.ClientSize.Height, form.WindowState, Form.ActiveForm == form ? " active" : "", BarsBottom(form, view));
            foreach (Control c in form.Controls)
                b.Append(' ').Append(Place(form, c));

            SplitContainer work = Named(view, "splitWorkSpace") as SplitContainer;
            if (work != null)
            {
                b.Append("; work ").Append(Place(form, work));
                if (work.Panel1Collapsed)
                    b.Append(" list closed");
            }

            TabControl tabs = NavigationTabs(view);
            if (tabs == null)
                return b.ToString();

            b.Append("; list ").Append(Place(form, tabs.Parent)).Append("; tabs ").Append(Place(form, tabs));
            if (tabs.TabCount > 0 && tabs.IsHandleCreated)
            {
                Rectangle r = tabs.GetTabRect(0);
                b.AppendFormat(CultureInfo.InvariantCulture, " first tab {0},{1} {2}x{3}, rows {4}", r.X, r.Y, r.Width, r.Height, tabs.RowCount);
                if (Form.ActiveForm == form)
                {
                    Point p = tabs.PointToScreen(new Point(r.Left + r.Width / 2, r.Top + r.Height / 2));
                    Control there = Control.FromChildHandle(WindowFromPoint(p));
                    b.Append(", at it on the screen: ");
                    b.Append(there == null ? "another window" : there == tabs || tabs.Contains(there) ? "the tabs" : Kind(there));
                }
            }

            return b.ToString();
        }

        /// <summary>A control for the log: its index among its siblings, its type and name, its dock and its place in the window.</summary>
        private static string Place(Form form, Control c)
        {
            if (c == null)
                return "-";

            Rectangle r = c.Bounds;
            string where = "";
            if (c.Parent != null && c.Parent != form)
            {
                if (c.Parent.IsHandleCreated && form.IsHandleCreated)
                    r = form.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
                else
                    where = " (in its parent)";
            }

            return string.Format(CultureInfo.InvariantCulture, "#{0} {1} {2} {3},{4} {5}x{6}{7}{8}", c.Parent != null ? c.Parent.Controls.GetChildIndex(c) : -1,
                Kind(c), c.Dock, r.X, r.Y, r.Width, r.Height, where, c.Visible ? "" : " hidden");
        }

        private static string Kind(Control c)
        {
            return string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.GetType().Name + ":" + c.Name;
        }

        /// <summary>The control of a name in a control (and in what it has).</summary>
        private static Control Named(Control root, string name)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Name == name)
                    return c;

                Control inner = Named(c, name);
                if (inner != null)
                    return inner;
            }

            return null;
        }

        /// <summary>The tabs of the list on the left (Φάκελοι, Συντομεύσεις, Κάμερες): the tabs of the browser of the files.</summary>
        private static TabControl NavigationTabs(Control root)
        {
            foreach (Control c in root.Controls)
            {
                TabControl t = c as TabControl;
                if (t != null && t.Parent != null && t.Parent.GetType().Name == "FileBrowserUserInterface")
                    return t;

                TabControl inner = NavigationTabs(c);
                if (inner != null)
                    return inner;
            }

            return null;
        }

        /// <summary>
        /// (2.26.3) The list on the left shown wide enough for its tabs when it was dragged to the side, down to a line (narrower than
        /// 150 points): 260 points wide, at most two fifths of the work space. True when it was widened.
        /// </summary>
        internal static bool WidenList(SplitContainer work)
        {
            const int narrow = 150;
            const int wide = 260;
            if (work == null || work.Panel1Collapsed || work.Orientation != Orientation.Vertical || work.SplitterDistance >= narrow)
                return false;

            int room = work.Width - work.Panel2MinSize - work.SplitterWidth;
            int target = Math.Min(wide, room * 2 / 5);
            if (target < narrow || target <= work.SplitterDistance || target < work.Panel1MinSize)
                return false;

            try
            {
                work.SplitterDistance = target;
                return true;
            }
            catch (ArgumentException e)
            {
                log.Error("Could not widen the list on the left.", e);
                return false;
            }
            catch (InvalidOperationException e)
            {
                log.Error("Could not widen the list on the left.", e);
                return false;
            }
        }

        /// <summary>
        /// «Κάμερες» (the toolbar, Προβολή): the list on the left shown (as Προβολή › Λίστα αρχείων) at its tab of the cameras, the
        /// thumbnails (with no screen open) at the cameras too: a double click on a camera opens it live.
        /// </summary>
        private void ShowCameras()
        {
            Form form = strip.FindForm();
            SplitContainer work = form != null ? Named(form, "splitWorkSpace") as SplitContainer : null;
            if (work != null && work.Panel1Collapsed)
            {
                NotificationCenter.RaiseToggleNavigationPane();
                ToolStripMenuItem pane = Item("mnuToggleNavigationPane") as ToolStripMenuItem;
                if (pane != null)
                    pane.Checked = !work.Panel1Collapsed;
            }

            // (2.26.3) A list dragged to the side, down to a line: wide again (else «Κάμερες» seems to do nothing).
            WidenList(work);

            NotificationCenter.RaiseBrowserContentTypeChanged(this, BrowserContentType.Cameras);

            // One step of the search of the cameras, as a click on the tab does (with screens open the thumbnails do not search): in the
            // window of Kinovea (with its work space).
            if (work != null)
            {
                try
                {
                    Kinovea.Camera.CameraTypeManager.DiscoveryStep();
                }
                catch (Exception e)
                {
                    log.Error("Could not look for the cameras.", e);
                }
            }

            KeepWorkSpaceUnderBars("cameras");
        }
    }
}
