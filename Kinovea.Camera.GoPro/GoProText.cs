#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace Kinovea.Camera.GoPro
{
    /// <summary>(Swim 2.20.0) The texts of the GoPro module in the language of the swim analysis (Greek or English).</summary>
    internal static class GoProText
    {
        public static string T(string greek, string english)
        {
            return CameraUiLanguage.English ? english : greek;
        }

        /// <summary>A duration in minutes and hours: "1 ώ 23΄" / "1 h 23 min", "45΄" / "45 min".</summary>
        public static string Duration(long seconds)
        {
            if (seconds < 0)
                return "–";

            long minutes = seconds / 60;
            long hours = minutes / 60;
            minutes %= 60;
            if (hours > 0)
                return CameraUiLanguage.English ?
                    string.Format(CultureInfo.InvariantCulture, "{0} h {1:00} min", hours, minutes) :
                    string.Format(CultureInfo.InvariantCulture, "{0} ώ {1:00}΄", hours, minutes);

            return CameraUiLanguage.English ?
                string.Format(CultureInfo.InvariantCulture, "{0} min", minutes) :
                string.Format(CultureInfo.InvariantCulture, "{0}΄", minutes);
        }

        /// <summary>A recording time: 00:12:34.</summary>
        public static string Clock(long seconds)
        {
            if (seconds < 0)
                seconds = 0;

            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, (seconds / 60) % 60, seconds % 60);
        }

        /// <summary>Seconds with two decimals, with the decimal mark of the language: +0,38 s / +0.38 s.</summary>
        public static string Seconds(double seconds)
        {
            string s = seconds.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            if (!CameraUiLanguage.English)
                s = s.Replace('.', ',');

            return s + " s";
        }
    }

    /// <summary>
    /// (Swim 2.20.0) What the GoPro module keeps of a camera in the camera history of the preferences: its Bluetooth address, its model
    /// and its Bluetooth name.
    /// </summary>
    public class SpecificInfo
    {
        public ulong Address { get; set; }
        public int ModelId { get; set; } = -1;
        public string BleName { get; set; }

        public string ToXml()
        {
            StringBuilder b = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings { OmitXmlDeclaration = true };
            using (XmlWriter w = XmlWriter.Create(b, settings))
            {
                w.WriteStartElement("GoPro");
                w.WriteElementString("Address", Address.ToString("X12", CultureInfo.InvariantCulture));
                w.WriteElementString("Model", ModelId.ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("Name", BleName ?? "");
                w.WriteEndElement();
            }

            return b.ToString();
        }

        public static SpecificInfo FromXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                return null;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(new StringReader(xml));
                SpecificInfo info = new SpecificInfo();
                XmlNode address = doc.SelectSingleNode("/GoPro/Address");
                if (address == null)
                    return null;

                info.Address = ulong.Parse(address.InnerText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                XmlNode model = doc.SelectSingleNode("/GoPro/Model");
                int modelId;
                if (model != null && int.TryParse(model.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out modelId))
                    info.ModelId = modelId;

                XmlNode name = doc.SelectSingleNode("/GoPro/Name");
                if (name != null && !string.IsNullOrEmpty(name.InnerText))
                    info.BleName = name.InnerText;

                return info;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
