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
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Kinovea.Services;

namespace Kinovea.ScreenManager
{
    /// <summary>(2.18.0) What the main menu of Kinovea Swim asks of the screen manager.</summary>
    internal sealed class SwimMenuHost
    {
        /// <summary>
        /// The window of the analysis of the video of the active screen (else of the first screen with a video): its race view (false),
        /// its technical view (true) or the view it has (null), and one of its pages (null: the page it shows). False without a video.
        /// </summary>
        public Func<bool?, string, bool> OpenAnalysis;

        public Func<bool> BigScreensOn;
        public Func<bool> CanBigScreens;
        public Action<bool> SetBigScreens;

        /// <summary>The menu of the group of screens of the common controls, at the mouse.</summary>
        public Action ShowGroupMenu;
    }

    /// <summary>
    /// (2.18.0) The main menu of Kinovea Swim: the menus of Kinovea and of the analysis in groups by task (Αρχείο, Επεξεργασία, Προβολή,
    /// Αγώνας, Τεχνική, Αναφορά, Εργαλεία, Βοήθεια and, on the right, the state of the Google Sheet, ΕΛ | EN and ⚙ Ρυθμίσεις), with
    /// Greek names, the colours of the analysis, and in the toolbar the actions used most.
    /// Nothing of the menu of Kinovea is lost: its items are the same objects, moved (their actions, keys, ticks and states stay the
    /// program's own), an item that the new places do not name goes to the new menu of its old group, and when an item would be
    /// missing the menu goes back to the classic one. The window of the analysis and the bars on the video stay as they are; the
    /// menus Αγώνας and Τεχνική open them at their pages and give the buttons of the bars of the moment.
    /// «Κλασικό μενού του Kinovea» in ⚙ Ρυθμίσεις brings the old menu back at the next start.
    /// </summary>
    internal sealed class SwimMainMenu
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly MenuStrip strip;
        private readonly SwimMenuHost host;
        private ToolStrip toolbar;
        private readonly SwimMenuRenderer renderer = new SwimMenuRenderer();
        private readonly bool classic;
        private readonly SwimSettings startSettings;

        /// <summary>The English of the menu where the Greek word has another English in the analysis (Αναφορά: «Reference» there).</summary>
        private static readonly Dictionary<string, string> MenuEnglish = new Dictionary<string, string>
        {
            { "Αναφορά", "Report" },
            { "ΑΝΑΦΟΡΑ", "REPORT" },
            { "Έξοδος", "Exit" }
        };
        private bool built;
        private bool toolbarBuilt;
        private bool failed;

        /// <summary>The Greek names of the items (set again whenever Kinovea sets its own, and in English with the analysis).</summary>
        private readonly Dictionary<ToolStripItem, string> labels = new Dictionary<ToolStripItem, string>();
        private readonly Dictionary<ToolStripItem, string> tips = new Dictionary<ToolStripItem, string>();

        /// <summary>Items whose name says something that changes: the word of Kinovea at their start (undo, redo, closing).</summary>
        private readonly Dictionary<ToolStripItem, KeyValuePair<Func<string>, string>> prefixes = new Dictionary<ToolStripItem, KeyValuePair<Func<string>, string>>();

        /// <summary>Items whose name follows a state (the debug logs on or off): the Greek name now.</summary>
        private readonly Dictionary<ToolStripItem, Func<string>> states = new Dictionary<ToolStripItem, Func<string>>();
        private readonly HashSet<ToolStripMenuItem> groups = new HashSet<ToolStripMenuItem>();
        private readonly HashSet<ToolStripDropDownItem> themed = new HashSet<ToolStripDropDownItem>();
        private readonly List<ToolStripItem> added = new List<ToolStripItem>();
        private bool relabeling;

        private Font boldFont;
        private Font headerFont;
        private Font captionFont;

        // The menus of the analysis.
        private ToolStripMenuItem mnuRace;
        private ToolStripMenuItem mnuTech;
        private ToolStripMenuItem mnuReport;
        private ToolStripMenuItem mnuClassic;
        private ToolStripMenuItem racePanel;
        private ToolStripItem raceNowBefore;
        private readonly List<ToolStripItem> raceNow = new List<ToolStripItem>();
        private readonly List<ToolStripItem> techNow = new List<ToolStripItem>();
        private readonly List<ToolStripItem> needRace = new List<ToolStripItem>();
        private ToolStripMenuItem reportPackage;
        private ToolStripMenuItem reportSheet;

        // Προβολή › Στο βίντεο.
        private ToolStripMenuItem viewBig;
        private ToolStripMenuItem viewGraphics;
        private ToolStripMenuItem viewHudOn;
        private ToolStripMenuItem viewHudOff;
        private ToolStripMenuItem viewReel;
        private ToolStripMenuItem viewDial;

        // On the right of the bar.
        private SwimMenuToggle barEl;
        private SwimMenuToggle barEn;
        private SwimMenuChip sheetChip;
        private Timer chipTimer;
        private SwimVideoBar source;

        public SwimMainMenu(MenuStrip strip, SwimMenuHost host)
        {
            this.strip = strip;
            this.host = host ?? new SwimMenuHost();
            try
            {
                startSettings = SwimSettings.Load();
                classic = startSettings.ClassicMenu;
            }
            catch (Exception e)
            {
                log.Error("Could not read the settings of the swim analysis for the menu.", e);
                startSettings = new SwimSettings();
                classic = false;
            }
        }

        /// <summary>The menu of Kinovea Swim is on (not the classic menu of Kinovea).</summary>
        public bool Active
        {
            get { return built && !classic; }
        }

        /// <summary>The items of the old menus that are not in the new one (0, else the classic menu stays).</summary>
        internal int Lost { get; private set; }

        /// <summary>The toolbar of the main window (after the modules put their buttons in it).</summary>
        public void SetToolbar(ToolStrip toolbar)
        {
            this.toolbar = toolbar;
        }

        /// <summary>
        /// After Kinovea set the texts of its menus (its start, a change of language or of the preferences): the new menu the first time,
        /// then its names again.
        /// </summary>
        public void Apply()
        {
            if (classic || failed || strip == null)
                return;

            try
            {
                if (!built)
                {
                    // Not yet (the menus of Kinovea not there): again at the next refresh; failed (put back): the classic menu stays.
                    if (!Build())
                        return;

                    built = true;
                    SwimLang.Changed += (s, e) => Relabel();
                }

                if (!toolbarBuilt && toolbar != null)
                {
                    BuildToolbar();
                    toolbarBuilt = true;
                }

                Relabel();
                ThemeBars();
            }
            catch (Exception e)
            {
                log.Error("Could not lay out the menu of Kinovea Swim.", e);
            }
        }

