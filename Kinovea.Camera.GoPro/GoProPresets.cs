#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System.Collections.Generic;
using System.Linq;

namespace Kinovea.Camera.GoPro
{
    /// <summary>One setting of a set: the setting, the values to try in order (the models do not all take the same values) and its label.</summary>
    internal sealed class GoProSettingValue
    {
        public byte Setting;
        public int[] Values;
        public string Label;

        public GoProSettingValue(byte setting, string label, params int[] values)
        {
            Setting = setting;
            Label = label;
            Values = values;
        }
    }

    /// <summary>A ready-made set of settings of the GoPro cameras.</summary>
    internal sealed class GoProPreset
    {
        public string Id;
        public string NameGreek;
        public string NameEnglish;
        public string ShortGreek;
        public string ShortEnglish;
        public int Fps;
        public int[] Resolutions;
        public string ResolutionLabel;
        public List<GoProSettingValue> Settings;

        public string Name
        {
            get { return GoProText.T(NameGreek, NameEnglish); }
        }

        /// <summary>The short name for the settings tile ("αγώνας", "τεχνική").</summary>
        public string Short
        {
            get { return GoProText.T(ShortGreek, ShortEnglish); }
        }

        /// <summary>What the set puts: "2.7K 4:3 · 50 fps · Linear · HyperSmooth off · 50 Hz".</summary>
        public string Description
        {
            get { return string.Join(" · ", Settings.Select(s => s.Label)); }
        }

        /// <summary>The camera has the values of this set now.</summary>
        public bool Matches(GoProState s)
        {
            return Resolutions.Contains(s.Resolution) && GoProIds.Fps(s.Fps) == Fps && s.Lens == 4 && s.Hypersmooth == 0 && GoProIds.AntiFlicker50(s.AntiFlicker);
        }
    }

    /// <summary>
    /// (Swim 2.20.0) The sets of settings of the GoPro cameras, the same for all of them: for races (the whole race, 50 images per
    /// second without flicker under the lights of 50 Hz) and for technique analysis (starts, turns, underwater: 100 images per second,
    /// 0.01 s per image). Both with the Linear lens (straight lines for distances and angles), HyperSmooth off (the camera is fixed and
    /// the stabilisation moves the image) and the anti-flicker of 50 Hz. The anti-flicker goes first: it changes the frame rates that the
    /// camera offers (25/50/100/200).
    /// </summary>
    internal static class GoProPresets
    {
        public const string Race = "race";
        public const string Technique = "technique";

        private static readonly List<GoProPreset> all = new List<GoProPreset>
        {
            new GoProPreset
            {
                Id = Race,
                NameGreek = "Ρυθμίσεις αγώνα",
                NameEnglish = "Race settings",
                ShortGreek = "αγώνας",
                ShortEnglish = "race",
                Fps = 50,
                Resolutions = new[] { 6, 111 },
                Settings = new List<GoProSettingValue>
                {
                    // 50 Hz: 3 on the recent models, 1 on the older ones.
                    new GoProSettingValue(GoProIds.SettingAntiFlicker, "50 Hz", 3, 1),
                    // 2.7K 4:3: 6 up to the HERO11, 111 on the HERO12 and later.
                    new GoProSettingValue(GoProIds.SettingResolution, "2.7K 4:3", 6, 111),
                    new GoProSettingValue(GoProIds.SettingFps, "50 fps", 6),
                    new GoProSettingValue(GoProIds.SettingLens, "Linear", 4),
                    new GoProSettingValue(GoProIds.SettingHypersmooth, "HyperSmooth off", 0)
                }
            },
            new GoProPreset
            {
                Id = Technique,
                NameGreek = "Ρυθμίσεις τεχνικής ανάλυσης",
                NameEnglish = "Technique analysis settings",
                ShortGreek = "τεχνική",
                ShortEnglish = "technique",
                Fps = 100,
                Resolutions = new[] { 4 },
                Settings = new List<GoProSettingValue>
                {
                    new GoProSettingValue(GoProIds.SettingAntiFlicker, "50 Hz", 3, 1),
                    new GoProSettingValue(GoProIds.SettingResolution, "2.7K", 4),
                    new GoProSettingValue(GoProIds.SettingFps, "100 fps", 2),
                    new GoProSettingValue(GoProIds.SettingLens, "Linear", 4),
                    new GoProSettingValue(GoProIds.SettingHypersmooth, "HyperSmooth off", 0)
                }
            }
        };

        public static IList<GoProPreset> All
        {
            get { return all; }
        }

        public static GoProPreset Find(string id)
        {
            return all.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>The set whose values the camera has now (null if none).</summary>
        public static GoProPreset Matching(GoProState s)
        {
            return all.FirstOrDefault(p => p.Matches(s));
        }
    }
}
