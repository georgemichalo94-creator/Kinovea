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
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.19.0) AI pro in the analysis: the starts and turns with their lists, the moments of their points, the frames of the screens at
    /// those moments (each screen at the same moment of the race, the screens back where they were after), the requests, and Claude's
    /// answers into the list and the notes (as the coach's own actions: saved, sent with the next update of the sheet).
    /// </summary>
    public partial class SwimAnalysisManager
    {
        /// <summary>The long side of the frames sent to Claude (normal and high detail).</summary>
        public const int AiEdgeNormal = 1280;
        public const int AiEdgeHigh = 1920;

        /// <summary>The window of AI pro for the analysis of the video, after the password (asked once in a session of Kinovea).</summary>
        public void ShowAiPro()
        {
            IWin32Window owner = AiOwner();
            if (!HasVideo || Race == null)
            {
                MessageBox.Show(owner, SwimLang.T("Άνοιξε πρώτα ένα βίντεο με την ανάλυσή του (Αγώνας › Πίνακας ανάλυσης αγώνα)."), "AI pro",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!SwimAiGate.Unlocked)
            {
                using (FormSwimAiPassword f = new FormSwimAiPassword())
                {
                    if (f.ShowDialog(owner) != DialogResult.OK || !SwimAiGate.Unlocked)
                        return;
                }
            }

            using (FormSwimAiPro f = new FormSwimAiPro(this))
                f.ShowDialog(owner);
        }

        private IWin32Window AiOwner()
        {
            if (form != null && !form.IsDisposed && form.Visible)
                return form;
            return player != null && !player.IsDisposed ? player.FindForm() : null;
        }

        /// <summary>The starts and turns of the analysis that have a list to evaluate with.</summary>
        internal List<SwimTechPhase> AiPhases()
        {
            List<SwimTechPhase> list = new List<SwimTechPhase>();
            if (TechPhases == null || Race == null)
                return list;
            foreach (SwimTechPhase p in TechPhases)
            {
                if (TechSheetFor(p) != null)
                    list.Add(p);
            }

            return list;
        }

        /// <summary>The start or the turn where the video is now (null outside them).</summary>
        internal SwimTechPhase AiCurrentPhase()
        {
            return HasVideo ? PhaseOf(player.CurrentTimestamp) : null;
        }

        /// <summary>The screens of the group with a video (the screen of the analysis alone without a group).</summary>
        internal List<PlayerScreenUserInterface> AiScreens()
        {
            return GroupScreens;
        }

        internal static int AiScreenNumber(PlayerScreenUserInterface screen, IList<PlayerScreenUserInterface> screens)
        {
            if (screen != null && screen.SwimScreenNumber > 0)
                return screen.SwimScreenNumber;
            int i = screens != null ? screens.IndexOf(screen) : -1;
            return i >= 0 ? i + 1 : 1;
        }

        /// <summary>The moments of a start or a turn for Claude (SwimAiMoments.Tech) in the video of the analysis.</summary>
        internal List<SwimAiMoment> AiTechMoments(SwimTechPhase phase, SwimTechSheet sheet, int max)
        {
            if (!HasVideo)
                return new List<SwimAiMoment>();
            return SwimAiMoments.Tech(phase, sheet, Timing, TechCatalog, frameServer.VideoReader.WorkingZone.Start, frameServer.VideoReader.WorkingZone.End, max, SwimLang.Code);
        }

        /// <summary>The moments of the race for its comments (SwimAiMoments.Race) in the video of the analysis.</summary>
        internal List<SwimAiMoment> AiRaceMoments(int max)
        {
            if (!HasVideo)
                return new List<SwimAiMoment>();
            return SwimAiMoments.Race(Race, Computed, Timing, frameServer.VideoReader.WorkingZone.Start, frameServer.VideoReader.WorkingZone.End, max);
        }

        /// <summary>
        /// The frames of the screens at the moments: each screen at the same moment of the race (its time from its origin, as the group of
        /// screens keeps them together), at most edge pixels on the long side, JPEG, without the graphics of the analysis. The screens go back
        /// to their frames after.
        /// </summary>
        internal List<SwimAiImage> AiCapture(IList<SwimAiMoment> moments, IList<PlayerScreenUserInterface> screens, int edge, Action<int, int> progress)
        {
            List<SwimAiImage> images = new List<SwimAiImage>();
            if (!HasVideo || moments == null || moments.Count == 0 || screens == null)
                return images;

            List<PlayerScreenUserInterface> list = screens.Where(s => s != null && !s.IsDisposed && s.SwimFrameServer != null && s.SwimFrameServer.Loaded).ToList();
            if (list.Count == 0)
                return images;

            Dictionary<PlayerScreenUserInterface, long> was = new Dictionary<PlayerScreenUserInterface, long>();
            foreach (PlayerScreenUserInterface s in list)
                was[s] = s.CurrentTimestamp;

            ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            try
            {
                int done = 0;
                foreach (SwimAiMoment m in moments)
                {
                    long time = player.SwimTimeFromOrigin(m.Timestamp);
                    foreach (PlayerScreenUserInterface s in list)
                    {
                        long at = s == player ? m.Timestamp : s.SwimTimestampFromOrigin(time);
                        if (s != player && !s.SwimInVideo(at))
                            continue;

                        var reader = s.SwimFrameServer.VideoReader;
                        long half = (long)Math.Floor(Math.Max(1.0, reader.Info.AverageTimeStampsPerFrame) * 0.5);
                        s.ForcePosition(Math.Max(reader.WorkingZone.Start, at - half), true);
                        byte[] data;
                        int w, h;
                        if (FrameJpeg(s, edge, codec, out data, out w, out h))
                            images.Add(new SwimAiImage { Moment = m.N, Screen = AiScreenNumber(s, list), Width = w, Height = h, Jpeg = data });
                    }

                    done++;
                    if (progress != null)
                        progress(done, moments.Count);
                }
            }
            finally
            {
                foreach (KeyValuePair<PlayerScreenUserInterface, long> kv in was)
                {
                    if (kv.Key.IsDisposed)
                        continue;
                    try
                    {
                        kv.Key.ForcePosition(kv.Value, true);
                    }
                    catch (Exception e)
                    {
                        log.Error("Could not bring a screen back to its frame.", e);
                    }
                }
            }

            return images;
        }

        /// <summary>The frame a screen shows (with the drawings of Kinovea, without the graphics of the analysis) as a JPEG.</summary>
        private bool FrameJpeg(PlayerScreenUserInterface screen, int edge, ImageCodecInfo codec, out byte[] data, out int width, out int height)
        {
            data = null;
            width = height = 0;
            Size size = screen.SwimFrameServer.VideoReader.Info.ReferenceSize;
            if (size.Width <= 0 || size.Height <= 0)
                return false;

            Bitmap full = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            Bitmap small = null;
            try
            {
                bool own = screen == player;
                try
                {
                    if (own)
                        suppressExportOverlay = true;
                    screen.BeforeExportVideo();
                    screen.PaintFlushedImage(full);
                }
                finally
                {
                    screen.AfterExportVideo();
                    if (own)
                        suppressExportOverlay = false;
                }

                Bitmap use = full;
                double scale = Math.Min(1.0, edge / (double)Math.Max(size.Width, size.Height));
                if (scale < 0.999)
                {
                    small = new Bitmap(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)), PixelFormat.Format24bppRgb);
                    using (Graphics g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
                    }

                    use = small;
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    if (codec != null)
                    {
                        using (EncoderParameters p = new EncoderParameters(1))
                        {
                            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                            use.Save(ms, codec, p);
                        }
                    }
                    else
                    {
                        use.Save(ms, ImageFormat.Jpeg);
                    }

                    data = ms.ToArray();
                }

                width = use.Width;
                height = use.Height;
                return data.Length > 0;
            }
            catch (Exception e)
            {
                log.Error("Could not take a frame for AI pro.", e);
                return false;
            }
            finally
            {
                if (small != null)
                    small.Dispose();
                full.Dispose();
            }
        }

        /// <summary>The request of the evaluation of a start or a turn with its list.</summary>
        internal JObject AiTechRequest(SwimTechPhase phase, SwimTechSheet sheet, SwimAiAthlete athlete, IList<SwimAiMoment> moments, IList<SwimAiImage> images,
            int screens, string model)
        {
            string lang = SwimLang.Code;
            string context = SwimAiPrompt.TechContext(Race, phase, sheet, TechCatalog, TechMeasures(phase), athlete, moments, screens, images.Count, lang,
                AiCalibration());
            return SwimAiRequest.Build(model, SwimAiPrompt.TechSystem(lang), context, moments, images, screens, SwimAiPrompt.TechToolSchema(),
                SwimAiRequest.TechMaxTokens);
        }

        /// <summary>The request of the comments of the race.</summary>
        internal JObject AiRaceRequest(SwimAiAthlete athlete, IList<SwimAiMoment> moments, IList<SwimAiImage> images, int screens, string model)
        {
            string lang = SwimLang.Code;
            string context = SwimAiPrompt.RaceContext(Race, Computed, TechPhases, TechCatalog, athlete, moments, screens, images.Count, lang);
            return SwimAiRequest.Build(model, SwimAiPrompt.RaceSystem(lang), context, moments, images, screens, SwimAiPrompt.RaceToolSchema(),
                SwimAiRequest.RaceMaxTokens);
        }

        /// <summary>
        /// Claude's evaluation into the list of the phase (the analysis it was asked for only): proposals AI, its answers beside the points,
        /// the block of the notes. Saved and sent as the coach's scoring. Null when the analysis or the phase is not there any more.
        /// </summary>
        internal SwimAiApplied AiApplyTech(SwimRace race, string phaseId, string sheetId, SwimAiTechAnswer answer, string model)
        {
            if (race == null || race != Race || TechPhases == null)
                return null;
            SwimTechPhase phase = TechPhases.FirstOrDefault(p => p.Id == phaseId);
            SwimTechSheet sheet = TechCatalog.Sheet(sheetId);
            if (phase == null || sheet == null)
                return null;

            SwimAiApplied applied = SwimAiApply.Tech(race, phase, sheet, answer, model, DateTime.Now);
            AiCalibrate(TechEval(phase, sheet, false), sheet);
            Changed(false);
            ScheduleSend(true);
            return applied;
        }

        /// <summary>Claude's comments of the race into the notes of the analysis (the analysis it was asked for only).</summary>
        internal bool AiApplyRace(SwimRace race, SwimAiRaceAnswer answer, string model)
        {
            if (race == null || race != Race)
                return false;
            SwimAiApply.Race(race, answer, model, DateTime.Now);
            Changed(false);
            ScheduleSend(true);
            return true;
        }

        /// <summary>The coach's answers beside Claude's of an evaluation that changed, into the calibration of the next requests.</summary>
        private void AiCalibrate(SwimTechEval eval, SwimTechSheet sheet)
        {
            if (eval == null || eval.Ai == null || Race == null || sheet == null)
                return;
            try
            {
                SwimAiCalibration calibration = SwimAiCalibration.Current;
                if (calibration.Sync(Race, eval, sheet))
                    calibration.Save();
            }
            catch (Exception e)
            {
                log.Error("Could not update the calibration of AI pro.", e);
            }
        }

        internal static SwimAiCalibration AiCalibration()
        {
            try
            {
                return SwimAiCalibration.Current;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>What a request cost, in the book of the month.</summary>
        internal void AiRecord(SwimAiReply reply)
        {
            if (Settings == null || reply == null)
                return;
            SwimAiBook.Add(Settings.AiProUsage, DateTime.Now, reply);
            Settings.Save();
        }

        /// <summary>The key of the Claude API kept in the settings (null without one, or when it cannot be read here).</summary>
        internal string AiKey
        {
            get { return Settings != null ? SwimAiKey.Unprotect(Settings.AiProKey) : null; }
        }

        internal void SetAiKey(string key)
        {
            if (Settings == null)
                return;
            Settings.AiProKey = SwimAiKey.Protect(key);
            Settings.Save();
        }

        internal void SetAiAthlete(SwimAiAthlete athlete)
        {
            if (Race == null || athlete == null)
                return;
            Race.AiAthlete = athlete;
            MarkDirty();
        }
    }
}