        #region Names of the items
        /// <summary>
        /// The items of the menus and of the toolbar of a module get the names of their fields (mnuOpenFile, toolSave…), so that the menu
        /// finds them wherever they are. Only an item without a name is named.
        /// </summary>
        public static void NameFields(object module)
        {
            if (module == null)
                return;

            foreach (FieldInfo f in module.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(ToolStripItem).IsAssignableFrom(f.FieldType))
                    continue;

                ToolStripItem item = f.GetValue(module) as ToolStripItem;
                if (item != null && string.IsNullOrEmpty(item.Name))
                    item.Name = f.Name;
            }
        }

        /// <summary>The items of the kernel of the main window (its root kernel, and the modules it has) by the names of their fields.</summary>
        private void NameKernels()
        {
            Form form = strip.FindForm();
            if (form == null)
                return;

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (FieldInfo f in form.GetType().GetFields(all))
            {
                if (!f.FieldType.Name.EndsWith("Kernel", StringComparison.Ordinal))
                    continue;

                object kernel = f.GetValue(form);
                if (kernel == null)
                    continue;

                NameFields(kernel);
                foreach (FieldInfo g in kernel.GetType().GetFields(all))
                {
                    if (g.FieldType.Name.EndsWith("Kernel", StringComparison.Ordinal))
                        NameFields(g.GetValue(kernel));
                }
            }
        }

        private static ToolStripItem Find(ToolStripItemCollection items, string name)
        {
            foreach (ToolStripItem it in items)
            {
                if (it.Name == name)
                    return it;

                ToolStripDropDownItem d = it as ToolStripDropDownItem;
                if (d != null && d.HasDropDownItems)
                {
                    ToolStripItem inner = Find(d.DropDownItems, name);
                    if (inner != null)
                        return inner;
                }
            }

            return null;
        }

        private static bool Contains(ToolStripItemCollection items, ToolStripItem item)
        {
            foreach (ToolStripItem it in items)
            {
                if (it == item)
                    return true;

                ToolStripDropDownItem d = it as ToolStripDropDownItem;
                if (d != null && d.HasDropDownItems && Contains(d.DropDownItems, item))
                    return true;
            }

            return false;
        }

        private ToolStripItem Item(string name)
        {
            return Find(strip.Items, name);
        }

        private ToolStripMenuItem TopMenu(string name)
        {
            foreach (ToolStripItem it in strip.Items)
            {
                if (it.Name == name)
                    return it as ToolStripMenuItem;
            }

            return null;
        }
        #endregion

        #region Build
        /// <summary>
        /// The new places. Every item of an old menu is taken out of it, in its order, then put in its new place; the ones the new places do
        /// not name go at the end of the new menu of their old group. When something goes wrong, or an item would be missing, every item
        /// goes back where it was (the classic menu).
        /// </summary>
        private bool Build()
        {
            NameKernels();

            ToolStripMenuItem file = TopMenu("mnuFile");
            ToolStripMenuItem edit = TopMenu("mnuEdit");
            ToolStripMenuItem view = TopMenu("mnuView");
            ToolStripMenuItem image = TopMenu("mnuImage");
            ToolStripMenuItem video = TopMenu("mnuVideo");
            ToolStripMenuItem tools = TopMenu("mnuTools");
            ToolStripMenuItem window = TopMenu("mnuWindow");
            ToolStripMenuItem options = TopMenu("mnuOptions");
            ToolStripMenuItem help = TopMenu("mnuHelp");
            if (file == null || edit == null || view == null || tools == null || options == null || help == null)
            {
                log.Warn("The menus of Kinovea were not found by their names: the classic menu stays for now.");
                return false;
            }

            // What every old menu has, in its order (separators too).
            Dictionary<ToolStripMenuItem, List<ToolStripItem>> had = new Dictionary<ToolStripMenuItem, List<ToolStripItem>>();
            foreach (ToolStripMenuItem m in new[] { file, edit, view, image, video, tools, window, options, help })
            {
                if (m != null && !had.ContainsKey(m))
                    had[m] = m.DropDownItems.Cast<ToolStripItem>().ToList();
            }

            List<ToolStripItem> before = had.Values.SelectMany(l => l).Where(i => !(i is ToolStripSeparator)).ToList();
            int optionsIndex = strip.Items.IndexOf(options);
            try
            {
                foreach (ToolStripMenuItem m in had.Keys)
                    m.DropDownItems.Clear();

                Place(had, file, edit, view, image, video, tools, window, options, help);
                Lost = before.Count(i => !Contains(strip.Items, i));
                if (Lost == 0)
                    return true;

                log.Warn(Lost + " items of the menu of Kinovea would not be in the new menu: the classic menu stays.");
            }
            catch (Exception e)
            {
                log.Error("Could not lay out the new menu: the classic menu stays.", e);
            }

            Restore(had, options, optionsIndex);
            failed = true;
            return false;
        }

        /// <summary>Every item back in its old menu, in its order; the new menus taken away (the classic menu).</summary>
        private void Restore(Dictionary<ToolStripMenuItem, List<ToolStripItem>> had, ToolStripMenuItem options, int optionsIndex)
        {
            try
            {
                labels.Clear();
                tips.Clear();
                if (chipTimer != null)
                    chipTimer.Stop();

                foreach (ToolStripItem i in added)
                    strip.Items.Remove(i);

                foreach (KeyValuePair<ToolStripMenuItem, List<ToolStripItem>> kv in had)
                {
                    kv.Key.DropDownItems.Clear();
                    kv.Key.DropDownItems.AddRange(kv.Value.ToArray());
                    kv.Key.Visible = true;
                }

                options.Alignment = ToolStripItemAlignment.Left;
                if (optionsIndex >= 0 && strip.Items.IndexOf(options) != optionsIndex)
                {
                    strip.Items.Remove(options);
                    strip.Items.Insert(Math.Min(optionsIndex, strip.Items.Count), options);
                }
            }
            catch (Exception e)
            {
                log.Error("Could not put the classic menu back.", e);
            }
        }

        private void Place(Dictionary<ToolStripMenuItem, List<ToolStripItem>> had, ToolStripMenuItem file, ToolStripMenuItem edit,
            ToolStripMenuItem view, ToolStripMenuItem image, ToolStripMenuItem video, ToolStripMenuItem tools, ToolStripMenuItem window,
            ToolStripMenuItem options, ToolStripMenuItem help)
        {
            boldFont = new Font(strip.Font, FontStyle.Bold);
            headerFont = new Font(strip.Font.FontFamily, Math.Max(6f, strip.Font.Size - 1f), FontStyle.Bold);
            captionFont = new Font(strip.Font.FontFamily, Math.Max(6f, strip.Font.Size - 2f), FontStyle.Bold);

            HashSet<ToolStripItem> placed = new HashSet<ToolStripItem>();
            Func<string, ToolStripItem> named = n => had.Values.SelectMany(l => l).FirstOrDefault(i => i.Name == n);

            // Αρχείο: open, recent, the folder watched; the drawings of the video; closing (leaving at the end).
            Put(file, placed, named, "mnuOpenFile", "mnuHistory", "mnuOpenReplayWatcher",
                Header("Σχέδια και σημειώσεις του βίντεο"), "mnuLoadAnnotations", "mnuSave", "mnuSaveAs", "mnuUnloadAnnotations",
                null, "mnuCloseFile", "mnuCloseFile2");

            // Επεξεργασία.
            Put(edit, placed, named, "mnuUndo", "mnuRedo", Header("Σχέδια στο βίντεο"), "mnuCutDrawing", "mnuCopyDrawing", "mnuPasteDrawing");

            // Προβολή: the screens, full screen, the panels; the analysis on the video; the image and the filters of the video.
            ToolStripMenuItem screens = Group("Οθόνες", Properties.Resources.television);
            Put(screens, placed, named, "mnuOnePlayer", "mnuTwoPlayers", "mnuThreePlayers", "mnuFourPlayers", "mnuFivePlayers", null,
                "mnuOneCapture", "mnuTwoCaptures", "mnuTwoMixed", null, "mnuVerticalLayout", "mnuSwapScreens");
            ToolStripMenuItem picture = Group("Εικόνα", Properties.Resources.image);
            if (image != null)
                PutAll(picture, placed, had[image]);
            ToolStripMenuItem filters = Group("Φίλτρο βίντεο", null);
            if (video != null)
                PutAll(filters, placed, had[video]);

            viewBig = Entry("Μεγάλες οθόνες (μόνο οι εικόνες)", null, ToggleBigScreens);
            ToolStripMenuItem screensGroup = Entry("Ομάδα οθονών και συγχρονισμός…", null, () =>
            {
                if (host.ShowGroupMenu != null)
                    host.ShowGroupMenu();
            });
            viewGraphics = Entry("Γραφικά της ανάλυσης", null, () => Swim().SetGraphics(!CurrentSettings().VideoGraphics));
            ToolStripMenuItem hud = Group("HUD ή πινακάκια", null);
            viewHudOn = Entry("HUD (όπως το demo)", null, () => Swim().SetHud(true));
            viewHudOff = Entry("Πινακάκια", null, () => Swim().SetHud(false));
            hud.DropDownItems.AddRange(new ToolStripItem[] { viewHudOn, viewHudOff });
            viewReel = Entry("Τεχνική στο βίντεο (reel)", null, () => Swim().SetReel(!CurrentSettings().ReelOverlay));
            viewDial = Entry("Στρογγυλό κουμπί", null, () => Swim().SetDialHidden(!CurrentSettings().DialHidden));
            Tip(viewDial, "Στην πλήρη οθόνη, όταν δεν χωρούν οι μπάρες δίπλα στο βίντεο: το στρογγυλό κουμπί πάνω στο βίντεο.");
            Put(view, placed, named, screens, "mnuFullScreen", viewBig, screensGroup, "mnuToggleCommonCtrls", "mnuToggleNavigationPane",
                Header("Στο βίντεο"), viewGraphics, hud, viewReel, viewDial, null, picture, filters);
            view.DropDownOpening += (s, e) => RefreshView();

            // Αγώνας: the window and its steps, the buttons of the bars of the moment, closing.
            mnuRace = TopLevel("Αγώνας");
            racePanel = Entry("Πίνακας ανάλυσης αγώνα", Properties.Resources.plot_16, () => OpenAnalysis(false, null));
            ToolStripMenuItem newAthlete = Entry("Νέος αθλητής…", null, () => WithRace(m => m.NewRace()));
            ToolStripMenuItem raceInfo = Entry("Στοιχεία και αποτελέσματα", null, () => OpenAnalysis(false, "race"));
            ToolStripMenuItem finals = Entry("Σύγκριση με τελικό…", null, () => OpenAnalysis(false, "finals"));
            ToolStripMenuItem raceAi = Entry("AI pro · ανάλυση με τον Claude…", null, () => WithRace(m => m.ShowAiPro()));
            Tip(raceAi, "Ο Claude σχολιάζει τον αγώνα για την αναφορά και αξιολογεί την εκκίνηση και τις στροφές με τις λίστες σου (με κωδικό).");
            ToolStripMenuItem closeAnalysis = Entry("Κλείσιμο ανάλυσης", null, () => WithRace(m => m.CloseAnalysis()));
            ToolStripMenuItem deleteAnalysis = Entry("Διαγραφή ανάλυσης…", null, () => WithRace(DeleteAnalysis));
            Tip(closeAnalysis, "Κλείνουν οι μπάρες και ο πίνακας· η ανάλυση μένει αποθηκευμένη στο βίντεο.");
            needRace.AddRange(new ToolStripItem[] { newAthlete, closeAnalysis, deleteAnalysis, raceAi });
            ToolStripSeparator closing = new ToolStripSeparator();
            raceNowBefore = closing;
            mnuRace.DropDownItems.AddRange(new ToolStripItem[] { racePanel, newAthlete, raceInfo, Header("Βήματα της ανάλυσης"),
                Entry("Εκκίνηση", null, () => OpenAnalysis(false, "start")),
                Entry("Κολύμβηση · περάσματα", null, () => OpenAnalysis(false, "swim")),
                Entry("Στροφές", null, () => OpenAnalysis(false, "turns")),
                Entry("Τερματισμός και ΔΑΚ", null, () => OpenAnalysis(false, "finish")),
                finals, raceAi, closing, closeAnalysis, deleteAnalysis });
            mnuRace.DropDownOpening += (s, e) => { RefreshNeedRace(); FillNow(mnuRace, raceNow, false, raceNowBefore); };

            // Τεχνική: the window and its pages, the buttons of the bars of the moment.
            mnuTech = TopLevel("Τεχνική");
            ToolStripMenuItem techAi = Entry("AI pro · ανάλυση με τον Claude…", null, () => WithRace(m => m.ShowAiPro()));
            Tip(techAi, "Ο Claude αξιολογεί την εκκίνηση ή τη στροφή από τα καρέ των οθονών με τη λίστα σου· οι απαντήσεις του μπαίνουν στη λίστα ως AI για να τις επιβεβαιώσεις (με κωδικό).");
            needRace.Add(techAi);
            mnuTech.DropDownItems.AddRange(new ToolStripItem[] {
                Entry("Πίνακας τεχνικής ανάλυσης", Properties.Resources.plot_16, () => OpenAnalysis(true, null)),
                Header("Σελίδες"),
                Entry("Εκκινήσεις", null, () => OpenAnalysis(true, "tech-start")),
                Entry("Στροφές", null, () => OpenAnalysis(true, "tech-turns")),
                Entry("Στιγμιότυπα και γωνίες", null, () => OpenAnalysis(true, "tech-media")),
                Entry("Αξιολόγηση", null, () => OpenAnalysis(true, "tech-eval")),
                new ToolStripSeparator(),
                techAi });
            mnuTech.DropDownOpening += (s, e) => { RefreshNeedRace(); FillNow(mnuTech, techNow, true, null); };

            // Αναφορά: the PDF and the sending of the analysis, then the exports of Kinovea.
            mnuReport = TopLevel("Αναφορά");
            ToolStripMenuItem pdf = Entry("PDF report", Properties.Resources.pdfexport, () => WithRace(m => m.PdfByUser()));
            reportPackage = Entry("📦 Δημιουργία αποστολής…", null, () => WithRace(m => m.CreatePackage()));
            ToolStripMenuItem send = Entry("Αποστολή τώρα στο Google Sheet", null, () => WithRace(m => m.SendByUser(false)));
            reportSheet = Entry("Άνοιγμα φύλλου", null, () => WithRace(m => m.OpenSheet()));
            Tip(reportPackage, "Η αποστολή για τους προπονητές: PDF, βίντεο με τα γραφικά, μήνυμα.");
            needRace.AddRange(new ToolStripItem[] { pdf, reportPackage, send, reportSheet });
            mnuReport.DropDownItems.AddRange(new ToolStripItem[] { pdf, reportPackage, send, reportSheet });
            Put(mnuReport, placed, named, Header("Εξαγωγή"), "mnuExportVideo", "mnuExportImage", "mnuExportSpreadsheet", "mnuExportDocument");
            mnuReport.DropDownOpening += (s, e) => RefreshNeedRace();

            // Εργαλεία: the calibrations, the diagrams, the image; the window of the swim analysis.
            Put(tools, placed, named, Header("Βαθμονόμηση"), "mnuTimeCalibration", "mnuCoordinateSystem", "mnuLensCalibration",
                "mnuCalibrationValidation", Header("Διαγράμματα"), "mnuScatterDiagram", "mnuLinearKinematics", "mnuAngularKinematics",
                "mnuAngleAngleDiagram", null, "mnuImportImage", "mnuBackground", "mnuTestGrid", null, "mnuSwimAnalysis");

            // ⚙ Ρυθμίσεις: language, time, pointer, the Google Sheet, variables; the windows of Kinovea; the preferences.
            ToolStripMenuItem connection = Entry("Σύνδεση με Google Sheet…", null, () => OpenAnalysis(null, "connection"));
            ToolStripMenuItem windows = Group("Παράθυρα", null);
            if (window != null)
                PutAll(windows, placed, had[window]);
            Put(options, placed, named, "mnuLanguages", "mnuTimecode", "mnuPointer", connection, "mnuVariables", windows, null, "mnuPreferences");

            // Βοήθεια: as it was.
            PutAll(help, placed, had[help]);

            // What the new places did not name goes to the new menu of its old group (nothing is lost).
            Dictionary<ToolStripMenuItem, ToolStripMenuItem> home = new Dictionary<ToolStripMenuItem, ToolStripMenuItem>
            {
                { file, file }, { edit, edit }, { view, view }, { tools, tools }, { options, options }, { help, help }
            };
            if (image != null)
                home[image] = picture;
            if (video != null)
                home[video] = filters;
            if (window != null)
                home[window] = windows;
            foreach (KeyValuePair<ToolStripMenuItem, List<ToolStripItem>> kv in had)
            {
                List<ToolStripItem> rest = kv.Value.Where(i => !placed.Contains(i) && !(i is ToolStripSeparator) && i.Name != "mnuQuit").ToList();
                if (rest.Count == 0)
                    continue;

                ToolStripMenuItem target = home[kv.Key];
                if (!groups.Contains(target) || target.Owner == null)
                {
                    // A group that was not put in its menu (it had nothing): in the menu of the old group itself.
                    target = groups.Contains(target) ? kv.Key : target;
                }

                if (target.DropDownItems.Count > 0)
                    target.DropDownItems.Add(new ToolStripSeparator());
                foreach (ToolStripItem i in rest)
                {
                    target.DropDownItems.Add(i);
                    placed.Add(i);
                }

                if (target == kv.Key && kv.Key != file && kv.Key != edit && kv.Key != view && kv.Key != tools && kv.Key != options && kv.Key != help)
                    kv.Key.Visible = true;
            }

            // Leaving: last in Αρχείο.
            ToolStripItem quit = named("mnuQuit");
            if (quit != null && !placed.Contains(quit))
            {
                file.DropDownItems.Add(new ToolStripSeparator());
                file.DropDownItems.Add(quit);
                placed.Add(quit);
            }

            // The classic menu of Kinovea, from the next start.
            mnuClassic = Entry("Κλασικό μενού του Kinovea (από το επόμενο άνοιγμα)", null, ToggleClassic);
            options.DropDownItems.Add(new ToolStripSeparator());
            options.DropDownItems.Add(mnuClassic);

            // The groups: their names and their order; on the right ⚙ Ρυθμίσεις at the edge, then ΕΛ | EN and the Google Sheet.
            SetLabel(file, "Αρχείο");
            SetLabel(edit, "Επεξεργασία");
            SetLabel(view, "Προβολή");
            SetLabel(tools, "Εργαλεία");
            SetLabel(help, "Βοήθεια");
            SetLabel(options, "⚙ Ρυθμίσεις");
            foreach (ToolStripMenuItem m in new[] { image, video, window })
            {
                if (m != null && !m.HasDropDownItems)
                    m.Visible = false;
            }

            int at = strip.Items.IndexOf(view) + 1;
            foreach (ToolStripMenuItem m in new[] { mnuRace, mnuTech, mnuReport })
            {
                strip.Items.Insert(at++, m);
                added.Add(m);
            }

            strip.Items.Remove(options);
            strip.Items.Add(options);
            options.Alignment = ToolStripItemAlignment.Right;

            barEn = new SwimMenuToggle { Alignment = ToolStripItemAlignment.Right, Text = "EN" };
            barEl = new SwimMenuToggle { Alignment = ToolStripItemAlignment.Right, Text = "ΕΛ" };
            tips[barEn] = "Η ανάλυση και το Kinovea στα αγγλικά";
            tips[barEl] = "Η ανάλυση και το Kinovea στα ελληνικά";
            barEn.Click += (s, e) => SetLanguage(true);
            barEl.Click += (s, e) => SetLanguage(false);
            sheetChip = new SwimMenuChip { Alignment = ToolStripItemAlignment.Right };
            sheetChip.Click += (s, e) => OpenAnalysis(null, "connection");
            foreach (ToolStripItem i in new ToolStripItem[] { barEn, barEl, sheetChip })
            {
                strip.Items.Add(i);
                added.Add(i);
            }

            strip.ShowItemToolTips = true;
            LabelItems();
            RefreshChip();
            chipTimer = new Timer { Interval = 1500 };
            chipTimer.Tick += (s, e) => RefreshChip();
            chipTimer.Start();
        }

        /// <summary>Items in their new place: names of items of the old menus, new items, or null for a line between groups.</summary>
        private void Put(ToolStripMenuItem parent, HashSet<ToolStripItem> placed, Func<string, ToolStripItem> named, params object[] entries)
        {
            bool line = false;
            foreach (object e in entries)
            {
                if (e == null)
                {
                    line = parent.DropDownItems.Count > 0;
                    continue;
                }

                ToolStripItem item = e as ToolStripItem;
                if (item == null)
                {
                    item = named((string)e);
                    if (item == null || placed.Contains(item))
                        continue;
                }
                else
                {
                    ToolStripMenuItem group = item as ToolStripMenuItem;
                    if (group != null && groups.Contains(group) && !group.HasDropDownItems)
                        continue;
                }

                // A title of a part has its line before it.
                if (item is SwimMenuHeader && parent.DropDownItems.Count > 0)
                    line = true;

                if (line && parent.DropDownItems.Count > 0 && !(parent.DropDownItems[parent.DropDownItems.Count - 1] is ToolStripSeparator))
                    parent.DropDownItems.Add(new ToolStripSeparator());
                line = false;

                parent.DropDownItems.Add(item);
                placed.Add(item);
            }
        }

        /// <summary>All the items of an old menu in a new one, in their order (its lines between groups too).</summary>
        private static void PutAll(ToolStripMenuItem parent, HashSet<ToolStripItem> placed, List<ToolStripItem> items)
        {
            foreach (ToolStripItem i in items)
            {
                if (placed.Contains(i))
                    continue;

                if (i is ToolStripSeparator)
                {
                    if (parent.DropDownItems.Count == 0 || parent.DropDownItems[parent.DropDownItems.Count - 1] is ToolStripSeparator)
                        continue;
                    parent.DropDownItems.Add(new ToolStripSeparator());
                    continue;
                }

                parent.DropDownItems.Add(i);
                placed.Add(i);
            }

            while (parent.DropDownItems.Count > 0 && parent.DropDownItems[parent.DropDownItems.Count - 1] is ToolStripSeparator)
                parent.DropDownItems.RemoveAt(parent.DropDownItems.Count - 1);
        }

        private ToolStripMenuItem TopLevel(string greek)
        {
            ToolStripMenuItem m = new ToolStripMenuItem();
            SetLabel(m, greek);
            return m;
        }

        private ToolStripMenuItem Group(string greek, Image image)
        {
            ToolStripMenuItem m = new ToolStripMenuItem { Image = image };
            SetLabel(m, greek);
            groups.Add(m);
            return m;
        }

        private SwimMenuHeader Header(string greek)
        {
            SwimMenuHeader h = new SwimMenuHeader { Font = headerFont ?? strip.Font };
            SetLabel(h, greek);
            return h;
        }

        private ToolStripMenuItem Entry(string greek, Image image, Action click)
        {
            ToolStripMenuItem m = new ToolStripMenuItem { Image = image };
            SetLabel(m, greek);
            m.Click += (s, e) => Run(click);
            return m;
        }

        private void Tip(ToolStripItem item, string greek)
        {
            if (item != null)
                tips[item] = greek;
        }

        private static void Run(Action click)
        {
            try
            {
                if (click != null)
                    click();
            }
            catch (Exception ex)
            {
                log.Error("A command of the menu of Kinovea Swim failed.", ex);
            }
        }
        #endregion

        #region Labels
        /// <summary>The Greek name of an item; Kinovea setting its own name again is answered by this one.</summary>
        private void SetLabel(ToolStripItem item, string greek)
        {
            if (item == null)
                return;

            bool known = labels.ContainsKey(item) || prefixes.ContainsKey(item) || states.ContainsKey(item);
            labels[item] = greek;
            if (!known)
                item.TextChanged += (s, e) => KeepLabel(item);
        }

        /// <summary>The name of an item in the language of the analysis.</summary>
        private static string Text(string greek)
        {
            string english;
            if (SwimLang.English && MenuEnglish.TryGetValue(greek, out english))
                return english;

            return SwimLang.T(greek);
        }

        /// <summary>The English of a Greek name of the menu (itself without one).</summary>
        private static string English(string greek)
        {
            string english;
            if (MenuEnglish.TryGetValue(greek, out english) || SwimLangEnglish.Map.TryGetValue(greek, out english))
                return english;

            return greek;
        }

        /// <summary>The name an item has to have now, or null for the one the program gave it.</summary>
        private string LabelOf(ToolStripItem item)
        {
            string greek;
            if (labels.TryGetValue(item, out greek))
                return Text(greek);

            Func<string> state;
            if (states.TryGetValue(item, out state))
            {
                string now = null;
                try
                {
                    now = state();
                }
                catch (Exception)
                {
                    now = null;
                }

                return now == null ? null : Text(now);
            }

            KeyValuePair<Func<string>, string> prefix;
            if (!prefixes.TryGetValue(item, out prefix))
                return null;

            // The word of Kinovea, or ours in either language, at the start: ours in the language of the analysis, the rest as it is.
            string current = item.Text ?? "";
            string kinovea = null;
            try
            {
                kinovea = prefix.Key();
            }
            catch (Exception)
            {
                kinovea = null;
            }

            foreach (string word in new[] { kinovea, prefix.Value, English(prefix.Value) })
            {
                if (!string.IsNullOrEmpty(word) && current.StartsWith(word, StringComparison.Ordinal))
                    return Text(prefix.Value) + current.Substring(word.Length);
            }

            return null;
        }

        private void KeepLabel(ToolStripItem item)
        {
            if (relabeling)
                return;

            string text = LabelOf(item);
            if (text == null || item.Text == text)
                return;

            relabeling = true;
            try
            {
                item.Text = text;
            }
            finally
            {
                relabeling = false;
            }
        }

        /// <summary>
        /// The Greek names of the items of Kinovea in their new places. Not the ones whose name says something that changes (undo and redo
        /// with their action, closing with the file, the debug logs on or off, the recent files, the languages, the filters).
        /// </summary>
        private void LabelItems()
        {
            string[,] names = new string[,]
            {
                { "mnuOpenFile", "Άνοιγμα βίντεο…" },
                { "mnuHistory", "Πρόσφατα" },
                { "mnuHistoryReset", "Καθαρισμός λίστας" },
                { "mnuOpenReplayWatcher", "Παρακολούθηση φακέλου" },
                { "mnuOpenReplayWatcherFolder", "Άνοιγμα φακέλου…" },
                { "mnuLoadAnnotations", "Φόρτωση…" },
                { "mnuSave", "Αποθήκευση" },
                { "mnuSaveAs", "Αποθήκευση ως…" },
                { "mnuUnloadAnnotations", "Αφαίρεση από το βίντεο" },
                { "mnuQuit", "Έξοδος" },
                { "mnuCutDrawing", "Αποκοπή σχεδίου" },
                { "mnuCopyDrawing", "Αντιγραφή σχεδίου" },
                { "mnuPasteDrawing", "Επικόλληση σχεδίου" },
                { "mnuOnePlayer", "1 βίντεο" },
                { "mnuTwoPlayers", "2 βίντεο" },
                { "mnuThreePlayers", "3 βίντεο" },
                { "mnuFourPlayers", "4 βίντεο" },
                { "mnuFivePlayers", "5 βίντεο" },
                { "mnuOneCapture", "1 κάμερα (ζωντανά)" },
                { "mnuTwoCaptures", "2 κάμερες" },
                { "mnuTwoMixed", "Κάμερα + βίντεο" },
                { "mnuVerticalLayout", "Κάθετη διάταξη" },
                { "mnuSwapScreens", "Αντιμετάθεση οθονών" },
                { "mnuFullScreen", "Πλήρης οθόνη" },
                { "mnuToggleNavigationPane", "Λίστα αρχείων" },
                { "mnuToggleCommonCtrls", "Κοινά χειριστήρια" },
                { "mnuAspectRatio", "Αναλογία" },
                { "mnuAspectRatioAuto", "Αυτόματα" },
                { "mnuRotation", "Περιστροφή" },
                { "mnuMirror", "Καθρέφτης" },
                { "mnuDeinterlace", "Απόπλεξη" },
                { "mnuDemosaicNone", "Κανένα" },
                { "mnuStabilization", "Σταθεροποίηση" },
                { "mnuExportVideo", "Βίντεο" },
                { "mnuExportVideoVideo", "Βίντεο…" },
                { "mnuExportVideoSlideshow", "Παρουσίαση καρέ…" },
                { "mnuExportVideoWithPauses", "Βίντεο με παύσεις…" },
                { "mnuExportVideoSideBySide", "Δίπλα-δίπλα…" },
                { "mnuExportImage", "Εικόνες" },
                { "mnuExportImageImage", "Εικόνα…" },
                { "mnuExportImageSequence", "Σειρά εικόνων…" },
                { "mnuExportImageKeys", "Βασικά καρέ…" },
                { "mnuExportImageSideBySide", "Δίπλα-δίπλα…" },
                { "mnuExportSpreadsheet", "Δεδομένα (Excel, CSV, JSON)" },
                { "mnuExportCSVTrajectory", "Τροχιές CSV…" },
                { "mnuExportTXTTrajectory", "Τροχιές κείμενο…" },
                { "mnuExportCSVChronometer", "Χρονόμετρα CSV…" },
                { "mnuExportDocument", "Έγγραφο (Word, Writer, Markdown)" },
                { "mnuImportImage", "Εισαγωγή εικόνας…" },
                { "mnuBackground", "Φόντο…" },
                { "mnuTestGrid", "Πλέγμα ελέγχου κάμερας" },
                { "mnuTimeCalibration", "Χρόνου…" },
                { "mnuCoordinateSystem", "Σύστημα συντεταγμένων" },
                { "mnuLensCalibration", "Φακού" },
                { "mnuLensCalibrationOpen", "Εισαγωγή…" },
                { "mnuLensCalibrationMode", "Λειτουργία βαθμονόμησης" },
                { "mnuLensCalibrationManual", "Χειροκίνητη εκτίμηση" },
                { "mnuLensCalibrationNone", "Καμία" },
                { "mnuCalibrationValidation", "Έλεγχος βαθμονόμησης…" },
                { "mnuScatterDiagram", "Διάγραμμα διασποράς…" },
                { "mnuLinearKinematics", "Γραμμικές κινηματικές…" },
                { "mnuAngularKinematics", "Γωνιακές κινηματικές…" },
                { "mnuAngleAngleDiagram", "Διάγραμμα γωνίας-γωνίας…" },
                { "mnuSwimAnalysis", "Ανάλυση αγώνα κολύμβησης…" },
                { "mnuLanguages", "Γλώσσα" },
                { "mnuTranslate1", "Μετάφραση…" },
                { "mnuTimecode", "Μορφή χρόνου" },
                { "mnuPointer", "Δείκτης ποντικιού" },
                { "mnuVariables", "Μεταβλητές" },
                { "mnuImportVariables", "Εισαγωγή…" },
                { "mnuPreferences", "Προτιμήσεις…" },
                { "mnuWindowProperties", "Ιδιότητες παραθύρου…" },
                { "mnuOpenNewWindow", "Νέο παράθυρο" },
                { "mnuReopenWindow", "Επανάνοιγμα" },
                { "mnuManageWindows", "Διαχείριση παραθύρων…" },
                { "mnuManageWorkspaces", "Χώροι εργασίας…" },
                { "mnuHelpContents", "Βοήθεια Kinovea" },
                { "mnuTranslate2", "Μετάφραση του Kinovea…" },
                { "mnuApplicationFolder", "Φάκελος ρυθμίσεων…" },
                { "mnuAbout", "Περί του Kinovea…" }
            };

            for (int i = 0; i < names.GetLength(0); i++)
                SetLabel(Item(names[i, 0]), names[i, 1]);

            // Undo and redo keep their action, closing its file: only the word of Kinovea at their start is ours.
            ToolStripMenuItem undo = Item("mnuUndo") as ToolStripMenuItem;
            ToolStripMenuItem redo = Item("mnuRedo") as ToolStripMenuItem;
            SetPrefix(undo, () => KinoveaWord(undo, "mnuUndo"), "Αναίρεση");
            SetPrefix(redo, () => KinoveaWord(redo, "mnuRedo"), "Επανάληψη");
            SetPrefix(Item("mnuCloseFile"), () => Languages.ScreenManagerLang.Generic_Close, "Κλείσιμο");
            SetPrefix(Item("mnuCloseFile2"), () => Languages.ScreenManagerLang.Generic_Close, "Κλείσιμο");

            // The debug logs: the name of what a click does now.
            SetState(Item("mnuEnableDebugLogs"), () => PreferencesManager.GeneralPreferences.EnableDebugLog ?
                "Απενεργοποίηση καταγραφής (debug)" : "Ενεργοποίηση καταγραφής (debug)");

            // The filters of the video, by their kind.
            foreach (ToolStripMenuItem menu in strip.Items.OfType<ToolStripMenuItem>())
                LabelFilters(menu.DropDownItems);
        }

        private void LabelFilters(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                if (item.Tag is VideoFilterType)
                {
                    switch ((VideoFilterType)item.Tag)
                    {
                        case VideoFilterType.None: SetLabel(item, "Κανονικό"); break;
                        case VideoFilterType.Kinogram: SetLabel(item, "Κινόγραμμα"); break;
                        case VideoFilterType.CameraMotion: SetLabel(item, "Κίνηση κάμερας"); break;
                        case VideoFilterType.LensCalibration: SetLabel(item, "Βαθμονόμηση φακού"); break;
                    }

                    continue;
                }

                ToolStripMenuItem m = item as ToolStripMenuItem;
                if (m != null && m.HasDropDownItems)
                    LabelFilters(m.DropDownItems);
            }
        }

        /// <summary>The word Kinovea starts the name of undo or redo with (from the resources it keeps in the item).</summary>
        private static string KinoveaWord(ToolStripItem item, string key)
        {
            System.Resources.ResourceManager rm = item != null ? item.Tag as System.Resources.ResourceManager : null;
            return rm != null ? rm.GetString(key) : null;
        }

        private void SetPrefix(ToolStripItem item, Func<string> kinovea, string greek)
        {
            if (item == null)
                return;

            bool known = prefixes.ContainsKey(item) || labels.ContainsKey(item) || states.ContainsKey(item);
            prefixes[item] = new KeyValuePair<Func<string>, string>(kinovea, greek);
            if (!known)
                item.TextChanged += (s, e) => KeepLabel(item);
        }

        private void SetState(ToolStripItem item, Func<string> greek)
        {
            if (item == null)
                return;

            bool known = prefixes.ContainsKey(item) || labels.ContainsKey(item) || states.ContainsKey(item);
            states[item] = greek;
            if (!known)
                item.TextChanged += (s, e) => KeepLabel(item);
        }

        private void Relabel()
        {
            relabeling = true;
            try
            {
                foreach (ToolStripItem item in labels.Keys.Concat(prefixes.Keys).Concat(states.Keys).ToList())
                {
                    if (item == null || item.IsDisposed)
                        continue;

                    string text = LabelOf(item);
                    if (text != null && item.Text != text)
                        item.Text = text;
                }
            }
            finally
            {
                relabeling = false;
            }

            foreach (KeyValuePair<ToolStripItem, string> kv in tips)
            {
                if (kv.Key != null && !kv.Key.IsDisposed)
                    kv.Key.ToolTipText = SwimLang.T(kv.Value);
            }

            if (mnuClassic != null)
                mnuClassic.Checked = CurrentSettings().ClassicMenu;

            RefreshLanguage();
            RefreshChip();
        }
        #endregion

        #region Toolbar
        /// <summary>
        /// The toolbar of Kinovea with names and parts: Άνοιγμα, Αποθήκευση, ↶ ↷, ΟΘΟΝΕΣ (its buttons of the screens), then ΑΝΑΛΥΣΗ
        /// (Αγώνας, Τεχνική) and ΑΝΑΦΟΡΑ (PDF, 📦 Δημιουργία αποστολής). Its own buttons stay, in their order.
        /// </summary>
        private void BuildToolbar()
        {
            NameKernels();

            ToolStripItem open = Find(toolbar.Items, "toolOpenFile");
            if (open != null)
            {
                open.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                SetLabel(open, "Άνοιγμα");
            }

            ToolStripItem save = Find(toolbar.Items, "toolSave");
            if (save != null)
            {
                save.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                SetLabel(save, "Αποθήκευση");
            }

            int at = save != null ? toolbar.Items.IndexOf(save) + 1 : open != null ? toolbar.Items.IndexOf(open) + 1 : 0;
            ToolStripButton undo = ToolButton("↶", null, () => Perform("mnuUndo"));
            ToolStripButton redo = ToolButton("↷", null, () => Perform("mnuRedo"));
            Follow(undo, Item("mnuUndo") as ToolStripMenuItem, "Ctrl+Z");
            Follow(redo, Item("mnuRedo") as ToolStripMenuItem, "Ctrl+Y");
            toolbar.Items.Insert(at, undo);
            toolbar.Items.Insert(at + 1, redo);

            ToolStripItem firstScreen = Find(toolbar.Items, "toolExplorer") ?? Find(toolbar.Items, "toolOnePlayer");
            if (firstScreen != null)
                toolbar.Items.Insert(toolbar.Items.IndexOf(firstScreen), Caption("ΟΘΟΝΕΣ"));

            ToolStripButton race = ToolButton("Αγώνας", "Ο πίνακας της ανάλυσης αγώνα του βίντεο", () => OpenAnalysis(false, null));
            ToolStripButton tech = ToolButton("Τεχνική", "Ο πίνακας της τεχνικής ανάλυσης του βίντεο", () => OpenAnalysis(true, null));
            ToolStripButton ai = ToolButton("AI pro", "Η ανάλυση της εκκίνησης, της στροφής ή του αγώνα από τον Claude (με κωδικό)", () => WithRace(m => m.ShowAiPro()));
            ToolStripButton pdf = ToolButton("PDF", "Το PDF της αναφοράς", () => WithRace(m => m.PdfByUser()));
            ToolStripButton package = ToolButton("📦 Δημιουργία αποστολής", "Η αποστολή για τους προπονητές: PDF, βίντεο με τα γραφικά, μήνυμα.",
                () => WithRace(m => m.CreatePackage()));
            race.Image = Properties.Resources.plot_16;
            race.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            pdf.Image = Properties.Resources.pdfexport;
            pdf.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(Caption("ΑΝΑΛΥΣΗ"));
            toolbar.Items.Add(race);
            toolbar.Items.Add(tech);
            toolbar.Items.Add(ai);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(Caption("ΑΝΑΦΟΡΑ"));
            toolbar.Items.Add(pdf);
            toolbar.Items.Add(package);

            // Room between the boxes of the buttons.
            foreach (ToolStripItem i in toolbar.Items)
            {
                if (i is ToolStripButton || i is ToolStripDropDownButton || i is ToolStripSplitButton)
                {
                    i.Margin = new Padding(2, 3, 2, 3);
                    if (i.DisplayStyle != ToolStripItemDisplayStyle.Image)
                        i.Padding = new Padding(4, 1, 4, 1);
                }
            }
        }

        private ToolStripButton ToolButton(string greek, string tip, Action click)
        {
            ToolStripButton b = new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Text };
            SetLabel(b, greek);
            if (tip != null)
                tips[b] = tip;
            b.Click += (s, e) => Run(click);
            return b;
        }

        private SwimMenuCaption Caption(string greek)
        {
            SwimMenuCaption c = new SwimMenuCaption { Font = captionFont ?? strip.Font, Margin = new Padding(8, 1, 2, 2) };
            SetLabel(c, greek);
            return c;
        }

        /// <summary>↶ and ↷ follow the menu of Kinovea: enabled with it, its text (with the action) as their tip.</summary>
        private static void Follow(ToolStripButton button, ToolStripMenuItem menu, string key)
        {
            if (menu == null)
                return;

            Action refresh = () =>
            {
                button.Enabled = menu.Enabled;
                button.ToolTipText = menu.Text + " (" + key + ")";
            };
            menu.EnabledChanged += (s, e) => refresh();
            menu.TextChanged += (s, e) => refresh();
            refresh();
        }

        private void Perform(string name)
        {
            ToolStripMenuItem m = Item(name) as ToolStripMenuItem;
            if (m != null && m.Enabled)
                m.PerformClick();
        }
        #endregion

        #region The analysis
        /// <summary>The settings of the analysis now (of its manager, else as read at the start).</summary>
        private SwimSettings CurrentSettings()
        {
            SwimAnalysisManager m = SwimAnalysisManager.Current;
            return m != null && m.Settings != null ? m.Settings : startSettings ?? new SwimSettings();
        }

        private static SwimAnalysisManager Swim()
        {
            return SwimAnalysisManager.Instance;
        }

        private void OpenAnalysis(bool? technical, string page)
        {
            if (host.OpenAnalysis != null && host.OpenAnalysis(technical, page))
                return;

            MessageBox.Show(SwimLang.T("Άνοιξε πρώτα ένα βίντεο (Αρχείο › Άνοιγμα βίντεο…)."), "Kinovea Swim", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void WithRace(Action<SwimAnalysisManager> action)
        {
            SwimAnalysisManager m = SwimAnalysisManager.Current;
            if (m == null || m.Race == null)
            {
                MessageBox.Show(SwimLang.T("Άνοιξε πρώτα ένα βίντεο με την ανάλυσή του (Αγώνας › Πίνακας ανάλυσης αγώνα)."), "Kinovea Swim",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            action(m);
        }

        /// <summary>As the ✕ of the window: the analysis of the video taken away, after a question.</summary>
        private static void DeleteAnalysis(SwimAnalysisManager m)
        {
            DialogResult answer = MessageBox.Show(
                SwimLang.T("Διαγραφή της ανάλυσης \"") + m.Race.DisplayName + SwimLang.T("\" από αυτό το βίντεο;\nΤο φύλλο στο Google Sheet δεν διαγράφεται."),
                SwimLang.T("Διαγραφή ανάλυσης"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                m.DeleteRace();
        }

        private void RefreshNeedRace()
        {
            SwimAnalysisManager m = SwimAnalysisManager.Current;
            bool race = m != null && m.Race != null;
            foreach (ToolStripItem i in needRace)
                i.Enabled = race;

            if (reportPackage != null)
                reportPackage.Enabled = race && m.CanCreatePackage;
            if (reportSheet != null)
                reportSheet.Enabled = race && !string.IsNullOrEmpty(m.Race.SheetUrl);
            if (racePanel != null)
                racePanel.ShortcutKeyDisplayString = KeyOf(PlayerScreenCommands.SwimShowPanel);
        }

        /// <summary>The ticks of Προβολή: the big screens and the analysis on the video, as they are now.</summary>
        private void RefreshView()
        {
            SwimSettings s = CurrentSettings();
            if (viewBig != null)
            {
                viewBig.Checked = host.BigScreensOn != null && host.BigScreensOn();
                viewBig.Enabled = viewBig.Checked || host.CanBigScreens == null || host.CanBigScreens();
            }

            viewGraphics.Checked = s.VideoGraphics;
            viewGraphics.ShortcutKeyDisplayString = KeyOf(PlayerScreenCommands.SwimGraphics);
            viewHudOn.Checked = s.HudLayout;
            viewHudOff.Checked = !s.HudLayout;
            viewReel.Checked = s.ReelOverlay;
            viewDial.Checked = !s.DialHidden;
            viewDial.ShortcutKeyDisplayString = KeyOf(PlayerScreenCommands.SwimDial);
        }

        private void ToggleBigScreens()
        {
            if (host.SetBigScreens != null && host.BigScreensOn != null)
                host.SetBigScreens(!host.BigScreensOn());
        }

        /// <summary>
        /// The buttons of the bars of the video now in a menu (the phase shown, its marks and their tools, the check of the proposals,
        /// undo), as the round button has them: the race view in Αγώνας, the technical view in Τεχνική. Made again at each opening.
        /// </summary>
        private void FillNow(ToolStripMenuItem menu, List<ToolStripItem> now, bool technical, ToolStripItem before)
        {
            foreach (ToolStripItem i in now)
            {
                menu.DropDownItems.Remove(i);
                renderer.Dim.Remove(i);
                i.Dispose();
            }

            now.Clear();
            int at = before != null && menu.DropDownItems.Contains(before) ? menu.DropDownItems.IndexOf(before) : menu.DropDownItems.Count;
            Action<ToolStripItem> add = item =>
            {
                menu.DropDownItems.Insert(at++, item);
                now.Add(item);
            };

            SwimAnalysisManager m = SwimAnalysisManager.Current;
            SwimRace race = m != null ? m.Race : null;
            bool tech = race != null && (race.IsTraining || m.TechnicalView);
            add(new ToolStripSeparator());
            add(new SwimMenuHeader { Text = SwimLang.T("Τώρα στο βίντεο"), Font = headerFont ?? strip.Font });
            if (race == null || tech != technical)
            {
                string hint = race == null ? "Τα κουμπιά των μπαρών εμφανίζονται εδώ όταν είναι ανοιχτή μια ανάλυση." :
                    technical ? "Τα κουμπιά της τεχνικής: άνοιξε τον πίνακα τεχνικής ανάλυσης." : "Τα κουμπιά του αγώνα: άνοιξε τον πίνακα ανάλυσης αγώνα.";
                add(new ToolStripMenuItem(SwimLang.T(hint)) { Enabled = false });
                Theme(menu.DropDown, menu.DropDownItems);
                return;
            }

            List<SwimBarButton> buttons = new List<SwimBarButton>();
            try
            {
                if (source == null || source.IsDisposed)
                    source = new SwimVideoBar(m, true);
                buttons.Add(source.DialPhaseButton());
                buttons.AddRange(source.DialButtons().Where(b => b.Id != "panel"));
            }
            catch (Exception e)
            {
                log.Error("Could not list the buttons of the swim analysis for the menu.", e);
            }

            foreach (SwimBarButton b in buttons)
            {
                SwimBarButton button = b;
                string text = (button.Id == "phase" ? SwimLang.T("Φάση: ") : "") + (button.Text ?? "") +
                    (string.IsNullOrEmpty(button.Value) ? "" : "  ·  " + button.Value);
                ToolStripMenuItem item = new ToolStripMenuItem(text.Replace("\n", " ").Replace("&", "&&"));
                if (!string.IsNullOrEmpty(button.Key))
                    item.ShortcutKeyDisplayString = button.Key;
                if (!string.IsNullOrEmpty(button.Tip))
                    item.ToolTipText = button.Tip;
                if (button.Look == SwimBarLook.Primary && boldFont != null)
                    item.Font = boldFont;
                if (button.Dim || button.Look == SwimBarLook.Quiet)
                    renderer.Dim.Add(item);
                item.Click += (s, e) => Run(button.Click);
                add(item);
            }

            Theme(menu.DropDown, menu.DropDownItems);
        }

        /// <summary>The key of a command of the video in the settings of Kinovea ("Ctrl+Shift+W"), or null.</summary>
        private static string KeyOf(PlayerScreenCommands command)
        {
            try
            {
                HotkeyCommand[] hotkeys = HotkeySettingsManager.LoadHotkeys("PlayerScreen");
                if (hotkeys == null)
                    return null;

                foreach (HotkeyCommand h in hotkeys)
                {
                    if (h != null && h.KeyData != Keys.None && h.CommandCode == (int)command)
                        return h.KeyData == Keys.Back ? "Backspace" : new KeysConverter().ConvertToString(h.KeyData);
                }
            }
            catch (Exception e)
            {
                log.Error("Could not read the keys of the player screen for the menu.", e);
            }

            return null;
        }

        private void SetLanguage(bool english)
        {
            Swim().SetLanguage(english);
            RefreshLanguage();
        }

        private void RefreshLanguage()
        {
            if (barEn == null || barEl == null)
                return;

            barEn.On = SwimLang.English;
            barEl.On = !SwimLang.English;
            barEn.Invalidate();
            barEl.Invalidate();
        }

        /// <summary>The state of the connection with the Google Sheet, on the right of the bar (a click: the page of the connection).</summary>
        private void RefreshChip()
        {
            if (sheetChip == null || sheetChip.IsDisposed)
                return;

            SwimAnalysisManager m = SwimAnalysisManager.Current;
            SwimSyncState state = m != null ? m.SyncState : CurrentSettings().IsConfigured ? SwimSyncState.Idle : SwimSyncState.NotConfigured;
            string word;
            Color dot;
            switch (state)
            {
                case SwimSyncState.Synced: word = "Google Sheet · συνδεδεμένο"; dot = SwimTheme.Green; break;
                case SwimSyncState.Sending: word = "Google Sheet · αποστολή…"; dot = SwimTheme.Accent; break;
                case SwimSyncState.Offline: word = "Google Sheet · χωρίς internet"; dot = SwimTheme.Warn; break;
                case SwimSyncState.Error: word = "Google Sheet · σφάλμα"; dot = SwimTheme.Red; break;
                case SwimSyncState.NotConfigured: word = "Google Sheet · χωρίς ρύθμιση"; dot = SwimTheme.Warn; break;
                default: word = "Google Sheet · έτοιμο"; dot = SwimTheme.Green; break;
            }

            string text = SwimMenuChip.DotText + SwimLang.T(word);
            string tip = m != null && !string.IsNullOrEmpty(m.SyncMessage) ? m.SyncMessage : SwimLang.T("Η σύνδεση με το Google Sheet");
            if (sheetChip.Text != text)
                sheetChip.Text = text;
            if (sheetChip.ToolTipText != tip)
                sheetChip.ToolTipText = tip;
            if (sheetChip.Dot != dot)
            {
                sheetChip.Dot = dot;
                sheetChip.Invalidate();
            }
        }

        private void ToggleClassic()
        {
            SwimSettings settings = Swim().Settings;
            settings.ClassicMenu = !settings.ClassicMenu;
            settings.Save();
            mnuClassic.Checked = settings.ClassicMenu;
            MessageBox.Show(SwimLang.T(settings.ClassicMenu ?
                "Το κλασικό μενού του Kinovea έρχεται στο επόμενο άνοιγμα του προγράμματος." :
                "Το μενού του Kinovea Swim μένει και στο επόμενο άνοιγμα."), "Kinovea Swim", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        #endregion

        #region Colours
        /// <summary>The colours of the analysis on the bar of the menus and on the toolbar (the menus get them when they open).</summary>
        private void ThemeBars()
        {
            strip.BackColor = SwimTheme.Surface;
            strip.ForeColor = SwimTheme.Text;
            Theme(strip, strip.Items);
            if (toolbar != null)
            {
                toolbar.BackColor = SwimTheme.Surface;
                toolbar.ForeColor = SwimTheme.Text;
                Theme(toolbar, toolbar.Items);
            }
        }

        private void Theme(ToolStrip owner, ToolStripItemCollection items)
        {
            if (owner.Renderer != renderer)
                owner.Renderer = renderer;

            foreach (ToolStripItem it in items)
            {
                ToolStripDropDownItem d = it as ToolStripDropDownItem;
                if (d == null)
                    continue;

                if (themed.Add(d))
                    d.DropDownOpening += (s, e) => Theme(d.DropDown, d.DropDownItems);

                if (d.HasDropDownItems)
                    Theme(d.DropDown, d.DropDownItems);
            }
        }
        #endregion
    }

    /// <summary>(2.18.0) The title of a part of a menu ("Στο βίντεο"): not an action.</summary>
    internal sealed class SwimMenuHeader : ToolStripMenuItem
    {
        public SwimMenuHeader()
        {
            Enabled = false;
        }
    }

    /// <summary>(2.18.0) The title of a part of the toolbar ("ΟΘΟΝΕΣ").</summary>
    internal sealed class SwimMenuCaption : ToolStripLabel
    {
    }

    /// <summary>(2.18.0) ΕΛ or EN on the bar of the menus: on when it is the language of the analysis.</summary>
    internal sealed class SwimMenuToggle : ToolStripMenuItem
    {
        public bool On;
    }

    /// <summary>(2.18.0) The state of the Google Sheet on the bar of the menus: a dot of its colour, then its words.</summary>
    internal sealed class SwimMenuChip : ToolStripMenuItem
    {
        public const string DotText = "● ";
        public Color Dot = SwimTheme.Dim;
    }

    /// <summary>(2.18.0) The bar, the menus and the toolbar of Kinovea in the colours of the analysis.</summary>
    internal sealed class SwimMenuRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>Items of the moment shown faint (a button of the bars that waits).</summary>
        public readonly HashSet<ToolStripItem> Dim = new HashSet<ToolStripItem>();

        public SwimMenuRenderer() : base(new SwimMenuColors())
        {
            RoundedEdges = false;
        }

        private Color TextColor(ToolStripItem item)
        {
            if (item is SwimMenuHeader)
                return SwimTheme.AccentLight;
            if (item is SwimMenuCaption)
                return SwimTheme.Faint;
            if (!item.Enabled)
                return SwimTheme.Faint;
            if (item.Selected || item.Pressed)
                return Color.White;
            if (Dim.Contains(item))
                return SwimTheme.Dim;
            return SwimTheme.Text;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            SwimMenuChip chip = e.Item as SwimMenuChip;
            if (chip != null && e.Text != null && e.Text.StartsWith(SwimMenuChip.DotText, StringComparison.Ordinal))
            {
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                Size dot = TextRenderer.MeasureText(e.Graphics, SwimMenuChip.DotText, e.TextFont, e.TextRectangle.Size, flags);
                TextRenderer.DrawText(e.Graphics, SwimMenuChip.DotText, e.TextFont, e.TextRectangle, chip.Dot, flags);
                Rectangle rest = new Rectangle(e.TextRectangle.X + dot.Width - 4, e.TextRectangle.Y, Math.Max(0, e.TextRectangle.Width - dot.Width + 4), e.TextRectangle.Height);
                TextRenderer.DrawText(e.Graphics, e.Text.Substring(SwimMenuChip.DotText.Length), e.TextFont, rest, SwimTheme.Text, flags);
                return;
            }

            Color color = TextColor(e.Item);
            if (e.TextDirection != ToolStripTextDirection.Horizontal)
            {
                e.TextColor = color;
                base.OnRenderItemText(e);
                return;
            }

            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && e.Item.Enabled ? SwimTheme.AccentLight : SwimTheme.Faint;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle box = new Rectangle(1, 2, e.Item.Width - 3, e.Item.Height - 5);
            SwimMenuToggle toggle = e.Item as SwimMenuToggle;
            if (toggle != null)
            {
                Box(e.Graphics, box, toggle.On ? SwimTheme.AccentDark : e.Item.Selected ? SwimTheme.RaisedHover : SwimTheme.Surface,
                    toggle.On || e.Item.Selected ? SwimTheme.Accent : SwimTheme.Border);
                return;
            }

            if (e.Item is SwimMenuChip)
            {
                Box(e.Graphics, box, e.Item.Selected ? SwimTheme.RaisedHover : SwimTheme.Surface, e.Item.Selected ? SwimTheme.Accent : SwimTheme.Border);
                return;
            }

            if (e.Item is SwimMenuHeader)
                return;

            base.OnRenderMenuItemBackground(e);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderButtonBackground(e);
                return;
            }

            ToolStripButton button = e.Item as ToolStripButton;
            ToolBox(e.Graphics, e.Item, button != null && button.Checked);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderDropDownButtonBackground(e);
                return;
            }

            ToolBox(e.Graphics, e.Item, false);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0 || r.Height <= 0)
                r = new Rectangle(4, (e.Item.Height - 16) / 2, 16, 16);
            r.Inflate(1, 1);
            Box(e.Graphics, r, SwimTheme.AccentDark, SwimTheme.Accent);
            if (e.Image != null && e.Item is ToolStripMenuItem && ((ToolStripMenuItem)e.Item).Image != null)
                return;

            SmoothingMode old = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                e.Graphics.DrawLines(pen, new[] { new PointF(x + w * 0.24f, y + h * 0.52f), new PointF(x + w * 0.43f, y + h * 0.72f), new PointF(x + w * 0.77f, y + h * 0.30f) });
            }

            e.Graphics.SmoothingMode = old;
        }

        private static void ToolBox(Graphics g, ToolStripItem item, bool on)
        {
            Rectangle r = new Rectangle(0, 0, item.Width - 1, item.Height - 1);
            Color fill;
            Color border;
            if (!item.Enabled)
            {
                fill = SwimTheme.Surface;
                border = SwimTheme.Border;
            }
            else if (item.Pressed || on)
            {
                fill = SwimTheme.AccentDark;
                border = SwimTheme.Accent;
            }
            else if (item.Selected)
            {
                fill = SwimTheme.RaisedHover;
                border = SwimTheme.Accent;
            }
            else
            {
                fill = SwimTheme.Raised;
                border = SwimTheme.Border;
            }

            Box(g, r, fill, border);
        }

        private static void Box(Graphics g, Rectangle r, Color fill, Color border)
        {
            if (r.Width <= 2 || r.Height <= 2)
                return;

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(r, 4))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            g.SmoothingMode = old;
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SwimMenuColors : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin { get { return SwimTheme.Surface; } }
        public override Color MenuStripGradientEnd { get { return SwimTheme.Surface; } }
        public override Color ToolStripGradientBegin { get { return SwimTheme.Surface; } }
        public override Color ToolStripGradientMiddle { get { return SwimTheme.Surface; } }
        public override Color ToolStripGradientEnd { get { return SwimTheme.Surface; } }
        public override Color ToolStripBorder { get { return SwimTheme.Border; } }
        public override Color ToolStripDropDownBackground { get { return SwimTheme.Raised; } }
        public override Color ImageMarginGradientBegin { get { return SwimTheme.Raised; } }
        public override Color ImageMarginGradientMiddle { get { return SwimTheme.Raised; } }
        public override Color ImageMarginGradientEnd { get { return SwimTheme.Raised; } }
        public override Color MenuBorder { get { return SwimTheme.Border; } }
        public override Color MenuItemBorder { get { return SwimTheme.Accent; } }
        public override Color MenuItemSelected { get { return SwimTheme.RaisedHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return SwimTheme.RaisedHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return SwimTheme.RaisedHover; } }
        public override Color MenuItemPressedGradientBegin { get { return SwimTheme.AccentDark; } }
        public override Color MenuItemPressedGradientMiddle { get { return SwimTheme.AccentDark; } }
        public override Color MenuItemPressedGradientEnd { get { return SwimTheme.AccentDark; } }
        public override Color SeparatorDark { get { return SwimTheme.Border; } }
        public override Color SeparatorLight { get { return SwimTheme.Raised; } }
        public override Color ButtonSelectedHighlight { get { return SwimTheme.RaisedHover; } }
        public override Color ButtonSelectedHighlightBorder { get { return SwimTheme.Accent; } }
        public override Color ButtonSelectedBorder { get { return SwimTheme.Accent; } }
        public override Color ButtonSelectedGradientBegin { get { return SwimTheme.RaisedHover; } }
        public override Color ButtonSelectedGradientMiddle { get { return SwimTheme.RaisedHover; } }
        public override Color ButtonSelectedGradientEnd { get { return SwimTheme.RaisedHover; } }
        public override Color ButtonPressedGradientBegin { get { return SwimTheme.AccentDark; } }
        public override Color ButtonPressedGradientMiddle { get { return SwimTheme.AccentDark; } }
        public override Color ButtonPressedGradientEnd { get { return SwimTheme.AccentDark; } }
        public override Color ButtonPressedHighlight { get { return SwimTheme.AccentDark; } }
        public override Color ButtonPressedBorder { get { return SwimTheme.Accent; } }
        public override Color ButtonCheckedGradientBegin { get { return SwimTheme.AccentDark; } }
        public override Color ButtonCheckedGradientMiddle { get { return SwimTheme.AccentDark; } }
        public override Color ButtonCheckedGradientEnd { get { return SwimTheme.AccentDark; } }
        public override Color ButtonCheckedHighlight { get { return SwimTheme.AccentDark; } }
        public override Color ButtonCheckedHighlightBorder { get { return SwimTheme.Accent; } }
        public override Color CheckBackground { get { return SwimTheme.AccentDark; } }
        public override Color CheckSelectedBackground { get { return SwimTheme.AccentDark; } }
        public override Color CheckPressedBackground { get { return SwimTheme.AccentDark; } }
        public override Color GripDark { get { return SwimTheme.Border; } }
        public override Color GripLight { get { return SwimTheme.Surface; } }
        public override Color OverflowButtonGradientBegin { get { return SwimTheme.Surface; } }
        public override Color OverflowButtonGradientMiddle { get { return SwimTheme.Surface; } }
        public override Color OverflowButtonGradientEnd { get { return SwimTheme.Surface; } }
    }
}
