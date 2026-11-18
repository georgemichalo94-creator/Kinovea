#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>(2.26.0) What the digital assistant asks of the menu of Kinovea Swim.</summary>
    internal interface ISwimAssistantHost
    {
        Form Window { get; }

        /// <summary>The angle of the ring of the sign (it turns while the assistant is open).</summary>
        float Turn { get; }

        /// <summary>The menu as it is now (its items with a name, their paths and keys).</summary>
        List<SwimAssistantEntry> MenuMap();

        ToolStripItem FindItem(string name);
        string PathOf(string name);
        string ShortcutOf(string name);

        /// <summary>«Δείξε μου»: its menus opened and the item lit (false: not in the menu now).</summary>
        bool Spot(ToolStripItem item);

        /// <summary>«Άνοιξέ το»: the item clicked after its state was asked again (false: not enabled now).</summary>
        bool Run(ToolStripItem item);

        void CloseAssistant();
    }

    /// <summary>
    /// (2.26.0) The digital assistant: a panel on the right of the window (between the bars of the window and its status bar). A question
    /// in its box finds the topic of the task and the items of the menu it names, at once and without internet; the card of the topic has
    /// its steps, its path in the menu as it is now, «Δείξε μου» (its menus open and the item lights up) and «Άνοιξέ το». A question it
    /// does not know can go to Claude (AI pro, with its password): only the question and the names of the menus are sent.
    /// </summary>
    internal sealed class SwimAssistantPanel : Panel
    {
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly ISwimAssistantHost host;
        private readonly float sc;
        private readonly SwimAssistantHeader header;
        private readonly SwimAssistantQuery query;
        private readonly SwimAssistantView view;
        private readonly Label footer;
        private readonly Timer typing;
        private bool settingText;
        private string chosen;
        private SwimAiCall aiCall;
        private string aiQuestion;
        private List<SwimAssistantTopic> available;

        public SwimAssistantPanel(ISwimAssistantHost host, float scale)
        {
            this.host = host;
            sc = Math.Max(1f, scale);
            Result = new SwimAssistantResult();
            Dock = DockStyle.Right;
            Width = P(400);
            BackColor = SwimTheme.Surface;
            ForeColor = SwimTheme.Text;
            Padding = new Padding(1, 0, 0, 0);
            DoubleBuffered = true;
            Name = "swimAssistantPanel";

            view = new SwimAssistantView(this, sc) { Dock = DockStyle.Fill };
            footer = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = P(60),
                Padding = new Padding(P(16), P(10), P(16), P(8)),
                BackColor = SwimTheme.Surface,
                ForeColor = SwimTheme.Dim,
                Font = SwimTheme.Ui(8.25f, FontStyle.Regular),
                UseMnemonic = false
            };
            footer.Paint += (s, e) =>
            {
                using (Pen p = new Pen(SwimTheme.Border))
                    e.Graphics.DrawLine(p, 0, 0, footer.Width, 0);
            };
            query = new SwimAssistantQuery(sc) { Dock = DockStyle.Top };
            header = new SwimAssistantHeader(this, sc) { Dock = DockStyle.Top };

            // Docked in the reverse order: the header at the top, the box under it, the words at the bottom, the answers in between.
            Controls.Add(view);
            Controls.Add(footer);
            Controls.Add(query);
            Controls.Add(header);

            typing = new Timer { Interval = 150 };
            typing.Tick += (s, e) =>
            {
                typing.Stop();
                Search();
            };
            query.Box.TextChanged += (s, e) =>
            {
                if (settingText)
                    return;
                chosen = null;
                typing.Stop();
                typing.Start();
            };
            query.Box.KeyDown += Box_KeyDown;
            query.Box.HandleCreated += (s, e) => SetCue();
            RefreshTexts();
        }

        #region State
        internal SwimAssistantResult Result { get; private set; }

        /// <summary>What happened on «Άνοιξέ το» (green), or why it cannot happen now (amber).</summary>
        internal string Note { get; private set; }
        internal string Warning { get; private set; }

        internal bool AiBusy { get; private set; }
        internal string AiAnswer { get; private set; }
        internal string AiError { get; private set; }
        internal string AiCost { get; private set; }
        internal List<SwimAssistantEntry> AiEntries { get; private set; }

        internal ISwimAssistantHost Host
        {
            get { return host; }
        }

        internal string QueryText
        {
            get { return query.Box.Text; }
        }

        /// <summary>The topics whose place is in the menu now (all of them with the menu of Kinovea Swim), found again at each search.</summary>
        internal List<SwimAssistantTopic> Topics()
        {
            if (available == null)
                available = SwimAssistantKnowledge.Topics.Where(t => string.IsNullOrEmpty(t.Target) || host.FindItem(t.Target) != null).ToList();
            return available;
        }

        internal string X(string text, SwimAssistantTopic topic)
        {
            return SwimAssistantKnowledge.Expand(text, topic, host.PathOf);
        }

        internal int P(float v)
        {
            return (int)Math.Round(v * sc);
        }
        #endregion

        #region Commands
        /// <summary>The panel was shown: the answer for what is in the box, the box ready to type.</summary>
        public void Opened()
        {
            Search();
            FocusQuery();
        }

        public void FocusQuery()
        {
            try
            {
                query.Box.Focus();
                query.Box.SelectAll();
            }
            catch (Exception e)
            {
                log.Error("Could not give the focus to the box of the assistant.", e);
            }
        }

        /// <summary>A question in the box and its answer at once (the chips; the checks).</summary>
        internal void Ask(string text)
        {
            settingText = true;
            try
            {
                query.Box.Text = text ?? "";
                query.Box.SelectionStart = query.Box.Text.Length;
            }
            finally
            {
                settingText = false;
            }

            chosen = null;
            Search();
        }

        /// <summary>A topic chosen (a chip, a related one, a guess): its card, with its name in the box.</summary>
        internal void Pick(SwimAssistantTopic topic)
        {
            if (topic == null)
                return;

            settingText = true;
            try
            {
                query.Box.Text = topic.ChipText;
                query.Box.SelectionStart = query.Box.Text.Length;
            }
            finally
            {
                settingText = false;
            }

            chosen = topic.Id;
            Search();
        }

        internal void Search()
        {
            typing.Stop();
            string text = query.Box.Text;
            available = null;
            List<SwimAssistantTopic> topics = Topics();
            List<SwimAssistantEntry> map;
            try
            {
                map = host.MenuMap();
            }
            catch (Exception e)
            {
                log.Error("Could not read the menu for the assistant.", e);
                map = new List<SwimAssistantEntry>();
            }

            SwimAssistantResult r = SwimAssistantSearch.Find(text, topics, map);
            if (chosen != null)
            {
                SwimAssistantTopic t = topics.FirstOrDefault(x => x.Id == chosen);
                if (t != null)
                {
                    r.Topic = t;
                    r.Guesses.Clear();
                    r.Entries = r.Entries.Where(x => x.Name != t.Target).ToList();
                }
            }

            Result = r;
            Note = null;
            Warning = null;
            if (!AiBusy && aiQuestion != text.Trim())
                ClearAi();
            view.Top0();
            view.Invalidate();
        }

        /// <summary>«Δείξε μου» for the topic of the card.</summary>
        internal void ShowTopic()
        {
            SwimAssistantTopic t = Result.Topic;
            if (t == null)
                return;

            Note = null;
            Warning = null;
            ToolStripItem item = host.FindItem(t.Target);
            if (item == null || !host.Spot(item))
                Warning = SwimAssistantKnowledge.L("Δεν είναι στο μενού τώρα.", "It is not in the menu now.");
            view.Invalidate();
        }

        internal void ShowEntry(SwimAssistantEntry entry)
        {
            if (entry == null)
                return;

            Note = null;
            Warning = null;
            if (entry.Item == null || entry.Item.IsDisposed || !host.Spot(entry.Item))
                Warning = SwimAssistantKnowledge.L("Δεν είναι στο μενού τώρα.", "It is not in the menu now.");
            view.Invalidate();
        }

        /// <summary>«Άνοιξέ το»: the command of the topic (a group of the menu opens instead).</summary>
        internal void OpenTopic()
        {
            SwimAssistantTopic t = Result.Topic;
            if (t == null || !t.CanOpen)
                return;

            Note = null;
            Warning = null;
            ToolStripItem item = host.FindItem(t.Target);
            ToolStripDropDownItem group = item as ToolStripDropDownItem;
            if (item == null)
            {
                Warning = SwimAssistantKnowledge.L("Δεν είναι στο μενού τώρα.", "It is not in the menu now.");
            }
            else if (group != null && group.HasDropDownItems)
            {
                host.Spot(item);
            }
            else if (host.Run(item))
            {
                Note = SwimAssistantKnowledge.L("Άνοιξε: ", "Opened: ") + host.PathOf(t.Target);
            }
            else
            {
                Warning = NeedsText(t);
            }

            view.Invalidate();
        }

        private string NeedsText(SwimAssistantTopic t)
        {
            if (t.Needs == "analysis")
                return X(SwimAssistantKnowledge.L("Τώρα δεν γίνεται: θέλει ανοιχτό βίντεο με την ανάλυσή του ({path:swimRacePanel}).",
                    "Not now: it needs an open video with its analysis ({path:swimRacePanel})."), t);
            if (t.Needs == "video")
                return X(SwimAssistantKnowledge.L("Τώρα δεν γίνεται: άνοιξε πρώτα ένα βίντεο ({path:mnuOpenFile}).",
                    "Not now: open a video first ({path:mnuOpenFile})."), t);
            return SwimAssistantKnowledge.L("Τώρα δεν γίνεται (ανενεργό).", "Not now (disabled).");
        }

        private void Box_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (typing.Enabled || chosen == null)
                    Search();
                if (Result.Topic != null)
                    ShowTopic();
                else if (Result.Entries.Count > 0)
                    ShowEntry(Result.Entries[0]);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                if (query.Box.TextLength > 0)
                    Ask("");
                else
                    host.CloseAssistant();
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown)
            {
                view.ScrollBy(e.KeyCode == Keys.Down ? P(48) : view.Height - P(40));
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp)
            {
                view.ScrollBy(e.KeyCode == Keys.Up ? -P(48) : -(view.Height - P(40)));
                e.Handled = true;
            }
        }

        /// <summary>The language changed (or the names of the menu): the texts and the answer again.</summary>
        public void RefreshTexts()
        {
            footer.Text = SwimAssistantKnowledge.L(
                "Απαντά αμέσως και χωρίς internet, από τα μενού του Kinovea Swim. «Δείξε μου»: ανοίγει το μενού και ανάβει το σημείο.",
                "Answers at once and without internet, from the menus of Kinovea Swim. «Show me»: opens the menu and lights up the place.");
            SetCue();
            header.Invalidate();
            query.Invalidate();
            if (IsHandleCreated)
                Search();
        }

        private void SetCue()
        {
            try
            {
                if (query.Box.IsHandleCreated)
                    SendMessage(query.Box.Handle, EmSetCueBanner, (IntPtr)1,
                        SwimAssistantKnowledge.L("π.χ. πού είναι η εξαγωγή βίντεο;", "e.g. where is the video export?"));
            }
            catch (Exception e)
            {
                log.Error("Could not set the hint of the box of the assistant.", e);
            }
        }

        /// <summary>A step of the animation (the ring of the sign turns, the dots of a question to Claude).</summary>
        public void Tick()
        {
            header.InvalidateSign();
            if (AiBusy)
                view.Invalidate();
        }
        #endregion

        #region AI pro
        /// <summary>
        /// A free question to Claude with AI pro: its password (once in a session), its key, the limit of the month. Only the question and
        /// the map of the menu go (no name, no analysis, no file).
        /// </summary>
        internal void AskAi()
        {
            string question = query.Box.Text.Trim();
            if (question.Length == 0 || AiBusy)
                return;

            Form owner = host.Window;
            try
            {
                if (!SwimAiGate.Unlocked)
                {
                    using (FormSwimAiPassword f = new FormSwimAiPassword())
                    {
                        if (f.ShowDialog(owner) != DialogResult.OK || !SwimAiGate.Unlocked)
                            return;
                    }
                }

                // The settings of the analysis when it is used, else those of the file (the analysis is not started for a question).
                SwimAnalysisManager manager = SwimAnalysisManager.Current;
                SwimSettings settings = manager != null && manager.Settings != null ? manager.Settings : SwimSettings.Load();
                if (settings == null)
                    return;

                string key = SwimAiKey.Unprotect(settings.AiProKey);
                if (string.IsNullOrEmpty(key))
                {
                    using (FormSwimAiKey f = new FormSwimAiKey(false))
                    {
                        if (f.ShowDialog(owner) != DialogResult.OK || string.IsNullOrEmpty(f.Key))
                            return;
                        settings.AiProKey = SwimAiKey.Protect(f.Key);
                        settings.Save();
                        key = f.Key;
                    }
                }

                List<SwimAssistantEntry> map = host.MenuMap();
                string model = SwimAiModels.Sonnet;
                string system = SwimAssistantAi.SystemPrompt(map, Topics(), host.PathOf, host.ShortcutOf);
                long tokens;
                double estimate = SwimAiModels.Estimate(model, null, system.Length + question.Length, SwimAssistantAi.AnswerTokens, out tokens);
                SwimAiUsage month = SwimAiBook.Month(settings.AiProUsage, DateTime.Now);
                if (month.Usd + estimate > settings.AiProMonthLimit)
                {
                    AiError = string.Format(CultureInfo.InvariantCulture, SwimAssistantKnowledge.L(
                        "Πέρασε το όριο του μήνα του AI pro ({0}· ξοδεύτηκαν {1}). Δεν στάλθηκε τίποτα.",
                        "The limit of the month of AI pro is reached ({0}; spent {1}). Nothing was sent."),
                        SwimAiModels.Dollars(settings.AiProMonthLimit), SwimAiModels.Dollars(month.Usd));
                    view.Invalidate();
                    return;
                }

                ClearAi();
                AiBusy = true;
                aiQuestion = question;
                view.Invalidate();
                SwimAiCall call = new SwimAiCall();
                aiCall = call;
                call.Start(key, SwimAssistantAi.Body(question, model, system), reply =>
                {
                    if (aiCall != call)
                        return;
                    aiCall = null;
                    AiBusy = false;
                    try
                    {
                        SwimAiBook.Add(settings.AiProUsage, DateTime.Now, reply);
                        settings.Save();
                    }
                    catch (Exception e)
                    {
                        log.Error("Could not keep the cost of a question of the assistant.", e);
                    }

                    string answer = SwimAssistantAi.AnswerOf(reply);
                    if (answer == null)
                    {
                        AiError = !string.IsNullOrEmpty(reply.Error) ? reply.Error : SwimAssistantKnowledge.L("Ο Claude δεν απάντησε.", "Claude did not answer.");
                    }
                    else
                    {
                        AiAnswer = answer;
                        AiEntries = SwimAssistantAi.PathsOf(reply, answer, map);
                        AiCost = string.Format(CultureInfo.InvariantCulture, "≈ {0} · {1}", SwimAiModels.Dollars(reply.Usd), SwimAiModels.Short(reply.Model));
                    }

                    if (!IsDisposed)
                        view.Invalidate();
                });
            }
            catch (Exception e)
            {
                log.Error("Could not ask Claude for the assistant.", e);
                AiBusy = false;
                AiError = e.Message;
                view.Invalidate();
            }
        }

        private void ClearAi()
        {
            AiAnswer = null;
            AiError = null;
            AiCost = null;
            AiEntries = null;
            aiQuestion = null;
        }
        #endregion

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(SwimTheme.Border))
                e.Graphics.DrawLine(p, 0, 0, 0, Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                typing.Dispose();
                if (aiCall != null)
                {
                    aiCall.Cancel();
                    aiCall = null;
                }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>(2.26.0) The top of the assistant: its sign (the ring turns), its name, ✕.</summary>
    internal sealed class SwimAssistantHeader : Control
    {
        private readonly SwimAssistantPanel panel;
        private readonly float sc;
        private bool closeHover;
        private readonly ToolTip tip = new ToolTip();

        public SwimAssistantHeader(SwimAssistantPanel panel, float sc)
        {
            this.panel = panel;
            this.sc = sc;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Height = P(66);
            BackColor = SwimTheme.Surface;
        }

        private int P(float v)
        {
            return (int)Math.Round(v * sc);
        }

        private Rectangle Sign
        {
            get { return new Rectangle(P(14), P(13), P(40), P(40)); }
        }

        private Rectangle CloseBox
        {
            get { return new Rectangle(Width - P(46), P(17), P(32), P(32)); }
        }

        public void InvalidateSign()
        {
            Rectangle r = Sign;
            r.Inflate(P(4), P(4));
            Invalidate(r);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.Surface);
            SwimAssistantIcon.Draw(g, Sign, panel.Host.Turn, SwimTheme.AccentPale, true, 0.7f);

            int x = Sign.Right + P(12);
            SwimAssistantDraw.Caps(g, SwimAssistantKnowledge.L("ΒΟΗΘΟΣ", "ASSISTANT"), SwimTheme.Ui(8.5f, FontStyle.Bold), SwimTheme.Accent, x, P(15), P(3), true);
            TextRenderer.DrawText(g, SwimAssistantKnowledge.L("Ρώτησε ό,τι θες για το Kinovea", "Ask anything about Kinovea"), SwimTheme.Ui(10.5f, FontStyle.Regular),
                new Rectangle(x, P(31), Math.Max(10, CloseBox.Left - x - P(6)), P(24)), SwimTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            Rectangle c = CloseBox;
            SwimAssistantDraw.Box(g, c, P(5), closeHover ? SwimTheme.RaisedHover : SwimTheme.Surface, closeHover ? SwimTheme.Accent : SwimTheme.Border, 1f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(SwimTheme.AccentLight, Math.Max(1.5f, 1.6f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                int k = P(6);
                Point m = new Point(c.X + c.Width / 2, c.Y + c.Height / 2);
                g.DrawLine(p, m.X - k, m.Y - k, m.X + k, m.Y + k);
                g.DrawLine(p, m.X + k, m.Y - k, m.X - k, m.Y + k);
            }

            using (Pen p = new Pen(SwimTheme.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = CloseBox.Contains(e.Location);
            if (hover != closeHover)
            {
                closeHover = hover;
                Cursor = hover ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, hover ? SwimAssistantKnowledge.L("Κλείσιμο (F1)", "Close (F1)") : null);
                Invalidate(CloseBox);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (closeHover)
            {
                closeHover = false;
                Cursor = Cursors.Default;
                Invalidate(CloseBox);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CloseBox.Contains(e.Location))
                panel.Host.CloseAssistant();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>(2.26.0) The box of the question: its name, its frame (lit while typing), the magnifier, the text box itself.</summary>
    internal sealed class SwimAssistantQuery : Panel
    {
        private readonly float sc;
        public readonly TextBox Box;

        public SwimAssistantQuery(float sc)
        {
            this.sc = sc;
            DoubleBuffered = true;
            BackColor = SwimTheme.Surface;
            Height = P(76);
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = SwimTheme.Input,
                ForeColor = SwimTheme.Text,
                Font = SwimTheme.Ui(10.5f, FontStyle.Regular),
                Name = "swimAssistantQuestion"
            };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Resize += (s, e) => PlaceBox();
            PlaceBox();
        }

        private int P(float v)
        {
            return (int)Math.Round(v * sc);
        }

        private Rectangle Frame
        {
            get { return new Rectangle(P(16), P(28), Math.Max(P(60), Width - P(32)), P(40)); }
        }

        private void PlaceBox()
        {
            Rectangle f = Frame;
            Box.Location = new Point(f.X + P(34), f.Y + (f.Height - Box.Height) / 2);
            Box.Width = Math.Max(P(20), f.Width - P(44));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.Surface);
            SwimAssistantDraw.Caps(g, SwimAssistantKnowledge.L("ΕΡΩΤΗΣΗ", "QUESTION"), SwimTheme.Ui(7.5f, FontStyle.Bold), SwimTheme.Dim, P(16), P(9), P(2), true);
            Rectangle f = Frame;
            bool focused = Box.Focused;
            if (focused)
            {
                Rectangle halo = f;
                halo.Inflate(P(2), P(2));
                SwimAssistantDraw.Box(g, halo, P(8), Color.Transparent, Color.FromArgb(70, SwimTheme.Accent), Math.Max(2f, 2f * sc));
            }

            SwimAssistantDraw.Box(g, f, P(6), SwimTheme.Input, focused ? SwimTheme.Accent : SwimTheme.Border, 1f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(SwimTheme.AccentLight, Math.Max(1.4f, 1.6f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cx = f.X + P(15), cy = f.Y + f.Height / 2f - P(1), r = P(5.5f);
                g.DrawEllipse(p, cx - r, cy - r, 2 * r, 2 * r);
                g.DrawLine(p, cx + r * 0.72f, cy + r * 0.72f, cx + r * 1.65f, cy + r * 1.65f);
            }
        }
    }

    /// <summary>(2.26.0) The answers of the assistant, drawn: the chips, the card of the topic, the items of the menu, Claude.</summary>
    internal sealed class SwimAssistantView : Control
    {
        private sealed class Hot
        {
            public string Id;
            public Rectangle Box;
            public Action Act;
            public string Tip;
        }

        private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;
        private const TextFormatFlags One = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

        /// <summary>The flags to measure one line (never with the ellipsis: a text measured in no room would come back as "Α…").</summary>
        private const TextFormatFlags Fit = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

        /// <summary>The flags of a text centred in a box (the chips, the buttons, the keys): measured with them too.</summary>
        private const TextFormatFlags Centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        private readonly SwimAssistantPanel panel;
        private readonly float sc;
        private readonly List<Hot> hots = new List<Hot>();
        private readonly ToolTip tip = new ToolTip();
        private string hover;
        private int scroll;
        private int contentHeight;

        public SwimAssistantView(SwimAssistantPanel panel, float sc)
        {
            this.panel = panel;
            this.sc = sc;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = SwimTheme.Surface;
        }

        private int P(float v)
        {
            return (int)Math.Round(v * sc);
        }

        private static Font Caption { get { return SwimTheme.Ui(7.5f, FontStyle.Bold); } }
        private static Font Body { get { return SwimTheme.Ui(9.75f, FontStyle.Regular); } }
        private static Font Strong { get { return SwimTheme.Ui(9.75f, FontStyle.Bold); } }
        private static Font Small { get { return SwimTheme.Ui(8.5f, FontStyle.Regular); } }
        private static Font Title { get { return SwimTheme.Display(13f); } }
        private static Font ButtonFont { get { return SwimTheme.Ui(9.5f, FontStyle.Bold); } }

        public void Top0()
        {
            scroll = 0;
        }

        public void ScrollBy(int dy)
        {
            int max = Math.Max(0, contentHeight - Height);
            int s = Math.Max(0, Math.Min(max, scroll + dy));
            if (s != scroll)
            {
                scroll = s;
                Invalidate();
            }
        }

        #region Paint
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SwimTheme.Surface);
            hots.Clear();
            GraphicsState state = g.Save();
            g.TranslateTransform(0, -scroll);
            int height;
            try
            {
                height = Content(g);
            }
            finally
            {
                g.Restore(state);
            }

            contentHeight = height;
            int max = Math.Max(0, contentHeight - Height);
            if (scroll > max)
            {
                scroll = max;
                Invalidate();
                return;
            }

            if (contentHeight > Height && Height > 0)
            {
                int track = Height - P(8);
                int thumb = Math.Max(P(28), track * Height / contentHeight);
                int y = P(4) + (max > 0 ? (track - thumb) * scroll / max : 0);
                SwimAssistantDraw.Box(g, new Rectangle(Width - P(6), y, P(3), thumb), P(1), SwimTheme.Faint, SwimTheme.Faint, 1f);
            }
        }

        private int Content(Graphics g)
        {
            SwimAssistantResult r = panel.Result;
            int x = P(16);
            int w = Math.Max(P(120), Width - P(32) - P(4));
            int y = P(12);

            y = Chips(g, x, y, w, r) + P(14);

            if (!r.Asked && r.Topic == null)
                y = Welcome(g, x, y, w) + P(14);

            if (r.Topic != null)
            {
                int h = Card(g, x, y, w, r.Topic, false);
                SwimAssistantDraw.Box(g, new Rectangle(x, y, w, h), P(8), SwimTheme.Raised, SwimTheme.Border, 1f);
                Card(g, x, y, w, r.Topic, true);
                y += h + P(16);
            }

            if (r.Entries.Count > 0)
            {
                y = Section(g, SwimAssistantKnowledge.L(r.Topic != null ? "ΚΑΙ ΣΤΟ ΜΕΝΟΥ" : "ΣΤΟ ΜΕΝΟΥ", r.Topic != null ? "ALSO IN THE MENU" : "IN THE MENU"), x, y);
                for (int i = 0; i < r.Entries.Count; i++)
                    y = Row(g, x, y, w, r.Entries[i], "entry:" + i) + P(4);
                y += P(12);
            }

            if (r.Topic != null && r.Topic.Related.Length > 0)
            {
                List<SwimAssistantTopic> related = r.Topic.Related.Select(id => panel.Topics().FirstOrDefault(x2 => x2.Id == id)).Where(x2 => x2 != null).ToList();
                if (related.Count > 0)
                {
                    y = Section(g, SwimAssistantKnowledge.L("ΣΧΕΤΙΚΑ", "RELATED"), x, y);
                    foreach (SwimAssistantTopic t in related)
                        y = Link(g, x, y, w, t.Title, "rel:" + t.Id, () => panel.Pick(t)) + P(2);
                    y += P(12);
                }
            }

            if (r.NoMatch)
            {
                int h = NoMatch(g, x, y, w, r, false);
                SwimAssistantDraw.Box(g, new Rectangle(x, y, w, h), P(8), SwimTheme.Raised, SwimTheme.Border, 1f);
                NoMatch(g, x, y, w, r, true);
                y += h + P(16);
            }

            if (r.Asked)
                y = Ai(g, x, y, w);

            return y + P(10);
        }

        private int Chips(Graphics g, int x, int y, int w, SwimAssistantResult r)
        {
            int cx = x, cy = y, h = P(28);
            foreach (SwimAssistantTopic t in panel.Topics().Where(x2 => x2.IsChip))
            {
                string text = t.ChipText;
                int tw = TextRenderer.MeasureText(g, text, Small, Size.Empty, Centre).Width + P(16);
                if (cx > x && cx + tw > x + w)
                {
                    cx = x;
                    cy += h + P(6);
                }

                Rectangle box = new Rectangle(cx, cy, tw, h);
                string id = "chip:" + t.Id;
                bool on = r.Topic == t;
                bool hot = hover == id;
                SwimAssistantDraw.Box(g, box, h / 2, on ? SwimTheme.AccentDark : hot ? SwimTheme.RaisedHover : SwimTheme.Raised, on || hot ? SwimTheme.Accent : SwimTheme.Border, 1f);
                TextRenderer.DrawText(g, text, Small, box, on ? SwimTheme.AccentPale : SwimTheme.AccentLight,
                    Centre);
                SwimAssistantTopic topic = t;
                Add(id, box, () => panel.Pick(topic), null);
                cx += tw + P(6);
            }

            return cy + h;
        }

        private int Welcome(Graphics g, int x, int y, int w)
        {
            string text = SwimAssistantKnowledge.L(
                "Γράψε τι θέλεις να κάνεις, π.χ. «εξαγωγή βίντεο», «συγχρονισμός», «αποστολή στον προπονητή», ή διάλεξε ένα θέμα από τα παραπάνω.",
                "Write what you want to do, e.g. «export a video», «sync», «send to the coach», or choose a topic above.");
            string more = SwimAssistantKnowledge.L(
                "Με «Δείξε μου» ανοίγει το μενού και ανάβει το σωστό σημείο. F1: ανοίγει και κλείνει ο βοηθός.",
                "«Show me» opens the menu and lights up the right place. F1 opens and closes the assistant.");
            int pad = P(14);
            int iw = w - 2 * pad;
            int h1 = TextRenderer.MeasureText(g, text, Body, new Size(iw, int.MaxValue), Wrap).Height;
            int h2 = TextRenderer.MeasureText(g, more, Small, new Size(iw, int.MaxValue), Wrap).Height;
            int h = pad + h1 + P(8) + h2 + pad;
            SwimAssistantDraw.Box(g, new Rectangle(x, y, w, h), P(8), SwimTheme.Raised, SwimTheme.Border, 1f);
            TextRenderer.DrawText(g, text, Body, new Rectangle(x + pad, y + pad, iw, h1), SwimTheme.Text, Wrap);
            TextRenderer.DrawText(g, more, Small, new Rectangle(x + pad, y + pad + h1 + P(8), iw, h2), SwimTheme.Dim, Wrap);
            return y + h;
        }

        /// <summary>The card of a topic (measured first without drawing, then drawn over its box).</summary>
        private int Card(Graphics g, int x0, int y0, int w0, SwimAssistantTopic t, bool paint)
        {
            int pad = P(16);
            int x = x0 + pad, w = w0 - 2 * pad, y = y0 + pad;

            y = Caps(g, SwimAssistantKnowledge.L("ΑΠΑΝΤΗΣΗ", "ANSWER"), x, y, SwimTheme.Dim, paint) + P(6);
            y = Para(g, t.Title, Title, x, y, w, SwimTheme.Text, paint) + P(6);
            string lead = panel.X(t.LeadText, t);
            if (lead.Length > 0)
                y = Para(g, lead, Body, x, y, w, SwimTheme.AccentPale, paint) + P(12);

            string path = panel.Host.PathOf(t.Target);
            if (!string.IsNullOrEmpty(path))
            {
                y = Caps(g, SwimAssistantKnowledge.L("ΠΟΥ ΤΟ ΒΡΙΣΚΕΙΣ", "WHERE IT IS"), x, y, SwimTheme.Dim, paint) + P(6);
                y = PathChips(g, path, x, y, w, paint) + P(12);
            }

            if (t.KeyList)
            {
                y = KeyRows(g, x, y, w, paint) + P(8);
            }
            else
            {
                string[] steps = t.StepTexts;
                for (int i = 0; i < steps.Length; i++)
                    y = Step(g, i + 1, panel.X(steps[i], t), x, y, w, paint) + P(8);
            }

            string key = panel.Host.ShortcutOf(t.Target);
            if (!string.IsNullOrEmpty(key) && !t.KeyList)
            {
                string label = SwimAssistantKnowledge.L("Συντόμευση:", "Shortcut:");
                int lw = TextRenderer.MeasureText(g, label, Small, Size.Empty, Fit).Width;
                int kh = P(22);
                if (paint)
                    TextRenderer.DrawText(g, label, Small, new Rectangle(x, y, lw, kh), SwimTheme.Dim, One);
                KeyCap(g, key, x + lw + P(6), y, kh, paint);
                y += kh + P(10);
            }

            // The buttons.
            int bh = P(38);
            int bx = x;
            string show = SwimAssistantKnowledge.L("Δείξε μου", "Show me");
            int sw = TextRenderer.MeasureText(g, show, ButtonFont, Size.Empty, Fit).Width + P(46);
            Rectangle sb = new Rectangle(bx, y, sw, bh);
            if (paint)
            {
                bool hot = hover == "show";
                SwimAssistantDraw.Box(g, sb, P(6), hot ? SwimTheme.AccentLight : SwimTheme.Accent, hot ? SwimTheme.AccentPale : SwimTheme.Accent, 1f);
                SwimAssistantDraw.Target(g, new Rectangle(sb.X + P(12), sb.Y + (bh - P(16)) / 2, P(16), P(16)), SwimTheme.Back, sc);
                TextRenderer.DrawText(g, show, ButtonFont, new Rectangle(sb.X + P(34), sb.Y, sb.Width - P(40), bh), SwimTheme.Back, One);
                Add("show", sb, panel.ShowTopic, SwimAssistantKnowledge.L("Ανοίγει το μενού και ανάβει το σημείο (Enter)", "Opens the menu and lights up the place (Enter)"));
            }

            bx += sw + P(8);
            ToolStripItem item = panel.Host.FindItem(t.Target);
            ToolStripDropDownItem group = item as ToolStripDropDownItem;
            if (t.CanOpen && item != null && (group == null || !group.HasDropDownItems))
            {
                string open = SwimAssistantKnowledge.L("Άνοιξέ το", "Open it");
                int ow = TextRenderer.MeasureText(g, open, ButtonFont, Size.Empty, Centre).Width + P(28);
                if (bx + ow > x + w)
                {
                    bx = x;
                    y += bh + P(8);
                }

                Rectangle ob = new Rectangle(bx, y, ow, bh);
                if (paint)
                {
                    bool hot = hover == "open";
                    SwimAssistantDraw.Box(g, ob, P(6), hot ? SwimTheme.RaisedHover : SwimTheme.Raised, hot ? SwimTheme.Accent : SwimTheme.Border, 1f);
                    TextRenderer.DrawText(g, open, ButtonFont, ob, SwimTheme.AccentLight,
                        Centre);
                    Add("open", ob, panel.OpenTopic, null);
                }
            }

            y += bh;
            string note = panel.Note ?? panel.Warning;
            if (!string.IsNullOrEmpty(note))
                y = Para(g, note, Small, x, y + P(10), w, panel.Note != null ? SwimTheme.Green : SwimTheme.Amber, paint);

            return y + pad - y0;
        }

        private int KeyRows(Graphics g, int x, int y, int w, bool paint)
        {
            int kh = P(22);
            foreach (KeyValuePair<string, string> kv in SwimAssistantKnowledge.KeyLines(panel.Host.ShortcutOf))
            {
                int kw = KeyCap(g, kv.Key, x, y, kh, paint);
                int tx = x + Math.Max(kw, P(86)) + P(10);
                int th = TextRenderer.MeasureText(g, kv.Value, Body, new Size(Math.Max(P(40), x + w - tx), int.MaxValue), Wrap).Height;
                if (paint)
                    TextRenderer.DrawText(g, kv.Value, Body, new Rectangle(tx, y + Math.Max(0, (kh - th) / 2), Math.Max(P(40), x + w - tx), th), SwimTheme.Text, Wrap);
                y += Math.Max(kh, th) + P(6);
            }

            return y;
        }

        private int KeyCap(Graphics g, string key, int x, int y, int h, bool paint)
        {
            int kw = TextRenderer.MeasureText(g, key, Small, Size.Empty, Centre).Width + P(12);
            if (paint)
            {
                Rectangle r = new Rectangle(x, y, kw, h);
                SwimAssistantDraw.Box(g, r, P(4), SwimTheme.Back, SwimTheme.Border, 1f);
                TextRenderer.DrawText(g, key, Small, r, SwimTheme.AccentPale,
                    Centre);
            }

            return kw;
        }

        private int Step(Graphics g, int n, string text, int x, int y, int w, bool paint)
        {
            int d = P(22);
            int tx = x + d + P(10);
            int tw = Math.Max(P(40), x + w - tx);
            int th = TextRenderer.MeasureText(g, text, Body, new Size(tw, int.MaxValue), Wrap).Height;
            if (paint)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(SwimTheme.Accent, Math.Max(1f, sc)))
                    g.DrawEllipse(p, x, y, d - 1, d - 1);
                TextRenderer.DrawText(g, n.ToString(CultureInfo.InvariantCulture), Small, new Rectangle(x, y, d, d), SwimTheme.Accent,
                    Centre);
                TextRenderer.DrawText(g, text, Body, new Rectangle(tx, y + Math.Max(0, (d - LineHeight(g)) / 2), tw, th), SwimTheme.Text, Wrap);
            }

            return y + Math.Max(d, th + Math.Max(0, (d - LineHeight(g)) / 2));
        }

        private int LineHeight(Graphics g)
        {
            return TextRenderer.MeasureText(g, "Ag", Body, Size.Empty, Fit).Height;
        }

        /// <summary>The path in the menu as chips with › between them, on as many lines as it needs.</summary>
        private int PathChips(Graphics g, string path, int x, int y, int w, bool paint)
        {
            string[] parts = path.Split(new[] { SwimAssistantMenuMap.Arrow }, StringSplitOptions.None);
            int cx = x, cy = y, h = P(26), gap = P(16);
            for (int i = 0; i < parts.Length; i++)
            {
                int tw = Math.Min(w, TextRenderer.MeasureText(g, parts[i], Small, Size.Empty, Centre).Width + P(14));
                if (cx > x && cx + tw > x + w)
                {
                    cx = x;
                    cy += h + P(6);
                }

                Rectangle r = new Rectangle(cx, cy, tw, h);
                if (paint)
                {
                    SwimAssistantDraw.Box(g, r, P(4), SwimTheme.Back, SwimTheme.Accent, 1f);
                    TextRenderer.DrawText(g, parts[i], Small, r, SwimTheme.AccentLight,
                        Centre | TextFormatFlags.EndEllipsis);
                }

                cx += tw;
                if (i < parts.Length - 1)
                {
                    if (paint)
                        SwimAssistantDraw.Chevron(g, new Rectangle(cx, cy, gap, h), SwimTheme.Dim, sc);
                    cx += gap;
                }
            }

            return cy + h;
        }

        /// <summary>An item of the menu: its name, its path under it, «Δείξε» on its right; the whole row lights it in the menu.</summary>
        private int Row(Graphics g, int x, int y, int w, SwimAssistantEntry e, string id)
        {
            int pad = P(10);
            int right = P(30);
            int tw = w - 2 * pad - right;
            int h1 = TextRenderer.MeasureText(g, e.Text, Strong, Size.Empty, Fit).Height + P(2);
            int h2 = TextRenderer.MeasureText(g, e.Path, Small, new Size(tw, int.MaxValue), Wrap).Height;
            int h = pad + h1 + P(2) + h2 + pad;
            Rectangle box = new Rectangle(x, y, w, h);
            bool hot = hover == id;
            SwimAssistantDraw.Box(g, box, P(6), hot ? SwimTheme.RaisedHover : SwimTheme.Raised, hot ? SwimTheme.Accent : SwimTheme.Border, 1f);
            string text = e.Text + (string.IsNullOrEmpty(e.Shortcut) ? "" : "   " + e.Shortcut);
            TextRenderer.DrawText(g, text, Strong, new Rectangle(x + pad, y + pad, tw, h1), e.Item != null && e.Item.Enabled ? SwimTheme.Text : SwimTheme.Dim, One);
            TextRenderer.DrawText(g, e.Path, Small, new Rectangle(x + pad, y + pad + h1 + P(2), tw, h2), SwimTheme.Dim, Wrap);
            SwimAssistantDraw.Target(g, new Rectangle(x + w - pad - P(18), y + (h - P(18)) / 2, P(18), P(18)), hot ? SwimTheme.AccentPale : SwimTheme.Accent, sc);
            SwimAssistantEntry entry = e;
            Add(id, box, () => panel.ShowEntry(entry), SwimAssistantKnowledge.L("Δείξε μου στο μενού", "Show me in the menu"));
            return y + h;
        }

        private int Link(Graphics g, int x, int y, int w, string text, string id, Action act)
        {
            int h = Math.Max(P(26), TextRenderer.MeasureText(g, text, Body, Size.Empty, Fit).Height + P(6));
            int tw = Math.Min(w - P(18), TextRenderer.MeasureText(g, text, Body, Size.Empty, Fit).Width);
            Rectangle box = new Rectangle(x, y, P(18) + tw, h);
            bool hot = hover == id;
            SwimAssistantDraw.Chevron(g, new Rectangle(x, y, P(12), h), hot ? SwimTheme.AccentPale : SwimTheme.Accent, sc);
            TextRenderer.DrawText(g, text, Body, new Rectangle(x + P(18), y, tw, h), hot ? SwimTheme.AccentPale : SwimTheme.AccentLight, One);
            Add(id, box, act, null);
            return y + h;
        }

        private int NoMatch(Graphics g, int x0, int y0, int w0, SwimAssistantResult r, bool paint)
        {
            int pad = P(16);
            int x = x0 + pad, w = w0 - 2 * pad, y = y0 + pad;
            y = Para(g, SwimAssistantKnowledge.L("Δεν βρήκα κάτι γι' αυτό. Μήπως ψάχνεις:", "I found nothing for this. Are you looking for:"), Body, x, y, w, SwimTheme.Text, paint) + P(8);
            foreach (SwimAssistantTopic t in r.Guesses)
            {
                if (paint)
                {
                    SwimAssistantTopic topic = t;
                    y = Link(g, x, y, w, t.Title, "guess:" + t.Id, () => panel.Pick(topic)) + P(2);
                }
                else
                {
                    y += Math.Max(P(26), TextRenderer.MeasureText(g, t.Title, Body, Size.Empty, Fit).Height + P(6)) + P(2);
                }
            }

            return y + pad - y0;
        }

        /// <summary>Claude (AI pro): the button and its note, the dots while it thinks, its answer with the items it names.</summary>
        private int Ai(Graphics g, int x, int y, int w)
        {
            if (panel.AiAnswer != null)
            {
                int pad = P(16);
                int iw = w - 2 * pad;
                int h1 = TextRenderer.MeasureText(g, panel.AiAnswer, Body, new Size(iw, int.MaxValue), Wrap).Height;
                int h2 = string.IsNullOrEmpty(panel.AiCost) ? 0 : TextRenderer.MeasureText(g, panel.AiCost, Small, Size.Empty, Fit).Height + P(8);
                int h = pad + P(16) + P(8) + h1 + h2 + pad;
                SwimAssistantDraw.Box(g, new Rectangle(x, y, w, h), P(8), SwimTheme.Raised, SwimTheme.Amber, 1f);
                Caps(g, "AI PRO · CLAUDE", x + pad, y + pad, SwimTheme.Amber, true);
                TextRenderer.DrawText(g, panel.AiAnswer, Body, new Rectangle(x + pad, y + pad + P(24), iw, h1), SwimTheme.Text, Wrap);
                if (h2 > 0)
                    TextRenderer.DrawText(g, panel.AiCost, Small, new Rectangle(x + pad, y + pad + P(24) + h1 + P(8), iw, h2), SwimTheme.Dim, One);
                y += h + P(12);
                if (panel.AiEntries != null)
                {
                    for (int i = 0; i < panel.AiEntries.Count; i++)
                        y = Row(g, x, y, w, panel.AiEntries[i], "ai:" + i) + P(4);
                    y += P(8);
                }
            }

            if (panel.AiBusy)
            {
                int dots = (int)(DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond / 350 % 4);
                string text = SwimAssistantKnowledge.L("Ο Claude σκέφτεται", "Claude is thinking") + new string('.', dots);
                return Para(g, text, Body, x, y, w, SwimTheme.Amber, true) + P(12);
            }

            if (!string.IsNullOrEmpty(panel.AiError))
                y = Para(g, panel.AiError, Small, x, y, w, SwimTheme.Amber, true) + P(10);

            string ask = SwimAssistantKnowledge.L("Ρώτα το AI (AI pro)", "Ask the AI (AI pro)");
            int bw = TextRenderer.MeasureText(g, ask, ButtonFont, Size.Empty, Centre).Width + P(28);
            Rectangle b = new Rectangle(x, y, bw, P(34));
            bool hot = hover == "ai";
            SwimAssistantDraw.Box(g, b, P(6), hot ? SwimTheme.AmberDark : SwimTheme.Surface, SwimTheme.Amber, 1f);
            TextRenderer.DrawText(g, ask, ButtonFont, b, SwimTheme.Amber,
                Centre);
            Add("ai", b, panel.AskAi, null);
            y += b.Height + P(8);
            string note = SwimAssistantKnowledge.L(
                "Ελεύθερη ερώτηση στον Claude, με τον κωδικό του AI pro. Στέλνεται μόνο η ερώτηση και τα ονόματα των μενού.",
                "A free question to Claude, with the password of AI pro. Only the question and the names of the menus are sent.");
            return Para(g, note, Small, x, y, w, SwimTheme.Dim, true) + P(12);
        }

        private int Section(Graphics g, string text, int x, int y)
        {
            return Caps(g, text, x, y, SwimTheme.Dim, true) + P(8);
        }

        private int Caps(Graphics g, string text, int x, int y, Color color, bool paint)
        {
            return y + SwimAssistantDraw.Caps(g, text, Caption, color, x, y, P(2), paint);
        }

        private int Para(Graphics g, string text, Font font, int x, int y, int w, Color color, bool paint)
        {
            if (string.IsNullOrEmpty(text))
                return y;

            int h = TextRenderer.MeasureText(g, text, font, new Size(Math.Max(1, w), int.MaxValue), Wrap).Height;
            if (paint)
                TextRenderer.DrawText(g, text, font, new Rectangle(x, y, w, h), color, Wrap);
            return y + h;
        }

        private void Add(string id, Rectangle box, Action act, string tipText)
        {
            hots.Add(new Hot { Id = id, Box = box, Act = act, Tip = tipText });
        }
        #endregion

        #region Mouse
        private Hot HotAt(Point p)
        {
            Point q = new Point(p.X, p.Y + scroll);
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                if (hots[i].Box.Contains(q))
                    return hots[i];
            }

            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Hot h = HotAt(e.Location);
            string id = h != null ? h.Id : null;
            if (id != hover)
            {
                hover = id;
                Cursor = h != null ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, h != null ? h.Tip : null);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null)
            {
                hover = null;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;

            Hot h = HotAt(e.Location);
            if (h == null || h.Act == null)
                return;

            try
            {
                h.Act();
            }
            catch (Exception ex)
            {
                log4net.LogManager.GetLogger(typeof(SwimAssistantView)).Error("A command of the assistant failed.", ex);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta / 120 * P(48));
        }
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                tip.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>(2.26.0) The drawing of the assistant: rounded boxes, spaced capitals, the target of «Δείξε μου», the chevron of a path.</summary>
    internal static class SwimAssistantDraw
    {
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Max(0.5f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Box(Graphics g, Rectangle r, int radius, Color fill, Color border, float width)
        {
            if (r.Width <= 2 || r.Height <= 2)
                return;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF f = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (GraphicsPath path = Rounded(f, radius))
            {
                if (fill.A > 0)
                {
                    using (SolidBrush b = new SolidBrush(fill))
                        g.FillPath(b, path);
                }

                if (border.A > 0 && width > 0)
                {
                    using (Pen p = new Pen(border, width))
                        g.DrawPath(p, path);
                }
            }

            g.SmoothingMode = old;
        }

        /// <summary>Capitals with room between them ("ΑΠΑΝΤΗΣΗ"); the height of the line.</summary>
        public static int Caps(Graphics g, string text, Font font, Color color, int x, int y, int spacing, bool paint)
        {
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int h = TextRenderer.MeasureText(g, "Α", font, Size.Empty, flags).Height;
            if (!paint || string.IsNullOrEmpty(text))
                return h;

            int cx = x;
            foreach (char c in text)
            {
                string s = c.ToString();
                int cw = c == ' ' ? Math.Max(2, h / 3) : TextRenderer.MeasureText(g, s, font, Size.Empty, flags).Width;
                if (c != ' ')
                    TextRenderer.DrawText(g, s, font, new Point(cx, y), color, flags);
                cx += cw + spacing;
            }

            return h;
        }

        /// <summary>The target of «Δείξε μου»: a ring with its marks and a dot.</summary>
        public static void Target(Graphics g, Rectangle r, Color color, float sc)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = r.Width * 0.33f;
            using (Pen p = new Pen(color, Math.Max(1.4f, 1.7f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawEllipse(p, cx - rad, cy - rad, 2 * rad, 2 * rad);
                float a = r.Width * 0.5f, b = r.Width * 0.18f;
                g.DrawLine(p, cx, cy - a, cx, cy - a + b);
                g.DrawLine(p, cx, cy + a, cx, cy + a - b);
                g.DrawLine(p, cx - a, cy, cx - a + b, cy);
                g.DrawLine(p, cx + a, cy, cx + a - b, cy);
            }

            using (SolidBrush d = new SolidBrush(color))
            {
                float k = Math.Max(1.6f, r.Width * 0.11f);
                g.FillEllipse(d, cx - k, cy - k, 2 * k, 2 * k);
            }

            g.SmoothingMode = old;
        }

        public static void Chevron(Graphics g, Rectangle r, Color color, float sc)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, k = Math.Max(3f, 3.5f * sc);
            using (Pen p = new Pen(color, Math.Max(1.3f, 1.5f * sc)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(cx - k * 0.5f, cy - k), new PointF(cx + k * 0.5f, cy), new PointF(cx - k * 0.5f, cy + k) });
            g.SmoothingMode = old;
        }
    }

    /// <summary>
    /// (2.26.0) ΒΟΗΘΟΣ on the bar of the menus: the sign (its ring turns while the assistant is open, its dot amber until it was opened once),
    /// the name and F1, in a capsule in the colours of the analysis; on the light bar of the classic menu of Kinovea, the sign on a dark tile.
    /// It draws itself (any renderer).
    /// </summary>
    internal sealed class SwimMenuAssistant : ToolStripMenuItem
    {
        public bool Open;
        public bool Fresh;
        public float Turn;
        public bool Light;
        public string Hint = "F1";
        private Font bold;
        private Font small;

        public SwimMenuAssistant()
        {
            Alignment = ToolStripItemAlignment.Right;
            DisplayStyle = ToolStripItemDisplayStyle.Text;
            Margin = new Padding(4, 0, 4, 0);
        }

        private Font Bold
        {
            get
            {
                if (bold == null || bold.Size != Font.Size || bold.FontFamily.Name != Font.FontFamily.Name)
                {
                    if (bold != null)
                        bold.Dispose();
                    bold = new Font(Font, FontStyle.Bold);
                }

                return bold;
            }
        }

        private Font SmallFont
        {
            get
            {
                if (small == null || Math.Abs(small.Size - Math.Max(6f, Font.Size - 1.5f)) > 0.01f || small.FontFamily.Name != Font.FontFamily.Name)
                {
                    if (small != null)
                        small.Dispose();
                    small = new Font(Font.FontFamily, Math.Max(6f, Font.Size - 1.5f), FontStyle.Regular);
                }

                return small;
            }
        }

        public override Size GetPreferredSize(Size constrainingSize)
        {
            Size basic = base.GetPreferredSize(constrainingSize);
            int h = Math.Max(22, basic.Height);
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            Size t = TextRenderer.MeasureText(Text ?? "", Bold, Size.Empty, flags);
            Size k = TextRenderer.MeasureText(Hint ?? "", SmallFont, Size.Empty, flags);
            int icon = h - 3;
            return new Size(3 + icon + 6 + t.Width + (string.IsNullOrEmpty(Hint) ? 0 : 6 + k.Width) + 12, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = new Rectangle(1, 2, Width - 3, Height - 5);
            if (r.Width < 10 || r.Height < 10)
                return;

            bool hot = Selected || Pressed;
            // The sign as big as the bar allows (a little over the capsule), centred on it.
            int icon = Height - 3;
            Rectangle sign = new Rectangle(r.X + 1, (Height - icon) / 2, icon, icon);
            Color text, hint;
            if (Light)
            {
                if (hot || Open)
                    SwimAssistantDraw.Box(g, r, 4, Color.FromArgb(Open ? 90 : 55, SwimTheme.Accent), SwimTheme.AccentDark, 1f);
                SwimAssistantDraw.Box(g, Rectangle.Inflate(sign, 1, 1), 4, SwimTheme.Surface, SwimTheme.Surface, 1f);
                text = SystemColors.MenuText;
                hint = SystemColors.GrayText;
            }
            else
            {
                if (Open || hot)
                {
                    using (GraphicsPath halo = SwimAssistantDraw.Rounded(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), (r.Height + 2) / 2f))
                    using (Pen p = new Pen(Color.FromArgb(Open ? 120 : 70, SwimTheme.Accent), 3f))
                    {
                        SmoothingMode old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawPath(p, halo);
                        g.SmoothingMode = old;
                    }
                }

                SwimAssistantDraw.Box(g, r, r.Height / 2, Open ? SwimTheme.AccentDark : hot ? SwimTheme.RaisedHover : SwimTheme.Raised,
                    Open || hot ? SwimTheme.Accent : SwimTheme.Border, 1f);
                text = Open ? SwimTheme.AccentPale : SwimTheme.AccentLight;
                hint = Open ? SwimTheme.AccentLight : SwimTheme.Dim;
            }

            SwimAssistantIcon.Draw(g, sign, Turn, Fresh ? SwimTheme.Amber : (Color?)SwimTheme.AccentPale, false, Open ? 0.5f : 0f);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int x = sign.Right + 6;
            Size t = TextRenderer.MeasureText(g, Text ?? "", Bold, Size.Empty, flags);
            TextRenderer.DrawText(g, Text ?? "", Bold, new Rectangle(x, r.Y, t.Width + 2, r.Height), text, flags);
            if (!string.IsNullOrEmpty(Hint))
                TextRenderer.DrawText(g, Hint, SmallFont, new Rectangle(x + t.Width + 6, r.Y, Math.Max(1, r.Right - x - t.Width - 6), r.Height), hint, flags);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (bold != null)
                    bold.Dispose();
                if (small != null)
                    small.Dispose();
                bold = null;
                small = null;
            }

            base.Dispose(disposing);
        }
    }
}
