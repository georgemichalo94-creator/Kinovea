#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Globalization;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.37.0) A distance typed or turned with the wheel, always with two decimals (the analyst's request: when it is not measured on the
    /// video with the calibration, the distance is written in the box or chosen with the wheel and the arrows, as a number box).
    /// Empty is "not measured" (null). The text is written with a point ("3.05"), as everywhere in the analysis, and read with a point or
    /// a comma. The wheel moves it by 0.01 a notch (Ctrl: 0.10, Shift: 1.00); the arrows by 0.01, faster when held. From empty, the first
    /// step goes to Seed (the usual value of the distance) instead of counting up from 0.
    /// Inline (in the bar beside the video): Enter or Tab keeps the value, Esc drops it, and no key goes on to the keys of the player
    /// (Backspace would undo the last mark, the arrows would move the video).
    /// </summary>
    public class SwimDecimalBox : NumericUpDown
    {
        private bool empty = true;
        private bool quiet;
        private int wheelDelta;

        /// <summary>The user changed the number (typed and confirmed, wheel, arrows); not when it is set by the program.</summary>
        public event EventHandler NumberChanged;

        /// <summary>Inline: Enter or Tab (the value is in Number).</summary>
        public event EventHandler Committed;

        /// <summary>Inline: Esc.</summary>
        public event EventHandler Cancelled;

        /// <summary>Its keys stay in it, Enter / Tab / Esc end it (the editor of the bar beside the video).</summary>
        public bool Inline { get; set; }

        /// <summary>Where the first step from an empty box goes (the usual value of this distance).</summary>
        public decimal Seed { get; set; }

        public SwimDecimalBox()
        {
            DecimalPlaces = 2;
            Increment = 0.01m;
            Minimum = 0m;
            Maximum = 50m;
            TextAlign = HorizontalAlignment.Right;
            // Held: 0.01, then 0.05 after a second, 0.10 after two, 0.50 after four.
            Accelerations.Add(new NumericUpDownAcceleration(1, 0.05m));
            Accelerations.Add(new NumericUpDownAcceleration(2, 0.10m));
            Accelerations.Add(new NumericUpDownAcceleration(4, 0.50m));

            // Empty at first ("0" written by the base class is not a value typed).
            UserEdit = false;
            empty = true;
            WriteText();
        }

        /// <summary>
        /// The number of the box (null: empty, not measured). What is being typed counts already (read without touching the text, as the
        /// boxes of the window commit at each key); set by the program, it replaces what was typed.
        /// </summary>
        public double? Number
        {
            get
            {
                if (UserEdit)
                {
                    string text = Text ?? "";
                    if (text.Trim().Length == 0)
                        return null;
                    double? typed = Read(text);
                    if (typed.HasValue)
                        return (double)Clamp(typed.Value);
                }

                // Not a number (yet): the number as it was.
                return empty ? (double?)null : (double)Kept;
            }
            set { SetNumber(value, false); }
        }

        /// <summary>
        /// (2.38.0) The number of the box without NumericUpDown reading what is being typed: its Value reads the text in the language of
        /// Windows, and in Greek the point is the thousands separator. A distance typed with one point too many ("3.30.", read as
        /// 330) became the most of the box, 50.00, in the window that keeps each key typed.
        /// </summary>
        private decimal Kept
        {
            get
            {
                if (!UserEdit)
                    return Value;

                UserEdit = false;
                try
                {
                    return Value;
                }
                finally
                {
                    UserEdit = true;
                }
            }
        }

        /// <summary>The number from the program, unless the user is typing in the box (what he types stays).</summary>
        public void ShowNumber(double? value)
        {
            if (ContainsFocus && UserEdit)
                return;
            SetNumber(value, false);
        }

        private decimal Clamp(double value)
        {
            decimal d = Math.Round((decimal)Math.Max(-1e9, Math.Min(1e9, value)), DecimalPlaces, MidpointRounding.AwayFromZero);
            return Math.Max(Minimum, Math.Min(Maximum, d));
        }

        /// <summary>The text of a value: two decimals ("3.05"), or a whole number for a box without decimals (a lane).</summary>
        private string TextOf(decimal value)
        {
            return value.ToString(DecimalPlaces == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>A number of the box as text ("3.05"), empty for none: what the box shows for it.</summary>
        public static string Format(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? Math.Round(value.Value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
                : "";
        }

        /// <summary>A number typed with a point or a comma ("3.05", "3,05", " 3 "), null when it is not one.</summary>
        public static double? Read(string text)
        {
            string t = (text ?? "").Trim().Replace(" ", "").Replace(',', '.');
            double value;
            if (t.Length == 0 || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                return null;
            return value;
        }

        private void SetNumber(double? value, bool byUser)
        {
            bool wasEmpty = empty;
            decimal was = Kept;
            quiet = true;
            try
            {
                if (value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value))
                {
                    decimal d = Clamp(value.Value);
                    empty = false;
                    if (Value != d)
                        Value = d;
                }
                else
                {
                    empty = true;
                }

                UserEdit = false;
                WriteText();
            }
            finally
            {
                quiet = false;
            }

            if (byUser && (wasEmpty != empty || (!empty && was != Value)))
                RaiseChanged();
        }

        private void RaiseChanged()
        {
            EventHandler handler = NumberChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        /// <summary>The text typed read (once): a number, empty (none), or else the number stays as it was.</summary>
        private void ParseTyped(bool byUser)
        {
            if (!UserEdit)
                return;

            UserEdit = false;
            string text = Text ?? "";
            if (text.Trim().Length == 0)
            {
                SetNumber(null, byUser);
                return;
            }

            double? value = Read(text);
            if (value.HasValue)
                SetNumber(value, byUser);
            else
                WriteText();
        }

        private void WriteText()
        {
            string text = empty ? "" : TextOf(Value);
            if (Text != text)
            {
                // As the base class does: the text changed by the program is not something the user typed.
                ChangingText = true;
                Text = text;
            }
        }

        protected override void UpdateEditText()
        {
            if (UserEdit)
            {
                ParseTyped(!quiet);
                return;
            }

            WriteText();
        }

        protected override void ValidateEditText()
        {
            ParseTyped(true);
            WriteText();
        }

        protected override void OnValueChanged(EventArgs e)
        {
            // A value set by the base class (the arrows) leaves the empty state.
            if (!quiet)
                empty = false;
            base.OnValueChanged(e);
        }

        public override void UpButton()
        {
            Step(1);
        }

        public override void DownButton()
        {
            Step(-1);
        }

        /// <summary>One step of the arrows (the increment of the moment: faster when held).</summary>
        private void Step(int direction)
        {
            ParseTyped(true);
            if (empty)
            {
                SetNumber((double)Seed, true);
                return;
            }

            decimal before = Value;
            if (direction > 0)
                base.UpButton();
            else
                base.DownButton();
            WriteText();
            if (Value != before)
                RaiseChanged();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Not the base class: it does not step with Shift, and it steps three times a notch.
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null)
                handled.Handled = true;

            wheelDelta += e.Delta;
            int notches = wheelDelta / 120;
            if (notches == 0)
                return;

            wheelDelta -= notches * 120;
            Turn(notches, ModifierKeys);
        }

        /// <summary>The wheel turned 'notches' (up: positive) with these keys: 0.01 a notch, Ctrl 0.10, Shift 1.00.</summary>
        internal void Turn(int notches, Keys modifiers)
        {
            ParseTyped(true);
            if (notches == 0)
                return;

            if (empty)
            {
                SetNumber((double)Seed, true);
                notches -= Math.Sign(notches);
                if (notches == 0)
                    return;
            }

            decimal step = DecimalPlaces == 0 ? 1m : (modifiers & Keys.Control) != 0 ? 0.10m : (modifiers & Keys.Shift) != 0 ? 1.00m : 0.01m;
            decimal next = Math.Max(Minimum, Math.Min(Maximum, Value + notches * step));
            if (next == Value)
                return;

            quiet = true;
            try
            {
                Value = next;
            }
            finally
            {
                quiet = false;
            }

            WriteText();
            RaiseChanged();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Inline)
                return base.ProcessCmdKey(ref msg, keyData);

            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Enter || code == Keys.Tab)
            {
                ValidateEditText();
                EventHandler committed = Committed;
                if (committed != null)
                    committed(this, EventArgs.Empty);
                return true;
            }

            if (code == Keys.Escape)
            {
                EventHandler cancelled = Cancelled;
                if (cancelled != null)
                    cancelled(this, EventArgs.Empty);
                return true;
            }

            // Every other key stays in the box: none goes on to the keys of the player screen around it.
            return false;
        }
    }

    /// <summary>
    /// (2.37.0) A text of the race (organization, place, date, link) changed in the bar beside the video: Enter keeps it, Esc drops it,
    /// and its keys stay in it (none goes on to the keys of the player screen around the bar).
    /// </summary>
    internal sealed class SwimInlineTextBox : TextBox
    {
        public event EventHandler Committed;
        public event EventHandler Cancelled;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Enter || code == Keys.Tab)
            {
                EventHandler committed = Committed;
                if (committed != null)
                    committed(this, EventArgs.Empty);
                return true;
            }

            if (code == Keys.Escape)
            {
                EventHandler cancelled = Cancelled;
                if (cancelled != null)
                    cancelled(this, EventArgs.Empty);
                return true;
            }

            // Ctrl+A: the whole text (a single line box does not do it on every Windows).
            if (keyData == (Keys.Control | Keys.A))
            {
                SelectAll();
                return true;
            }

            // The other shortcuts with Ctrl are not keys of the player here (Ctrl+Shift+W, …): only those of the text work (copy, paste,
            // cut, undo, the moves by word). The plain keys and Alt (Alt+F4) go to the box and to Windows, never to the player screen.
            if ((keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
            {
                bool text = code == Keys.C || code == Keys.V || code == Keys.X || code == Keys.Z || code == Keys.Left || code == Keys.Right ||
                    code == Keys.Home || code == Keys.End || code == Keys.Delete || code == Keys.Insert;
                return !text;
            }

            return false;
        }
    }
}
