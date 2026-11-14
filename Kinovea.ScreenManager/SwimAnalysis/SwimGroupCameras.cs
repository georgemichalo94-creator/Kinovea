#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.23.0) A camera of a group of screens as the snapshots know it: where it films from (out of the water or in the water), or
    /// that it gives a picture only when the analyst works on its screen. Kept in the settings by camera (the name the recordings of
    /// the GoPro cameras give it, else its video file), so that it is said once.
    /// </summary>
    public class SwimGroupCamera
    {
        /// <summary>"cam:" and the name of the camera as its files carry it, or "file:" and the name of the video file.</summary>
        public string Key = "";

        /// <summary>The name shown ("" for a camera known by its video file only).</summary>
        public string Name = "";

        /// <summary>SwimCameraPick.None (not said yet), Above, Water or Extra.</summary>
        public int Role;
    }

    /// <summary>
    /// (2.23.0) Which camera of a group of screens gives the picture of a snapshot. The cameras follow the swimmer from the side, so
    /// they have no place along the pool: one films out of the water and one in the water. A start from its signal to the entry is
    /// seen out of the water; what follows the entry and the whole of a turn in the water. A camera of neither kind (at the wall, in
    /// front) gives a picture only when the analyst works on its screen.
    /// </summary>
    public static class SwimCameraPick
    {
        /// <summary>The role of a camera was not said yet; of a moment: it says nothing of the camera.</summary>
        public const int None = 0;

        /// <summary>Out of the water.</summary>
        public const int Above = 1;

        /// <summary>In the water.</summary>
        public const int Water = 2;

        /// <summary>Only when the analyst works on its screen.</summary>
        public const int Extra = 3;

        /// <summary>A start whose entry and release are not marked: the swimmer is in the water this long after the signal (s).</summary>
        private const double EntryGuess = 1.15;

        /// <summary>A start whose entry is not marked: the flight lasts this long after the release (s).</summary>
        private const double FlightGuess = 0.40;

        /// <summary>
        /// The camera that shows the event of a mark: the signal, the first movement, the hands, the back foot, the release from the
        /// block and the entry out of the water; the kicks, the breakout, the strokes, the rotation, the wall and the push-off in the
        /// water. kind: the phase of the mark (the feet leave the block of a start or the wall of a turn).
        /// </summary>
        public static int RoleOfMark(SwimMarkType type, SwimPhaseKind? kind)
        {
            switch (type)
            {
                case SwimMarkType.Signal:
                case SwimMarkType.Reaction:
                case SwimMarkType.HandsOff:
                case SwimMarkType.BackFoot:
                case SwimMarkType.Entry:
                    return Above;
                case SwimMarkType.FeetOff:
                    return !kind.HasValue ? None : kind.Value == SwimPhaseKind.Start ? Above : Water;
                case SwimMarkType.Kick:
                case SwimMarkType.Breakout:
                case SwimMarkType.Stroke:
                case SwimMarkType.RotationStart:
                case SwimMarkType.WallContact:
                case SwimMarkType.Split:
                case SwimMarkType.TechSplit:
                    return Water;
                default:
                    return None;
            }
        }

        /// <summary>
        /// The camera that shows a moment of a phase (seconds from its origin): a start until its entry out of the water (the entry
        /// not marked: a flight after the release, else a little over a second after the signal), then in the water; a turn in the
        /// water. Outside a phase the moment says nothing.
        /// </summary>
        public static int RoleAt(SwimTechPhase phase, double seconds)
        {
            if (phase == null)
                return None;
            if (phase.Kind != SwimPhaseKind.Start)
                return Water;

            double end = phase.Entry.HasValue ? phase.Entry.Value + 0.02 : phase.Block.HasValue ? phase.Block.Value + FlightGuess : EntryGuess;
            return seconds <= end ? Above : Water;
        }

        /// <summary>
        /// The screens of a group in the order their picture is wanted for a snapshot (the first whose video has the moment gives it).
        /// Empty while a camera has no role: the snapshot stays the picture of all the screens. roles: the role of the camera of each
        /// screen; wanted: Above or Water (None: the moment says nothing); pressed: the screen the analyst works on (-1: none); lead:
        /// the screen of the analysis. A camera asked for only on its screen comes first when he works on it; then the cameras of the
        /// kind wanted (the one he works on, the one of the analysis, the others); then the screen he works on and the one of the
        /// analysis, whatever they film.
        /// </summary>
        public static List<int> Order(IList<int> roles, int wanted, int pressed, int lead)
        {
            List<int> order = new List<int>();
            if (roles == null || roles.Count == 0)
                return order;

            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] != Above && roles[i] != Water && roles[i] != Extra)
                    return order;
            }

            bool onScreen = pressed >= 0 && pressed < roles.Count;
            bool hasLead = lead >= 0 && lead < roles.Count;
            if (onScreen && roles[pressed] == Extra)
                order.Add(pressed);

            if (wanted == Above || wanted == Water)
            {
                if (onScreen && roles[pressed] == wanted && !order.Contains(pressed))
                    order.Add(pressed);
                if (hasLead && roles[lead] == wanted && !order.Contains(lead))
                    order.Add(lead);
                for (int i = 0; i < roles.Count; i++)
                {
                    if (roles[i] == wanted && !order.Contains(i))
                        order.Add(i);
                }
            }

            if (onScreen && !order.Contains(pressed))
                order.Add(pressed);
            if (hasLead && !order.Contains(lead))
                order.Add(lead);
            return order;
        }

        /// <summary>
        /// The camera in the name of a video of the recordings of the GoPro cameras ("ΑΘΛΗΤΗΣ_Στροφή_20261112_GoPro1.MP4", a part the
        /// camera cut "…_μέρος2"): what follows the day of the name (and the time of a recording without an athlete); "" when the
        /// name is not one of those.
        /// </summary>
        public static string AliasOfFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            string name = fileName.Trim();
            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0)
                name = name.Substring(slash + 1);
            int dot = name.LastIndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);

            List<string> parts = name.Split('_').ToList();
            int day = -1;
            for (int i = parts.Count - 2; i >= 1; i--)
            {
                if (IsDigits(parts[i], 8) && parts[i].StartsWith("20", StringComparison.Ordinal))
                {
                    day = i;
                    break;
                }
            }

            if (day < 0)
                return "";

            List<string> rest = parts.Skip(day + 1).ToList();
            if (rest.Count >= 2 && IsDigits(rest[0], 6))
                rest.RemoveAt(0);
            if (rest.Count >= 2 && rest[rest.Count - 1].StartsWith("μέρος", StringComparison.OrdinalIgnoreCase) && IsDigits(rest[rest.Count - 1].Substring(5), 0))
                rest.RemoveAt(rest.Count - 1);

            return string.Join("_", rest.ToArray()).Trim('_', ' ');
        }

        private static bool IsDigits(string text, int length)
        {
            if (string.IsNullOrEmpty(text) || (length > 0 && text.Length != length))
                return false;

            foreach (char c in text)
            {
                if (c < '0' || c > '9')
                    return false;
            }

            return true;
        }

        /// <summary>The key of a camera known by its name: the name as the files of the camera carry it, in small letters.</summary>
        public static string KeyOfAlias(string alias)
        {
            return "cam:" + SwimGoProTakes.Clean(alias ?? "", true).ToLowerInvariant();
        }

        /// <summary>
        /// The camera of a video: the one of the recordings of the GoPro cameras that has a file of this name, else the one in the
        /// name of the file, else the file itself (its name: the camera is not known, its role is kept for this video).
        /// </summary>
        public static SwimGroupCamera Identify(string path, IEnumerable<SwimTake> takes)
        {
            string file = (path ?? "").Trim();
            int slash = Math.Max(file.LastIndexOf('\\'), file.LastIndexOf('/'));
            if (slash >= 0)
                file = file.Substring(slash + 1);

            if (takes != null && file.Length > 0)
            {
                foreach (SwimTake take in takes.Reverse())
                {
                    if (take == null || take.Cameras == null)
                        continue;

                    foreach (SwimTakeCamera camera in take.Cameras)
                    {
                        if (camera == null || string.IsNullOrWhiteSpace(camera.Alias) || camera.Targets == null)
                            continue;
                        if (camera.Targets.Any(t => SameFile(t, file)))
                            return new SwimGroupCamera { Key = KeyOfAlias(camera.Alias), Name = camera.Alias.Trim() };
                    }
                }
            }

            string alias = AliasOfFileName(file);
            if (alias.Length > 0)
                return new SwimGroupCamera { Key = KeyOfAlias(alias), Name = alias.Replace('_', ' ') };

            return new SwimGroupCamera { Key = "file:" + file.ToLowerInvariant(), Name = "" };
        }

        private static bool SameFile(string target, string file)
        {
            if (string.IsNullOrEmpty(target))
                return false;

            int slash = Math.Max(target.LastIndexOf('\\'), target.LastIndexOf('/'));
            string name = slash >= 0 ? target.Substring(slash + 1) : target;
            return string.Equals(name, file, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What the name of a camera says of where it films from, for the window of the cameras (the analyst confirms it): Water for
        /// "υποβρύχια", "underwater", "UW" or "μέσα στο νερό", Above for "έξω", "πάνω", "above" or "deck"; None when the name says
        /// nothing, or both.
        /// </summary>
        public static int SuggestRole(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return None;

            string s = name.ToLowerInvariant();
            string[] words = s.Split(new[] { ' ', '_', '-', '.', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            bool water = s.Contains("υποβρ") || s.Contains("underwater") || s.Contains("under water") || s.Contains("μέσα στο νερό") ||
                s.Contains("μεσα στο νερο") || words.Contains("uw");
            bool above = s.Contains("έξω") || s.Contains("εξω") || s.Contains("πάνω") || s.Contains("πανω") || s.Contains("above") || words.Contains("deck");
            return water == above ? None : water ? Water : Above;
        }

        /// <summary>
        /// Two cameras, one out of the water or in the water and the other not said: the other films from the other side of the
        /// surface (what the window of the cameras proposes).
        /// </summary>
        public static void Complete(int[] roles)
        {
            if (roles == null || roles.Length != 2)
                return;

            for (int i = 0; i < 2; i++)
            {
                int other = roles[1 - i];
                if (roles[i] == None && (other == Above || other == Water))
                    roles[i] = other == Above ? Water : Above;
            }
        }
    }

    /// <summary>
    /// (2.23.0) The window «Κάμερες της ομάδας»: where each camera of the group of screens films from (out of the water, in the
    /// water, or a picture only when the analyst works on its screen). Said once for each camera.
    /// </summary>
    public class FormSwimGroupCameras : Form
    {
        private readonly List<ComboBox> combos = new List<ComboBox>();
        private readonly SwimButton ok;
        private readonly Label hint;

        /// <summary>The role chosen for each camera, in the order of the titles (SwimCameraPick.None: not chosen).</summary>
        public int[] Roles
        {
            get { return combos.Select(c => RoleOfIndex(c.SelectedIndex)).ToArray(); }
        }

        public FormSwimGroupCameras(IList<string> titles, IList<int> roles)
            : this(titles, roles, false)
        {
        }

        /// <summary>
        /// titles: "Οθόνη 1 · GoPro A" for each camera; roles: the role of each, None for one to choose; proposed: a role there is
        /// a proposal of the program (the name of the camera, the choice of the last time), not what was said for this camera: the
        /// window asks to check it.
        /// </summary>
        public FormSwimGroupCameras(IList<string> titles, IList<int> roles, bool proposed)
        {
            int count = titles != null ? titles.Count : 0;
            Text = SwimLang.T("Κάμερες της ομάδας");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, Math.Min(640, 212 + 62 * count + (proposed ? 44 : 0)));
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            Font = SwimTheme.Ui(9.5f, FontStyle.Regular);
            KeyPreview = true;

            TableLayoutPanel t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 12, 16, 12), BackColor = SwimTheme.Back, AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            t.Controls.Add(new Label
            {
                Text = SwimLang.T("Από πού γράφει κάθε κάμερα;"),
                AutoSize = true,
                Font = SwimTheme.Ui(11f, FontStyle.Bold),
                ForeColor = SwimTheme.AccentLight,
                Margin = new Padding(0, 0, 0, 6)
            });
            t.Controls.Add(new Label
            {
                Text = SwimLang.T("Το στιγμιότυπο κάθε γεγονότος παίρνει μόνο του την εικόνα της κάμερας που το δείχνει. Το ορίζεις μία φορά για κάθε κάμερα."),
                AutoSize = true,
                ForeColor = SwimTheme.Dim,
                Margin = new Padding(0, 0, 0, 6),
                MaximumSize = new Size(470, 0)
            });

            for (int i = 0; i < count; i++)
            {
                t.Controls.Add(new Label
                {
                    Text = titles[i] ?? "",
                    AutoSize = true,
                    Font = SwimTheme.Ui(10f, FontStyle.Bold),
                    ForeColor = SwimTheme.Text,
                    Margin = new Padding(0, 8, 0, 2),
                    MaximumSize = new Size(470, 0)
                });

                ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(0, 0, 0, 2) };
                combo.Items.Add(SwimLang.T("Έξω από το νερό"));
                combo.Items.Add(SwimLang.T("Μέσα στο νερό"));
                combo.Items.Add(SwimLang.T("Μόνο όταν πατάω πάνω της"));
                combo.SelectedIndex = IndexOfRole(roles != null && i < roles.Count ? roles[i] : SwimCameraPick.None);
                combo.SelectedIndexChanged += (s, e) => UpdateState();
                combos.Add(combo);
                t.Controls.Add(combo);
            }

            if (proposed)
            {
                t.Controls.Add(new Label
                {
                    Text = SwimLang.T("Οι επιλογές προτείνονται (από το όνομα της κάμερας ή όπως την τελευταία φορά): έλεγξε ότι ισχύουν και για αυτά τα βίντεο."),
                    AutoSize = true,
                    ForeColor = SwimTheme.Amber,
                    Margin = new Padding(0, 8, 0, 0),
                    MaximumSize = new Size(470, 0)
                });
            }

            hint = new Label { AutoSize = true, ForeColor = SwimTheme.Dim, Margin = new Padding(0, 10, 0, 0), MaximumSize = new Size(470, 0) };
            t.Controls.Add(hint);

            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
            ok = new SwimButton(true) { Text = SwimLang.T("Αποθήκευση"), AutoSize = true, Height = 32, Primary = true };
            ok.Click += (s, e) =>
            {
                if (!AllChosen)
                    return;
                DialogResult = DialogResult.OK;
                Close();
            };
            SwimButton cancel = new SwimButton(true) { Text = SwimLang.T("Άκυρο"), AutoSize = true, Height = 32 };
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            t.Controls.Add(buttons);
            Controls.Add(t);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
                else if (e.KeyCode == Keys.Enter && AllChosen && !combos.Any(c => c.DroppedDown))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
            UpdateState();
        }

        private bool AllChosen
        {
            get { return combos.All(c => c.SelectedIndex >= 0); }
        }

        private static int IndexOfRole(int role)
        {
            switch (role)
            {
                case SwimCameraPick.Above: return 0;
                case SwimCameraPick.Water: return 1;
                case SwimCameraPick.Extra: return 2;
                default: return -1;
            }
        }

        private static int RoleOfIndex(int index)
        {
            switch (index)
            {
                case 0: return SwimCameraPick.Above;
                case 1: return SwimCameraPick.Water;
                case 2: return SwimCameraPick.Extra;
                default: return SwimCameraPick.None;
            }
        }

        private void UpdateState()
        {
            bool all = AllChosen;
            ok.Enabled = all;
            hint.ForeColor = all ? SwimTheme.Dim : SwimTheme.Amber;
            hint.Text = all
                ? SwimLang.T("Εκκίνηση, από το σήμα ως την είσοδο στο νερό: η κάμερα έξω από το νερό. Μετά την είσοδο και σε όλη τη στροφή: η κάμερα μέσα στο νερό.")
                : SwimLang.T("Διάλεξε από πού γράφει κάθε κάμερα.");
        }
    }
}
