#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.26.0) The digital assistant in the menu: «ΒΟΗΘΟΣ» on the bar (before the Google Sheet) and «Ψηφιακός βοηθός» first in Βοήθεια
    /// (F1, which the guide of Kinovea had), its panel on the right of the window, and «Δείξε μου»: the menus of an item opened and the
    /// item lit (its box pulses, «ΕΔΩ» in the column of its keys) until its menu closes. With the classic menu of Kinovea the assistant is
    /// there too (on its light bar, the item only selected).
    /// </summary>
    internal sealed partial class SwimMainMenu : ISwimAssistantHost
    {
        private SwimMenuAssistant assistantButton;
        private ToolStripMenuItem assistantEntry;
        private SwimAssistantPanel assistant;
        private Timer assistantTimer;
        private float assistantTurn;
        private bool assistantBeforeFullScreen;
        private ToolStripMenuItem helpContents;
        private bool helpF1;
        private bool classicAssistant;

        // «Δείξε μου».
        private ToolStripItem spotItem;
        private string spotKeys;
        private bool spotKeysSet;
        private ToolStripDropDown spotDropDown;
        private Timer spotTimer;
        private DateTime spotStart;

        private static string SpotTag
        {
            get { return SwimLang.English ? "HERE" : "ΕΔΩ"; }
        }

        /// <summary>The panel of the assistant (null until it was opened once).</summary>
        internal SwimAssistantPanel Assistant
        {
            get { return assistant; }
        }

        /// <summary>The item lit by «Δείξε μου» now (null: none).</summary>
        internal ToolStripItem Spotted
        {
            get { return spotItem; }
        }

        #region In the menu
        /// <summary>The assistant first in Βοήθεια (F1) and its button for the bar (put on the bar with ΕΛ | EN and the Google Sheet).</summary>
        private void PlaceAssistant(ToolStripMenuItem help, Func<string, ToolStripItem> named)
        {
            assistantEntry = Entry("Ψηφιακός βοηθός", SwimAssistantIcon.Picture(16), ToggleAssistant);
            assistantEntry.Name = "swimAssistant";
            assistantEntry.ShortcutKeys = Keys.F1;
            Tip(assistantEntry, "Ρώτα πού είναι κάτι στο Kinovea: ο βοηθός λέει τα βήματα και δείχνει το σημείο στο μενού.");
            help.DropDownItems.Insert(0, assistantEntry);
            help.DropDownItems.Insert(1, new ToolStripSeparator());
            TakeF1(named("mnuHelpContents") as ToolStripMenuItem);

            assistantButton = new SwimMenuAssistant { Name = "swimAssistantBar", Fresh = !AssistantSeen() };
            SetLabel(assistantButton, "ΒΟΗΘΟΣ");
            Tip(assistantButton, "Ψηφιακός βοηθός (F1): ρώτα πού είναι το καθετί στο Kinovea.");
            assistantButton.Click += (s, e) => Run(ToggleAssistant);
        }

        /// <summary>F1 goes to the assistant (the guide of Kinovea keeps its place in Βοήθεια, without a key).</summary>
        private void TakeF1(ToolStripMenuItem contents)
        {
            if (contents == null || contents.ShortcutKeys != Keys.F1)
                return;

            helpContents = contents;
            contents.ShortcutKeys = Keys.None;
            helpF1 = true;
        }

        /// <summary>The classic menu put back: F1 to the guide of Kinovea again.</summary>
        private void GiveF1Back()
        {
            if (!helpF1 || helpContents == null || helpContents.IsDisposed)
                return;

            helpContents.ShortcutKeys = Keys.F1;
            helpF1 = false;
        }

        /// <summary>
        /// The classic menu of Kinovea (chosen, or put back because the new one could not be laid out): «Ψηφιακός βοηθός» first in its
        /// Help (F1) and the button on the right of its bar, in the language of the analysis.
        /// </summary>
        private void ApplyClassic()
        {
            try
            {
                if (!classicAssistant)
                {
                    NameKernels();
                    ToolStripMenuItem help = TopMenu("mnuHelp");
                    if (help == null)
                        return;

                    classicAssistant = true;
                    assistantEntry = new ToolStripMenuItem { Name = "swimAssistant", Image = SwimAssistantIcon.Picture(16), ShortcutKeys = Keys.F1 };
                    assistantEntry.Click += (s, e) => Run(ToggleAssistant);
                    help.DropDownItems.Insert(0, assistantEntry);
                    help.DropDownItems.Insert(1, new ToolStripSeparator());
                    TakeF1(Find(help.DropDownItems, "mnuHelpContents") as ToolStripMenuItem);

                    assistantButton = new SwimMenuAssistant { Name = "swimAssistantBar", Light = true, Fresh = !AssistantSeen() };
                    assistantButton.Click += (s, e) => Run(ToggleAssistant);
                    strip.Items.Add(assistantButton);
                    SwimLang.Changed += (s, e) => ClassicTexts();
                }

                ClassicTexts();
            }
            catch (Exception e)
            {
                log.Error("Could not put the assistant in the classic menu.", e);
            }
        }

        private void ClassicTexts()
        {
            if (assistantEntry != null && !assistantEntry.IsDisposed)
            {
                assistantEntry.Text = SwimLang.T("Ψηφιακός βοηθός");
                assistantEntry.ToolTipText = SwimLang.T("Ρώτα πού είναι κάτι στο Kinovea: ο βοηθός λέει τα βήματα και δείχνει το σημείο στο μενού.");
            }

            if (assistantButton != null && !assistantButton.IsDisposed)
            {
                assistantButton.Text = SwimLang.T("ΒΟΗΘΟΣ");
                assistantButton.ToolTipText = SwimLang.T("Ψηφιακός βοηθός (F1): ρώτα πού είναι το καθετί στο Kinovea.");
            }

            RefreshAssistant();
        }

        private bool AssistantSeen()
        {
            return startSettings != null && startSettings.AssistantSeen;
        }

        /// <summary>The amber dot of the sign goes once the assistant was opened (kept in the settings).</summary>
        private void MarkSeen()
        {
            if (AssistantSeen())
                return;

            try
            {
                SwimSettings s = Swim().Settings;
                if (s != null && !s.AssistantSeen)
                {
                    s.AssistantSeen = true;
                    s.Save();
                }

                if (startSettings != null)
                    startSettings.AssistantSeen = true;
            }
            catch (Exception e)
            {
                log.Error("Could not keep that the assistant was opened.", e);
            }
        }

        /// <summary>The texts and the answer of the open assistant again (the language, the names of the menu).</summary>
        private void RefreshAssistant()
        {
            if (assistant != null && !assistant.IsDisposed)
                assistant.RefreshTexts();
        }
        #endregion

        #region The panel
        /// <summary>F1, Βοήθεια › Ψηφιακός βοηθός, ΒΟΗΘΟΣ on the bar, ✕ of the panel: open or closed.</summary>
        internal void ToggleAssistant()
        {
            if (assistant != null && !assistant.IsDisposed && assistant.Visible)
                CloseAssistantPanel();
            else
                OpenAssistant();
        }

        /// <summary>
        /// The panel on the right of the window, between its bars and its status bar: docked just after the bars (before the main view
        /// that fills the rest), so that the screens make room for it.
        /// </summary>
        internal void OpenAssistant()
        {
            Form form = strip != null ? strip.FindForm() : null;
            if (form == null)
                return;

            if (assistant == null || assistant.IsDisposed)
            {
                float scale = 1f;
                using (Graphics g = form.CreateGraphics())
                    scale = g.DpiX / 96f;

                assistant = new SwimAssistantPanel(this, scale) { Visible = false };
                form.Controls.Add(assistant);
                Control fill = null;
                foreach (Control c in form.Controls)
                {
                    if (c != assistant && c.Dock == DockStyle.Fill)
                    {
                        fill = c;
                        break;
                    }
                }

                if (fill != null)
                    form.Controls.SetChildIndex(assistant, form.Controls.GetChildIndex(fill) + 1);
            }

            EndSpot();
            assistant.Visible = true;
            if (assistantButton != null)
            {
                assistantButton.Open = true;
                assistantButton.Fresh = false;
                assistantButton.Invalidate();
            }

            MarkSeen();
            if (assistantTimer == null)
            {
                assistantTimer = new Timer { Interval = 40 };
                assistantTimer.Tick += (s, e) => AssistantTick();
            }

            assistantTimer.Start();
            assistant.Opened();
        }

        private void CloseAssistantPanel()
        {
            EndSpot();
            if (assistant != null && !assistant.IsDisposed)
                assistant.Visible = false;
            if (assistantTimer != null)
                assistantTimer.Stop();
            if (assistantButton != null && !assistantButton.IsDisposed)
            {
                assistantButton.Open = false;
                assistantButton.Invalidate();
            }
        }

        /// <summary>The ring of the sign turns (a turn in 9″) while the panel is open.</summary>
        private void AssistantTick()
        {
            assistantTurn = (assistantTurn + 1.6f) % 360f;
            if (assistantButton != null && !assistantButton.IsDisposed)
            {
                assistantButton.Turn = assistantTurn;
                assistantButton.Invalidate();
            }

            if (assistant != null && !assistant.IsDisposed && assistant.Visible)
                assistant.Tick();
        }

        /// <summary>The full screen hides the panel; it comes back after it.</summary>
        public void SetFullScreen(bool on)
        {
            if (assistant == null || assistant.IsDisposed)
                return;

            if (on)
            {
                assistantBeforeFullScreen = assistant.Visible;
                if (assistant.Visible)
                {
                    EndSpot();
                    assistant.Visible = false;
                    if (assistantTimer != null)
                        assistantTimer.Stop();
                }
            }
            else if (assistantBeforeFullScreen)
            {
                assistantBeforeFullScreen = false;
                assistant.Visible = true;
                if (assistantTimer != null)
                    assistantTimer.Start();
            }
        }
        #endregion

        #region «Δείξε μου»
        /// <summary>
        /// The menus of the item opened from the bar and the item lit: the menus that were open close first, a group shows what it has, the
        /// tag «ΕΔΩ» goes in the column of the keys of a command (after its menu opened: a menu sets the keys of its items when it opens).
        /// The light ends when its menu closes (a click anywhere, the command itself), after 20″, or at the next «Δείξε μου».
        /// </summary>
        private bool Spot(ToolStripItem item)
        {
            EndSpot();
            if (item == null || item.IsDisposed || strip == null)
                return false;

            List<ToolStripDropDownItem> chain = new List<ToolStripDropDownItem>();
            for (ToolStripItem i = item.OwnerItem; i != null; i = i.OwnerItem)
            {
                ToolStripDropDownItem d = i as ToolStripDropDownItem;
                if (d == null)
                    return false;
                chain.Insert(0, d);
            }

            ToolStripItem top = chain.Count > 0 ? chain[0] : item;
            if (top.Owner != strip || !top.Available)
                return false;

            CloseMenus();
            foreach (ToolStripDropDownItem d in chain)
                d.ShowDropDown();

            ToolStripDropDownItem group = item as ToolStripDropDownItem;
            if (group != null && group.HasDropDownItems && group.Enabled)
                group.ShowDropDown();

            item.Select();
            spotItem = item;

            ToolStripMenuItem m = item as ToolStripMenuItem;
            if (m != null && chain.Count > 0 && !m.HasDropDownItems)
            {
                spotKeys = m.ShortcutKeyDisplayString;
                spotKeysSet = true;
                m.ShortcutKeyDisplayString = SpotTag;
                ToolStrip owner = m.Owner;
                if (owner != null)
                    owner.PerformLayout();
            }

            renderer.Spot = item;
            renderer.SpotTag = SpotTag;
            renderer.SpotGlow = 1f;
            item.Invalidate();

            spotDropDown = chain.Count > 0 ? chain[chain.Count - 1].DropDown : null;
            if (spotDropDown != null)
                spotDropDown.Closed += SpotClosed;

            spotStart = DateTime.Now;
            if (spotTimer == null)
            {
                spotTimer = new Timer { Interval = 60 };
                spotTimer.Tick += (s, e) => SpotTick();
            }

            spotTimer.Start();
            return true;
        }

        private void SpotClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            EndSpot();
        }

        private void SpotTick()
        {
            if (spotItem == null || spotItem.IsDisposed)
            {
                EndSpot();
                return;
            }

            double t = (DateTime.Now - spotStart).TotalSeconds;
            if (t > 20)
            {
                EndSpot();
                return;
            }

            renderer.SpotGlow = (float)(0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / 1.2));
            spotItem.Invalidate();
        }

        private void EndSpot()
        {
            if (spotTimer != null)
                spotTimer.Stop();
            if (spotDropDown != null)
            {
                spotDropDown.Closed -= SpotClosed;
                spotDropDown = null;
            }

            ToolStripItem old = spotItem;
            spotItem = null;
            renderer.Spot = null;
            if (spotKeysSet)
            {
                spotKeysSet = false;
                ToolStripMenuItem m = old as ToolStripMenuItem;
                if (m != null && !m.IsDisposed)
                    m.ShortcutKeyDisplayString = spotKeys;
            }

            spotKeys = null;
            if (old != null && !old.IsDisposed)
                old.Invalidate();
        }

        /// <summary>The menus open on the bar closed.</summary>
        private void CloseMenus()
        {
            foreach (ToolStripItem i in strip.Items)
            {
                ToolStripDropDownItem d = i as ToolStripDropDownItem;
                if (d != null && d.HasDropDownItems && d.DropDown.Visible)
                    d.HideDropDown();
            }
        }

        /// <summary>The key of an item as its menu shows it (its own, not «ΕΔΩ» while it is lit).</summary>
        private string KeysOf(ToolStripItem item)
        {
            ToolStripMenuItem m = item as ToolStripMenuItem;
            if (m == null)
                return null;

            string shown = spotKeysSet && item == spotItem ? spotKeys : m.ShortcutKeyDisplayString;
            if (!string.IsNullOrEmpty(shown))
                return shown;

            return m.ShortcutKeys != Keys.None ? new KeysConverter().ConvertToInvariantString(m.ShortcutKeys) : null;
        }

        /// <summary>The states of the items of the analysis now (they are set when their menu opens): enabled, keys, ticks.</summary>
        private void RefreshStates()
        {
            if (!Active || spotItem != null)
                return;

            try
            {
                RefreshNeedRace();
                RefreshView();
            }
            catch (Exception e)
            {
                log.Error("Could not refresh the states of the menu for the assistant.", e);
            }
        }
        #endregion

        #region ISwimAssistantHost
        Form ISwimAssistantHost.Window
        {
            get { return strip != null ? strip.FindForm() : null; }
        }

        float ISwimAssistantHost.Turn
        {
            get { return assistantTurn; }
        }

        List<SwimAssistantEntry> ISwimAssistantHost.MenuMap()
        {
            RefreshStates();
            return SwimAssistantMenuMap.Build(strip, KeysOf);
        }

        ToolStripItem ISwimAssistantHost.FindItem(string name)
        {
            return string.IsNullOrEmpty(name) || strip == null ? null : Item(name);
        }

        string ISwimAssistantHost.PathOf(string name)
        {
            ToolStripItem item = string.IsNullOrEmpty(name) || strip == null ? null : Item(name);
            return item != null ? SwimAssistantMenuMap.PathOf(item) : null;
        }

        string ISwimAssistantHost.ShortcutOf(string name)
        {
            ToolStripItem item = string.IsNullOrEmpty(name) || strip == null ? null : Item(name);
            return item != null ? KeysOf(item) : null;
        }

        bool ISwimAssistantHost.Spot(ToolStripItem item)
        {
            try
            {
                return Spot(item);
            }
            catch (Exception e)
            {
                log.Error("Could not show an item of the menu for the assistant.", e);
                EndSpot();
                return false;
            }
        }

        bool ISwimAssistantHost.Run(ToolStripItem item)
        {
            if (item == null || item.IsDisposed)
                return false;

            EndSpot();
            RefreshStates();
            if (!item.Enabled)
                return false;

            Control c = assistant != null && !assistant.IsDisposed && assistant.IsHandleCreated ? (Control)assistant : strip;
            c.BeginInvoke((Action)(() =>
            {
                try
                {
                    item.PerformClick();
                }
                catch (Exception e)
                {
                    log.Error("A command opened by the assistant failed.", e);
                }
            }));
            return true;
        }

        void ISwimAssistantHost.CloseAssistant()
        {
            CloseAssistantPanel();
        }
        #endregion
    }
}
