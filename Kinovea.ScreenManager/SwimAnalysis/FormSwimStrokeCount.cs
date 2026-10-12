#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// Strokes of the segment that ends at a checkpoint: Enter keeps the count shown, a number replaces it,
    /// an empty box removes the typed value (the count comes from the stroke marks again), Esc changes nothing.
    /// </summary>
    internal class FormSwimStrokeCount : Form
    {
        private readonly TextBox box;
        private readonly Label lblError;

        /// <summary>The count confirmed or typed (OK with a number).</summary>
        public double? Value { get; private set; }

        /// <summary>OK with an empty box: no typed count for this segment.</summary>
        public bool Cleared { get; private set; }

        /// <param name="sequence">Several segments are asked one after the other: Esc stops the sequence.</param>
        public FormSwimStrokeCount(string title, string detail, double? count, bool sequence)
        {
            Text = "Χεριές";
            Font = SwimTheme.Ui(9f, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            ClientSize = new Size(400, 176);
            BackColor = SwimTheme.Back;
            ForeColor = SwimTheme.Text;
            HandleCreated += (s, e) => SwimTheme.DarkTitleBar(Handle);

            Label lblTitle = new Label
            {
                Text = title,
                Left = 14,
                Top = 10,
                Width = 372,
                Height = 30,
                Font = SwimTheme.Display(14f),
                ForeColor = SwimTheme.Accent
            };

            box = new TextBox
            {
                Text = Format(count),
                Left = 14,
                Top = 48,
                Width = 96,
                Font = SwimTheme.Display(18f),
                TextAlign = HorizontalAlignment.Center
            };

            Label lblDetail = new Label
            {
                Text = detail ?? "",
                Left = 122,
                Top = 46,
                Width = 264,
                Height = 48,
                ForeColor = SwimTheme.Dim
            };

            Label lblHint = new Label
            {
                Text = "Enter: σωστό · γράψε τον σωστό αριθμό αν είναι λάθος (π.χ. 7 ή 7,5) · κενό: χωρίς αριθμό",
                Left = 14,
                Top = 98,
                Width = 372,
                Height = 32,
                ForeColor = SwimTheme.Faint
            };

            lblError = new Label { Left = 14, Top = 130, Width = 210, Height = 34, ForeColor = SwimTheme.Red };

            Button ok = new SwimButton(true) { Text = "OK", Left = 232, Top = 136, Width = 72, Height = 28, Primary = true };
            Button cancel = new SwimButton(true)
            {
                Text = sequence ? "Σταμάτημα" : "Άκυρο",
                DialogResult = DialogResult.Cancel,
                Left = 310,
                Top = 136,
                Width = 76,
                Height = 28
            };
            ok.Click += (s, e) => Accept();

            Controls.Add(lblTitle);
            Controls.Add(box);
            Controls.Add(lblDetail);
            Controls.Add(lblHint);
            Controls.Add(lblError);
            Controls.Add(ok);
            Controls.Add(cancel);
            SwimTheme.Apply(this);
            AcceptButton = ok;
            CancelButton = cancel;

            Shown += (s, e) =>
            {
                box.Focus();
                box.SelectAll();
            };
        }

        private void Accept()
        {
            string text = box.Text.Trim();
            if (text.Length == 0)
            {
                Cleared = true;
                Value = null;
                DialogResult = DialogResult.OK;
                return;
            }

            double value;
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value < 0 || value > 300)
            {
                lblError.Text = "Γράψε τον αριθμό των χεριών, π.χ. 7 ή 7,5.";
                box.Focus();
                box.SelectAll();
                return;
            }

            Value = Math.Round(value, 2);
            Cleared = false;
            DialogResult = DialogResult.OK;
        }

        private static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') : "";
        }
    }
}
