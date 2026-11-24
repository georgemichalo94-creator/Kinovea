#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.38.0) The PDF of the report of the 2nd version: the pages (HTML) that the Apps Script makes from the analysis are printed here,
    /// by the Microsoft Edge of the computer (or Google Chrome) without a window, as A4 pages with their colours and with the fonts of
    /// the report (Inter, in Kinovea: it is not a font of Windows), so the PDF is the same on every computer. Edge is asked through its
    /// DevTools (as the checks of the report print them: the fonts loaded first, A4, no margins, no header); its own "print to PDF" of
    /// the command line when that fails. Called on a background thread: it waits for the browser.
    /// </summary>
    public static class SwimPdfPrinter
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public class Result
        {
            public bool Ok;
            public byte[] Pdf;
            public string Error;

            /// <summary>The pages of the PDF (counted in it), 0 when unknown.</summary>
            public int Pages;
            public double Seconds;

            /// <summary>"devtools" or "command line".</summary>
            public string Method;
            public string Browser;
        }

        /// <summary>The faces of the report (the CSS of its pages asks for these): file, family, weight.</summary>
        private static readonly string[][] Faces =
        {
            new[] { "Inter-Regular", "Inter", "400" }, new[] { "Inter-Medium", "Inter", "500" }, new[] { "Inter-SemiBold", "Inter", "600" },
            new[] { "Inter-Bold", "Inter", "700" }, new[] { "Inter-ExtraBold", "Inter", "800" },
            new[] { "InterDisplay-Medium", "Inter Display", "500" }, new[] { "InterDisplay-SemiBold", "Inter Display", "600" },
            new[] { "InterDisplay-ExtraBold", "Inter Display", "800" }
        };

        /// <summary>A4 in inches (the pages of the report are 794 × 1123 px).</summary>
        private const double PaperWidth = 8.27, PaperHeight = 11.69;

        private static readonly object gate = new object();
        private static string fontsCss;
        private static string browserFound;
        private static bool browserLooked;

        /// <summary>
        /// The DevTools of the browser failed and its command line printed (DevTools blocked on this computer): the command line first
        /// from then on, until Kinovea starts again (not the wait for DevTools every time).
        /// </summary>
        private static bool commandLineFirst;

        /// <summary>The browser that prints (Edge, else Chrome), null when none is installed. Looked for once.</summary>
        public static string BrowserPath
        {
            get
            {
                lock (gate)
                {
                    if (!browserLooked)
                    {
                        browserLooked = true;
                        try
                        {
                            browserFound = FindBrowser();
                        }
                        catch (Exception e)
                        {
                            log.Error("Could not look for the browser that prints the PDF.", e);
                        }
                    }

                    return browserFound;
                }
            }
        }

        /// <summary>Microsoft Edge where Windows says (App Paths) or where it installs itself; else Google Chrome.</summary>
        private static string FindBrowser()
        {
            foreach (string exe in new[] { "msedge.exe", "chrome.exe" })
            {
                List<string> places = new List<string>();
                foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                {
                    foreach (string key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exe })
                    {
                        try
                        {
                            using (RegistryKey k = root.OpenSubKey(key))
                            {
                                string path = k != null ? k.GetValue(null) as string : null;
                                if (!string.IsNullOrWhiteSpace(path))
                                    places.Add(path.Trim().Trim('"'));
                            }
                        }
                        catch (Exception)
                        {
                            // Not readable: the usual folders below.
                        }
                    }
                }

                string vendor = exe == "msedge.exe" ? @"Microsoft\Edge\Application" : @"Google\Chrome\Application";
                foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.LocalApplicationData })
                {
                    string root = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(root))
                        places.Add(Path.Combine(root, vendor, exe));
                }

                string found = places.FirstOrDefault(p => { try { return File.Exists(p); } catch (Exception) { return false; } });
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// The pages printed as a PDF. method: null (DevTools, then the command line), "devtools" or "commandline" (the checks). Never
        /// throws: Ok false and the reason.
        /// </summary>
        public static Result Print(string html, string method = null, int timeoutSeconds = 90)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Result result = new Result();
            string browser = BrowserPath;
            result.Browser = browser != null ? Path.GetFileNameWithoutExtension(browser) : "";
            if (browser == null)
            {
                result.Error = SwimLang.T("δεν βρέθηκε ο Microsoft Edge ούτε το Google Chrome");
                return result;
            }

            if (string.IsNullOrEmpty(html))
            {
                result.Error = SwimLang.T("χωρίς σελίδες");
                return result;
            }

            string root = Path.Combine(Path.GetTempPath(), "KinoveaSwimPdf");
            string dir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                CleanOld(root);
                Directory.CreateDirectory(dir);
                string page = Path.Combine(dir, "report.html");
                File.WriteAllText(page, WithFonts(html), new UTF8Encoding(false));
                int timeout = Math.Max(30, timeoutSeconds) * 1000;
                List<string> errors = new List<string>();
                byte[] pdf = null;
                bool devTools = method == null || method == "devtools", commandLine = method == null || method == "commandline";
                bool lineFirst = method == null && commandLineFirst;
                for (int pass = 0; pass < 2 && pdf == null; pass++)
                {
                    bool useDevTools = (pass == 0) != lineFirst;
                    if (useDevTools && devTools)
                    {
                        try
                        {
                            pdf = PrintDevTools(browser, page, Path.Combine(dir, "profile"), timeout);
                            result.Method = "devtools";
                        }
                        catch (Exception e)
                        {
                            log.Warn("The PDF was not printed through DevTools: " + e.Message);
                            errors.Add(e.Message);
                        }
                    }
                    else if (!useDevTools && commandLine)
                    {
                        try
                        {
                            // What is left of the time (at least half a minute: Edge prints a report in a few seconds).
                            int left = (int)Math.Max(30000, timeout - watch.ElapsedMilliseconds);
                            pdf = PrintCommandLine(browser, page, Path.Combine(dir, "report.pdf"), Path.Combine(dir, "profile2"), left);
                            result.Method = "command line";
                            if (method == null && errors.Count > 0 && !commandLineFirst)
                            {
                                commandLineFirst = true;
                                log.Info("The PDF printed from the command line of the browser, not through its DevTools: the command line first from now on.");
                            }
                        }
                        catch (Exception e)
                        {
                            log.Warn("The PDF was not printed from the command line: " + e.Message);
                            errors.Add(e.Message);
                        }
                    }
                }

                if (pdf == null || !IsPdf(pdf))
                {
                    result.Error = errors.Count > 0 ? string.Join(" · ", errors) : SwimLang.T("δεν βγήκε PDF");
                    return result;
                }

                result.Pdf = pdf;
                result.Pages = CountPages(pdf);
                result.Ok = true;
                return result;
            }
            catch (Exception e)
            {
                log.Error("Could not print the PDF of the report.", e);
                result.Error = e.Message;
                return result;
            }
            finally
            {
                result.Seconds = watch.Elapsed.TotalSeconds;
                Remove(dir);
            }
        }

        /// <summary>The faces of the report put in its pages (data URIs, before its own styles): the PDF has them on any computer.</summary>
        public static string WithFonts(string html)
        {
            string css = FontsCss();
            if (css.Length == 0)
                return html;

            int at = html.IndexOf("<style", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                at = head >= 0 ? head : 0;
            }

            return html.Substring(0, at) + css + html.Substring(at);
        }

        private static string FontsCss()
        {
            lock (gate)
            {
                if (fontsCss != null)
                    return fontsCss;

                StringBuilder b = new StringBuilder("<style id=\"kinovea-swim-fonts\">");
                Assembly assembly = typeof(SwimPdfPrinter).Assembly;
                int found = 0;
                foreach (string[] face in Faces)
                {
                    using (Stream s = assembly.GetManifestResourceStream("SwimFonts." + face[0] + ".woff"))
                    {
                        if (s == null)
                        {
                            log.Error("A font of the report is missing in Kinovea: " + face[0]);
                            continue;
                        }

                        using (MemoryStream m = new MemoryStream())
                        {
                            s.CopyTo(m);
                            b.Append("@font-face{font-family:'").Append(face[1]).Append("';font-style:normal;font-weight:").Append(face[2])
                                .Append(";font-display:block;src:url(data:font/woff;base64,").Append(Convert.ToBase64String(m.ToArray())).Append(") format('woff');}\n");
                            found++;
                        }
                    }
                }

                b.Append("</style>");
                fontsCss = found > 0 ? b.ToString() : "";
                return fontsCss;
            }
        }

        /// <summary>The arguments of a browser without a window, with a profile of its own (never the profile of the analyst's Edge).</summary>
        private static string Arguments(string profile, params string[] more)
        {
            List<string> a = new List<string>
            {
                "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-sync",
                "--disable-background-networking", "--disable-component-update", "--disable-default-apps", "--mute-audio", "--hide-scrollbars",
                "--disable-features=Translate,OptimizationHints,MediaRouter", "--user-data-dir=" + Quote(profile)
            };
            a.AddRange(more);
            return string.Join(" ", a);
        }

        private static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        /// <summary>
        /// The page in the browser through its DevTools: loaded, its fonts ready, then Page.printToPDF on A4 without margins, header or
        /// footer, with the colours of the backgrounds (as the checks of the report print it).
        /// </summary>
        private static byte[] PrintDevTools(string browser, string page, string profile, int timeout)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Directory.CreateDirectory(profile);
            ProcessStartInfo info = new ProcessStartInfo(browser, Arguments(profile, "--remote-debugging-port=0", "about:blank"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (Process process = Process.Start(info))
            {
                try
                {
                    // The port the browser chose, in its profile (DevToolsActivePort: the port, the path of the browser).
                    string file = Path.Combine(profile, "DevToolsActivePort");
                    int port = 0;
                    string browserPath = null;
                    while (port == 0)
                    {
                        if (watch.ElapsedMilliseconds > Math.Min(20000, timeout))
                            throw new TimeoutException(SwimLang.T("ο browser δεν άνοιξε"));
                        if (process.HasExited && !File.Exists(file))
                            throw new InvalidOperationException(SwimLang.T("ο browser έκλεισε αμέσως"));
                        Thread.Sleep(100);
                        try
                        {
                            if (!File.Exists(file))
                                continue;
                            string[] lines = File.ReadAllLines(file);
                            if (lines.Length >= 2 && int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0)
                                browserPath = lines[1].Trim();
                            else
                                port = 0;
                        }
                        catch (IOException)
                        {
                            port = 0;
                        }
                    }

                    byte[] pdf;
                    using (DevTools tab = new DevTools(PageTarget(port), 15000))
                    {
                        JObject navigated = tab.Call("Page.navigate", new JObject { ["url"] = new Uri(page).AbsoluteUri }, 20000);
                        string failed = navigated.Value<string>("errorText");
                        if (!string.IsNullOrEmpty(failed))
                            throw new InvalidOperationException(SwimLang.T("η σελίδα δεν άνοιξε: ") + failed);

                        // The page loaded (its own, not the blank one before it), then its fonts.
                        Stopwatch load = Stopwatch.StartNew();
                        while (true)
                        {
                            try
                            {
                                JObject state = tab.Call("Runtime.evaluate", new JObject
                                {
                                    ["expression"] = "location.protocol === 'file:' && document.readyState === 'complete'",
                                    ["returnByValue"] = true
                                }, 10000);
                                JObject value = state["result"] as JObject;
                                if (value != null && value.Value<bool?>("value") == true)
                                    break;
                            }
                            catch (InvalidOperationException)
                            {
                                // The page of before going away: once more.
                            }

                            if (load.ElapsedMilliseconds > 30000)
                                throw new TimeoutException(SwimLang.T("η σελίδα δεν φόρτωσε"));
                            Thread.Sleep(100);
                        }

                        tab.Call("Runtime.evaluate", new JObject
                        {
                            ["expression"] = "document.fonts.ready.then(function () { return document.fonts.size; })",
                            ["awaitPromise"] = true,
                            ["returnByValue"] = true
                        }, 20000);

                        JObject printed = tab.Call("Page.printToPDF", new JObject
                        {
                            ["printBackground"] = true,
                            ["preferCSSPageSize"] = true,
                            ["paperWidth"] = PaperWidth,
                            ["paperHeight"] = PaperHeight,
                            ["marginTop"] = 0,
                            ["marginBottom"] = 0,
                            ["marginLeft"] = 0,
                            ["marginRight"] = 0,
                            ["displayHeaderFooter"] = false,
                            ["scale"] = 1,
                            ["transferMode"] = "ReturnAsBase64"
                        }, (int)Math.Max(20000, timeout - watch.ElapsedMilliseconds));
                        string data = printed.Value<string>("data");
                        if (string.IsNullOrEmpty(data))
                            throw new InvalidDataException(SwimLang.T("ο browser δεν έδωσε PDF"));
                        pdf = Convert.FromBase64String(data);
                    }

                    // The browser closes itself (and its processes); else it is stopped.
                    if (!string.IsNullOrEmpty(browserPath))
                    {
                        try
                        {
                            using (DevTools b = new DevTools("ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + browserPath, 5000))
                                b.Send("Browser.close");
                        }
                        catch (Exception)
                        {
                            // Stopped below.
                        }
                    }

                    process.WaitForExit(8000);
                    return pdf;
                }
                finally
                {
                    Stop(process);
                }
            }
        }

        /// <summary>The DevTools address of the tab of the browser (the one it opened, else a new one).</summary>
        private static string PageTarget(int port)
        {
            string url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < 50; i++)
            {
                JArray targets;
                try
                {
                    targets = JArray.Parse(Http(url + "/json/list", "GET"));
                }
                catch (WebException)
                {
                    // Not answering yet: once more.
                    Thread.Sleep(200);
                    continue;
                }

                JObject tab = targets.OfType<JObject>().FirstOrDefault(t => t.Value<string>("type") == "page" && !string.IsNullOrEmpty(t.Value<string>("webSocketDebuggerUrl")));
                if (tab != null)
                    return tab.Value<string>("webSocketDebuggerUrl");
                if (i == 5)
                {
                    try
                    {
                        JObject made = JObject.Parse(Http(url + "/json/new?about:blank", "PUT"));
                        string ws = made.Value<string>("webSocketDebuggerUrl");
                        if (!string.IsNullOrEmpty(ws))
                            return ws;
                    }
                    catch (Exception)
                    {
                        // The list again.
                    }
                }

                Thread.Sleep(200);
            }

            throw new InvalidOperationException(SwimLang.T("ο browser δεν έχει σελίδα"));
        }

        private static string Http(string url, string method)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Proxy = null;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            using (WebResponse response = request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>The "print to PDF" of the browser's command line (the page printed when it has loaded).</summary>
        private static byte[] PrintCommandLine(string browser, string page, string output, string profile, int timeout)
        {
            Directory.CreateDirectory(profile);
            ProcessStartInfo info = new ProcessStartInfo(browser, Arguments(profile, "--no-pdf-header-footer", "--print-to-pdf-no-header",
                "--print-to-pdf=" + Quote(output), Quote(new Uri(page).AbsoluteUri)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (Process process = Process.Start(info))
            {
                try
                {
                    if (!process.WaitForExit(timeout))
                        throw new TimeoutException(SwimLang.T("ο browser δεν τελείωσε"));
                }
                finally
                {
                    Stop(process);
                }
            }

            // The file may take a moment after the browser ends.
            for (int i = 0; i < 20 && !File.Exists(output); i++)
                Thread.Sleep(100);
            if (!File.Exists(output))
                throw new FileNotFoundException(SwimLang.T("ο browser δεν έγραψε PDF"));
            return File.ReadAllBytes(output);
        }

        private static void Stop(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                // Ended in the meantime.
            }
        }

        public static bool IsPdf(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 1000 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';
        }

        /// <summary>The pages of a PDF of Chromium (its page objects, "/Type /Page").</summary>
        public static int CountPages(byte[] pdf)
        {
            if (pdf == null)
                return 0;
            string text = Encoding.GetEncoding(28591).GetString(pdf);
            return Regex.Matches(text, @"/Type\s*/Page(?![A-Za-z])").Count;
        }

        /// <summary>The folder of a print, and the ones of earlier prints left over a day ago (a browser still holding a file then).</summary>
        private static void Remove(string dir)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(300);
                }
            }
        }

        private static void CleanOld(string root)
        {
            try
            {
                if (!Directory.Exists(root))
                    return;
                foreach (string d in Directory.GetDirectories(root))
                {
                    try
                    {
                        if ((DateTime.Now - Directory.GetCreationTime(d)).TotalHours > 24)
                            Directory.Delete(d, true);
                    }
                    catch (Exception)
                    {
                        // Next time.
                    }
                }
            }
            catch (Exception)
            {
                // Nothing to clean.
            }
        }

        /// <summary>A connection to a target of the DevTools of the browser: calls and their answers.</summary>
        private sealed class DevTools : IDisposable
        {
            private readonly ClientWebSocket socket = new ClientWebSocket();
            private readonly byte[] buffer = new byte[1 << 16];
            private int next = 1;

            public DevTools(string url, int timeout)
            {
                socket.Options.Proxy = null;
                using (CancellationTokenSource cancel = new CancellationTokenSource(timeout))
                    socket.ConnectAsync(new Uri(url), cancel.Token).GetAwaiter().GetResult();
            }

            /// <summary>A call without waiting for its answer (Browser.close: the browser goes).</summary>
            public void Send(string method)
            {
                JObject message = new JObject { ["id"] = next++, ["method"] = method };
                byte[] data = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
                using (CancellationTokenSource cancel = new CancellationTokenSource(5000))
                    socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancel.Token).GetAwaiter().GetResult();
            }

            public JObject Call(string method, JObject parameters, int timeout)
            {
                int id = next++;
                JObject message = new JObject { ["id"] = id, ["method"] = method };
                if (parameters != null)
                    message["params"] = parameters;
                byte[] data = Encoding.UTF8.GetBytes(message.ToString(Newtonsoft.Json.Formatting.None));
                using (CancellationTokenSource cancel = new CancellationTokenSource(timeout))
                {
                    try
                    {
                        socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancel.Token).GetAwaiter().GetResult();
                        while (true)
                        {
                            // The events of the page (none is asked for) are passed over: the answer of this call.
                            JObject answer = Receive(cancel.Token);
                            if (answer.Value<int?>("id") == id)
                            {
                                JObject error = answer["error"] as JObject;
                                if (error != null)
                                    throw new InvalidOperationException(method + ": " + error.Value<string>("message"));
                                return answer["result"] as JObject ?? new JObject();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException(method + ": " + SwimLang.T("χωρίς απάντηση"));
                    }
                }
            }

            private JObject Receive(CancellationToken token)
            {
                using (MemoryStream m = new MemoryStream())
                {
                    while (true)
                    {
                        WebSocketReceiveResult r = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                        if (r.MessageType == WebSocketMessageType.Close)
                            throw new IOException(SwimLang.T("ο browser έκλεισε τη σύνδεση"));
                        m.Write(buffer, 0, r.Count);
                        if (r.EndOfMessage)
                            break;
                    }

                    return JObject.Parse(Encoding.UTF8.GetString(m.ToArray()));
                }
            }

            public void Dispose()
            {
                try
                {
                    socket.Abort();
                }
                catch (Exception)
                {
                    // Closed.
                }

                socket.Dispose();
            }
        }
    }
}
