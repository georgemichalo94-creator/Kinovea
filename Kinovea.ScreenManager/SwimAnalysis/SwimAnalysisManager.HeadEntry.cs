using System;
using System.Collections.Generic;
using System.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.41.0) The entry of the start is where the head goes into the water, the analyst's request («θα βγάλουμε από την εκκίνηση το
    /// είσοδος χεριών και θα βάλουμε είσοδος κεφάλι, άλλαξε το από παντού»). An analysis marked with the entry of the hands before
    /// 2.41.0 keeps it (its speeds from the head behind the hands, its reports say «χέρια») until the analyst marks or measures the
    /// entry there: then it passes to the head's, and its entries of the hands go, so that no time or distance of the hands is taken for
    /// the head's. «Αναίρεση» puts them back.
    /// </summary>
    public partial class SwimAnalysisManager
    {
        private const string EntryUndoPrefix = "entryhead:";

        /// <summary>What an analysis had of the entry of the hands when it passed to the head's (for «Αναίρεση»).</summary>
        private sealed class EntryUndo
        {
            public bool? EntryHead;
            public double? DistHead, VelHead;
            public List<SwimMark> Marks = new List<SwimMark>();
            public Dictionary<string, double?> PhaseEntries = new Dictionary<string, double?>();
        }

        private readonly Dictionary<string, EntryUndo> entryUndo = new Dictionary<string, EntryUndo>();

        /// <summary>
        /// Before an entry is marked or measured: the analysis is the head's from now on. One marked with the hands passes to the head's:
        /// its entries of the hands go (the marks, and the distances of the attempts of a training video; the distance of the race stays
        /// in EntryDistHands, no longer used) and the passing goes on the undo stack. The text for the status when it passed, else null.
        /// </summary>
        private string EnsureHeadEntry()
        {
            if (Race == null)
                return null;

            if (Race.HeadEntry())
            {
                Race.EntryHead = true;
                return null;
            }

            EntryUndo undo = new EntryUndo { EntryHead = Race.EntryHead, DistHead = Race.EntryDistHead, VelHead = Race.EntryVelHead };
            undo.Marks.AddRange(Race.Marks.Where(m => m.Type == SwimMarkType.Entry));
            Race.Marks.RemoveAll(m => m.Type == SwimMarkType.Entry);
            foreach (SwimMark old in undo.Marks)
                ForgetTaught(old);

            if (Race.PhaseValues != null)
            {
                foreach (KeyValuePair<string, SwimPhaseManual> pair in Race.PhaseValues)
                {
                    if (pair.Value == null || !pair.Value.EntryDist.HasValue)
                        continue;
                    undo.PhaseEntries[pair.Key] = pair.Value.EntryDist;
                    pair.Value.EntryDist = null;
                }
            }

            Race.EntryHead = true;
            string id = EntryUndoPrefix + Guid.NewGuid().ToString("N");
            entryUndo[id] = undo;
            undoStack.Add(id);
            RefreshEntryFields();

            return SwimLang.T("Η ανάλυση πέρασε στην είσοδο του κεφαλιού: η είσοδος των χεριών βγήκε (η Αναίρεση τη φέρνει πίσω).");
        }

        /// <summary>
        /// After a passing to the entry of the head or its «Αναίρεση»: the boxes of the entry in the window show the entry of the analysis
        /// now (after what changed it has finished). A box left with the other entry's distance would be read back as a value typed
        /// (the hands' distance taken for the head's, or the head passing again).
        /// </summary>
        private void RefreshEntryFields()
        {
            FormSwimAnalysis window = form;
            if (window == null || window.IsDisposed || !window.IsHandleCreated)
                return;

            window.BeginInvoke((Action)(() =>
            {
                if (!window.IsDisposed)
                    window.RefreshManualFields();
            }));
        }

        /// <summary>«Αναίρεση» of a passing to the entry of the head: the entry of the hands back. False when the id is not one.</summary>
        private bool UndoHeadEntry(string id)
        {
            EntryUndo undo;
            if (Race == null || id == null || !entryUndo.TryGetValue(id, out undo))
                return false;

            entryUndo.Remove(id);
            foreach (SwimMark m in undo.Marks)
            {
                if (!Race.Marks.Any(x => x.Id == m.Id))
                    Race.Marks.Add(m);
            }

            foreach (KeyValuePair<string, double?> pair in undo.PhaseEntries)
                Race.GetPhaseValues(pair.Key, true).EntryDist = pair.Value;

            // As before the passing: also the distance and the speed of the head written with it (a later passing starts without them).
            Race.EntryDistHead = undo.DistHead;
            Race.EntryVelHead = undo.VelHead;
            Race.EntryHead = undo.EntryHead.HasValue ? undo.EntryHead : false;
            Status(SwimLang.T("Αναιρέθηκε: η είσοδος του κεφαλιού (η ανάλυση έχει ξανά την είσοδο των χεριών)."), false);
            Changed(true);
            RefreshEntryFields();
            return true;
        }

        /// <summary>
        /// The entry of the start marked on the video (E): the head's. In an analysis marked with the hands, it passes to the head's first
        /// (the status says so after the time of the mark).
        /// </summary>
        private void AddEntryMark(long timestamp)
        {
            string passed = EnsureHeadEntry();
            AddMark(SwimMarkType.Entry, timestamp, SwimLang.T("Είσοδος κεφαλιού στο νερό"));
            if (passed != null)
                Status((string.IsNullOrEmpty(LastStatus) ? "" : LastStatus + "\n") + passed + SwimLang.T(" Μέτρησε και την απόσταση εισόδου του κεφαλιού (📍)."), false);
        }

        /// <summary>
        /// The distance of the entry of the start (pool units, measured or typed): the head's. In an analysis marked with the hands, it
        /// passes to the head's first. The text for the status when it passed, else null.
        /// </summary>
        private string SetEntryDistance(double? value)
        {
            string passed = EnsureHeadEntry();
            Race.EntryDistHead = value;
            return passed == null ? null : passed + SwimLang.T(" Σημείωσε και το καρέ που μπαίνει το κεφάλι στο νερό (E).");
        }

        /// <summary>
        /// The distance and the speed of the entry typed in the window: the head's. An analysis marked with the hands passes to the
        /// head's when one of the two changed (only the one changed is written: the other one was the hands').
        /// </summary>
        public void SetTypedEntry(double? distance, double? speed)
        {
            if (Race == null)
                return;

            bool sameDistance = Same(Race.EntryDistance(), distance), sameSpeed = Same(Race.EntryVelocity(), speed);
            if (sameDistance && sameSpeed)
                return;

            string passed = EnsureHeadEntry();
            if (!sameDistance)
                Race.EntryDistHead = distance;
            if (!sameSpeed)
                Race.EntryVelHead = speed;
            // (The boxes of the window show the entry of the head after its commit: EnsureHeadEntry.)
            if (passed != null)
                Status(passed + SwimLang.T(" Σημείωσε και το καρέ που μπαίνει το κεφάλι στο νερό (E)."), false);
        }

        /// <summary>The distance of the entry of an attempt of a training video: the head's (as SetEntryDistance).</summary>
        private string SetPhaseEntryDistance(string phaseId, double? value)
        {
            string passed = EnsureHeadEntry();
            Race.GetPhaseValues(phaseId, true).EntryDist = value;
            return passed == null ? null : passed + SwimLang.T(" Σημείωσε ξανά την είσοδο του κεφαλιού (E) σε κάθε προσπάθεια.");
        }
    }
}
