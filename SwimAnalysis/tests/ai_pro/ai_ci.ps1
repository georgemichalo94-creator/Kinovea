# ai_ci.ps1: the CI check of AI pro (2.19.0) with FICTIONAL data only: the password gate (with a test hash: the owner's password is in
# no file), the protected key, the prices and the book of the month, the moments of a start and a turn, the request (no name of a person
# or a club or a competition, every point of the list, the frames), a fake Claude API on this computer (the headers, a busy API asked
# again, the errors), Claude's answers into the list (proposals AI, the coach's own scores and the program's measurements kept), the
# notes of the report, the agreement and the calibration. Pictures of the windows to -out (an artifact of the run).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7 (without the Windows parts). Exit code 1 when a check fails.
#   powershell -File ai_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -catalog <mini_single.json> -out <folder>
param([string]$sm, [string]$bin, [string]$catalog, [string]$out)
$ErrorActionPreference = 'Stop'
$windows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
if ($windows) {
  Add-Type -AssemblyName System.Windows.Forms
  Add-Type -AssemblyName System.Drawing
  Add-Type -AssemblyName System.Security
}
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path; $catalog = (Resolve-Path $catalog).Path
if ($out) { New-Item -ItemType Directory -Force -Path $out | Out-Null; $out = (Resolve-Path $out).Path }
# The other DLLs of the program from its folder (plain string work only in the handler, see te_ci.ps1).
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
$smAssembly = [System.Reflection.Assembly]::LoadFrom($sm)
function T($n) { return $smAssembly.GetType('Kinovea.ScreenManager.' + $n, $true) }
$all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
# (The comma: an empty list returned from a function would come back as nothing.)
function NewList($type) { return ,([Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($type))) }

# A settings folder of its own (the catalogue of the test as the coach's), before anything reads the settings.
$settingsDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-ai-ci-' + [Guid]::NewGuid().ToString('N')) + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
[System.IO.File]::Copy($catalog, [System.IO.Path]::Combine($settingsDir, 'SwimTechSheets.json'), $true)
$software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
$software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)

# ---- The password gate (a test hash; the owner's own is in the program only as a salted hash).
$gate = T 'SwimAiGate'
$verify = $gate.GetMethod('Verify')
$salt = '00112233445566778899aabbccddeeff'; $hash = '5414e0ceec8cb9dd9630b980e5a71f4e34ab40ead2faf04b1009389b5ef12f98'
Check ($verify.Invoke($null, @('test-password', $salt, $hash, 1000))) 'the test password opens'
Check ($verify.Invoke($null, @('  test-password ', $salt, $hash, 1000))) 'spaces at its ends do not count'
Check (-not $verify.Invoke($null, @('test-Password', $salt, $hash, 1000))) 'a capital letter changed: closed'
Check (-not $verify.Invoke($null, @('', $salt, $hash, 1000))) 'nothing typed: closed'
Check (-not $verify.Invoke($null, @('test-password', 'ff112233445566778899aabbccddeeff', $hash, 1000))) 'another salt: closed'
Check (-not $verify.Invoke($null, @('test-password', 'zz', $hash, 1000))) 'a salt that is not hex: closed'
$unlock = $gate.GetMethod('Unlock')
for ($i = 0; $i -lt 5; $i++) { [void]$unlock.Invoke($null, @('not-the-password-' + $i)) }
Check ((-not $gate.GetProperty('Unlocked').GetValue($null)) -and ($gate.GetProperty('WaitSeconds').GetValue($null) -gt 0)) 'five wrong passwords: closed, and a wait before the next try'

# ---- The key of the API.
$keyType = T 'SwimAiKey'
$fakeKey = 'sk-ant-api03-' + ('x' * 40) + 'ab12'
Check ($keyType.GetMethod('LooksValid').Invoke($null, @($fakeKey))) 'a key as the Console gives it looks valid'
Check (-not $keyType.GetMethod('LooksValid').Invoke($null, @('sk-ant-short'))) 'a short key does not'
Check (-not $keyType.GetMethod('LooksValid').Invoke($null, @('sk-ant-api03-' + ('x' * 20) + ' ' + ('y' * 20)))) 'a key with a space does not'
Check (($keyType.GetMethod('Masked').Invoke($null, @($fakeKey))) -eq ('sk-ant-' + [char]0x2026 + 'ab12')) 'the key shown: its start and its last 4 characters'
if ($windows) {
  $protected = $keyType.GetMethod('Protect').Invoke($null, @($fakeKey))
  Check ($protected.Length -gt 0 -and -not $protected.Contains('sk-ant')) 'the key kept protected (DPAPI), not as text'
  Check (($keyType.GetMethod('Unprotect').Invoke($null, @($protected))) -eq $fakeKey) 'and read back by the same Windows user'
  Check ($null -eq ($keyType.GetMethod('Unprotect').Invoke($null, @('bm90IGEga2V5')))) 'something else is not a key'
}

# ---- Prices, tokens, the book of the month.
$models = T 'SwimAiModels'
Check ([Math]::Abs($models.GetMethod('Usd').Invoke($null, @('claude-opus-5-5', [long]1000000, [long]0)) - 4) -lt 1e-9) 'Opus 5.5: $4 a million input tokens'
Check ([Math]::Abs($models.GetMethod('Usd').Invoke($null, @('claude-sonnet-5-5', [long]0, [long]1000000)) - 10) -lt 1e-9) 'Sonnet 5.5: $10 a million output tokens'
Check (($models.GetMethod('ImageTokens').Invoke($null, @(1280, 720))) -eq 1196) 'a frame of 1280 x 720: 1196 tokens'
Check (($models.GetMethod('ImageTokens').Invoke($null, @(4000, 3000))) -eq 4784) 'a frame is at most 4784 tokens'
$book = T 'SwimAiBook'; $tReply = T 'SwimAiReply'; $tUsage = T 'SwimAiUsage'
$dict = [Activator]::CreateInstance([System.Collections.Generic.Dictionary``2].MakeGenericType([string], $tUsage))
$r1 = [Activator]::CreateInstance($tReply); $r1.InputTokens = 30000; $r1.OutputTokens = 3000; $r1.Usd = 0.18
$day = [DateTime]::new(2026, 11, 6)
[void]$book.GetMethod('Add').Invoke($null, @($dict, $day, $r1)); [void]$book.GetMethod('Add').Invoke($null, @($dict, $day, $r1))
$month = $book.GetMethod('Month').Invoke($null, @($dict, $day))
Check ($month.Requests -eq 2 -and [Math]::Abs($month.Usd - 0.36) -lt 1e-9 -and $month.Input -eq 60000) 'the book of the month adds the requests and their cost'
[void]$book.GetMethod('Add').Invoke($null, @($dict, $day.AddMonths(13), $r1))
Check ((-not $dict.ContainsKey('2026-11')) -and $dict.ContainsKey('2027-12')) 'months older than a year go'

# ---- A fictional training video at 50 fps (a timestamp = 1 ms): a start, and a flip turn as te_ci.ps1.
$tCat = T 'SwimTechSheetCatalog'; $tEv = T 'SwimTechEvaluator'; $tRace = T 'SwimRace'; $tMark = T 'SwimMark'; $tType = T 'SwimMarkType'
$tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'
$one = $tCat.GetMethod('Parse').Invoke($null, @([System.IO.File]::ReadAllText($catalog)))
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
$race = [Activator]::CreateInstance($tRace)
$race.Kind = 'training'; $race.Style = 'fs'; $race.Pool = 50
# Everything that could name someone: none of it may reach a request.
$race.Athlete = 'Δοκιμαστική Αθλήτρια'; $race.AthleteId = 'TEST-ID-123'; $race.AthleteClub = 'Όμιλος Δοκιμής'; $race.AthleteBirthYear = '2010'
$race.AthleteCategory = 'Νεάνιδες'; $race.AthleteGender = 'Γυναικών'; $race.Competition = 'Αγώνες Δοκιμής'; $race.Location = 'Πόλη Δοκιμής'
function Mark($type, $ms, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long]$ms; $m.Wall = -1
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
Mark 'Signal' 1000 $null; Mark 'Reaction' 1600 $null; Mark 'HandsOff' 1640 $null; Mark 'FeetOff' 1760 $null; Mark 'Entry' 2100 $null
foreach ($k in @(2800, 3240, 3680)) { Mark 'Kick' $k $null }
Mark 'Breakout' 4900 $null; Mark 'TechSplit' 3300 5; Mark 'TechSplit' 5600 10
foreach ($t in @(-4.0, -3.4, -2.8, -2.2, -1.6, -1.0)) { Mark 'Stroke' ([Math]::Round((30 + $t) * 1000)) $null }
Mark 'TechSplit' 27400 -5; Mark 'RotationStart' 29400 $null; Mark 'WallContact' 30000 $null; Mark 'FeetOff' 30300 $null
foreach ($k in @(31000, 31460, 31900, 32360)) { Mark 'Kick' $k $null }
Mark 'Breakout' 33500 $null; Mark 'TechSplit' 35000 10
$computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
$phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
$start = @($phases) | Where-Object { $_.Kind.ToString() -eq 'Start' } | Select-Object -First 1
$turn = @($phases) | Where-Object { $_.Kind.ToString() -eq 'Turn' } | Select-Object -First 1
# (The objects themselves, not their wrappers of the pipeline: reflection takes them as they are.)
if ($start) { $start = $start.psobject.BaseObject }
if ($turn) { $turn = $turn.psobject.BaseObject }
Write-Output ('phases: ' + (@($phases) | ForEach-Object { $_.Id + ' ' + $_.Kind }) -join ', ')
Check ($null -ne $start -and $null -ne $turn) 'a start and a turn in the fictional video'
$turnSheet = $one.Sheet('kv-turn-fs'); $startSheet = $one.Sheet('kv-start-fs')

# ---- The moments Claude sees.
$momentsType = T 'SwimAiMoments'
$moments = $momentsType.GetMethod('Tech').Invoke($null, @($turn, $turnSheet, $timing, $one, [long]0, [long]60000, 16, 'el'))
$anchors = @($turnSheet.Items | ForEach-Object { $_.Criterion.Anchor } | Where-Object { $_ } | Select-Object -Unique)
$timed = @($anchors | Where-Object { $null -ne $tEv.GetMethod('AnchorTimestamp').Invoke($null, @($turn, [string]$_, $timing)) })
$covered = @($timed | Where-Object { $a = [string]$_; @($moments | Where-Object { $_.Anchors.Contains($a) }).Count -gt 0 })
Write-Output ('turn moments: ' + (@($moments) | ForEach-Object { '' + $_.N + ' ' + $_.Label + ' (' + $_.Seconds.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + ')' }) -join ' | ')
Check (@($moments).Count -ge 3 -and @($moments).Count -le 16) ('the turn: ' + @($moments).Count + ' moments')
Check ($covered.Count -eq $timed.Count) ('every moment of a point of the list is among them (' + $covered.Count + '/' + $timed.Count + ')')
$ordered = $true; for ($i = 0; $i -lt @($moments).Count; $i++) { if ($moments[$i].N -ne $i + 1 -or ($i -gt 0 -and $moments[$i].Timestamp -le $moments[$i - 1].Timestamp)) { $ordered = $false } }
Check $ordered 'numbered in their order in the video'
$rotation = @($moments | Where-Object { $_.Anchors.Contains('rotation') })
$push = @($moments | Where-Object { $_.Anchors.Contains('pushoff') })
Check ($rotation.Count -eq 1 -and [Math]::Abs($rotation[0].Seconds + 0.6) -lt 0.011 -and $push.Count -eq 1 -and [Math]::Abs($push[0].Seconds - 0.3) -lt 0.011) 'the times from the wall contact (rotation −0.60 s, push-off +0.30 s)'
$far = @($moments | Where-Object { $_.Seconds -lt -4 -or $_.Seconds -gt 5 })
Check ($far.Count -eq 0) 'the frames between the marks stay near the marks (no frame far from the turn)'
$few = $momentsType.GetMethod('Tech').Invoke($null, @($turn, $turnSheet, $timing, $one, [long]0, [long]60000, 3, 'el'))
$fewCovered = @($timed | Where-Object { $a = [string]$_; @($few | Where-Object { $_.Anchors.Contains($a) }).Count -gt 0 })
Check (@($few).Count -le 3 -and $fewCovered.Count -eq $timed.Count) 'at most 3 moments: the nearest join, no point lost'
$startMoments = $momentsType.GetMethod('Tech').Invoke($null, @($start, $startSheet, $timing, $one, [long]0, [long]60000, 16, 'el'))
Write-Output ('start moments: ' + (@($startMoments) | ForEach-Object { '' + $_.N + ' ' + $_.Label + ' (' + $_.Seconds.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) + ')' }) -join ' | ')
Check (@($startMoments).Count -ge 3 -and @($startMoments | Where-Object { $_.Seconds -lt -2 -or $_.Seconds -gt 6 }).Count -eq 0) ('the start: ' + @($startMoments).Count + ' moments, all near its marks')

# ---- The request.
$tAth = T 'SwimAiAthlete'; $tImg = T 'SwimAiImage'; $prompt = T 'SwimAiPrompt'; $request = T 'SwimAiRequest'
$athlete = [Activator]::CreateInstance($tAth); $athlete.Gender = 'F'; $athlete.Category = 'Νεάνιδες'; $athlete.Age = '16'; $athlete.Height = '172'
$athlete.Best = '1:02.10'; $athlete.Level = 'πανελλήνιο'; $athlete.Focus = 'η θέση του κεφαλιού'
if ($windows) {
  $bmp = New-Object System.Drawing.Bitmap 64, 36
  # (Types of System.Drawing by name: PowerShell 7 on another system would stop at their names while it reads the script.)
  $formats = 'System.Drawing.Imaging.ImageFormat' -as [type]
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, $formats::Jpeg); $jpeg = $ms.ToArray(); $bmp.Dispose()
} else {
  $jpeg = [byte[]](1..200 | ForEach-Object { [byte]($_ % 256) })
}
$images = NewList $tImg
foreach ($m in $moments) { foreach ($screen in @(1, 2)) { $img = [Activator]::CreateInstance($tImg); $img.Moment = $m.N; $img.Screen = $screen; $img.Width = 1280; $img.Height = 720; $img.Jpeg = $jpeg; $images.Add($img) } }
$measures = $tEv.GetMethod('Measure').Invoke($null, @($race, $turn, $timing, $one))
$context = $prompt.GetMethod('TechContext').Invoke($null, @($race, $turn, $turnSheet, $one, $measures, $athlete, $moments, 2, $images.Count, 'el', $null))
$body = $request.GetMethod('Build').Invoke($null, @('claude-opus-5-5', $prompt.GetMethod('TechSystem').Invoke($null, @('el')), $context, $moments, $images, 2, $prompt.GetMethod('TechSchema').Invoke($null, @()), 16000))
$texts = New-Object System.Text.StringBuilder
[void]$texts.Append($body['system'].ToString())
$imageBlocks = 0; $firstMedia = ''
foreach ($c in $body['messages'][0]['content']) {
  if ($c['type'].ToString() -eq 'text') { [void]$texts.Append("`n" + $c['text'].ToString()) }
  if ($c['type'].ToString() -eq 'image') { $imageBlocks++; if (-not $firstMedia) { $firstMedia = $c['source']['media_type'].ToString() } }
}
$all_text = $texts.ToString()
if ($out) { [System.IO.File]::WriteAllText([System.IO.Path]::Combine($out, 'request_text_fictional.txt'), $all_text, (New-Object System.Text.UTF8Encoding $true)) }
$named = @('Δοκιμαστική Αθλήτρια', 'TEST-ID-123', 'Όμιλος Δοκιμής', '2010', 'Αγώνες Δοκιμής', 'Πόλη Δοκιμής') | Where-Object { $all_text.Contains($_) }
Check ($named.Count -eq 0) ('no name, id, club, birth year, competition or place in the request (' + ($named -join ', ') + ')')
$keys = @($turnSheet.Items | ForEach-Object { $_.Key }); $missingKeys = @($keys | Where-Object { -not $context.Contains('key ' + $_ + ':') })
Check ($missingKeys.Count -eq 0) ('every point of the list in the request (' + $keys.Count + ')')
Check ($imageBlocks -eq $images.Count -and $firstMedia -eq 'image/jpeg') ('every frame in the request (' + $imageBlocks + ', ' + $firstMedia + ')')
Check ($all_text.Contains('Moment 1 · screen 1') -and $all_text.Contains('Moment 1 · screen 2')) 'each frame named by its moment and its screen'
Check ($context.Contains('female') -and $context.Contains('Νεάνιδες') -and $context.Contains('172 cm') -and $context.Contains('η θέση του κεφαλιού')) 'the athlete as the coach gave it (no name)'
Check ($context.Contains('flip turn') -and $context.Contains('freestyle')) 'the event and the kind of turn'
# (Claude Opus 5.5 and Sonnet 5.5 answer 400 to a forced tool: the answer is JSON of a schema, structured outputs.)
$format = $body['output_config']['format']
Check ($format['type'].ToString() -eq 'json_schema' -and $null -eq $body['tool_choice'] -and $null -eq $body['tools']) 'the answer as JSON of a schema (structured outputs), no forced tool'
$closed = @($format['schema'].SelectTokens('$..additionalProperties') | Where-Object { $_.ToString() -eq 'False' }).Count
Check ($closed -eq 3 -and @($format['schema']['required'] | ForEach-Object { $_.ToString() }) -contains 'points') ('the schema: its objects closed (' + $closed + '), the points required')
Check ($body['model'].ToString() -eq 'claude-opus-5-5' -and [int]$body['max_tokens'] -eq 16000) 'the model and the length of the answer'
Check ($all_text.Contains('Write every text in Greek')) 'the answer in Greek for a Greek analysis'

# ---- A fake Claude API on this computer: the headers, a busy API asked again, the errors.
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
public class KvFakeClaudeApi {
  private HttpListener listener;
  private Thread thread;
  public string Prefix;
  public List<string> Bodies = new List<string>();
  public List<string> Keys = new List<string>();
  public List<string> Versions = new List<string>();
  public Queue<int> Codes = new Queue<int>();
  public Queue<string> Replies = new Queue<string>();
  public Queue<string> RetryAfter = new Queue<string>();
  public void Start(int port) {
    listener = new HttpListener();
    Prefix = "http://127.0.0.1:" + port + "/";
    listener.Prefixes.Add(Prefix);
    listener.Start();
    thread = new Thread(Run);
    thread.IsBackground = true;
    thread.Start();
  }
  private void Run() {
    while (true) {
      HttpListenerContext c;
      try { c = listener.GetContext(); } catch (Exception) { return; }
      string body;
      using (StreamReader r = new StreamReader(c.Request.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
      int code; string reply; string after;
      lock (this) {
        Bodies.Add(body); Keys.Add(c.Request.Headers["x-api-key"]); Versions.Add(c.Request.Headers["anthropic-version"]);
        code = Codes.Count > 0 ? Codes.Dequeue() : 200;
        reply = Replies.Count > 0 ? Replies.Dequeue() : "{}";
        after = RetryAfter.Count > 0 ? RetryAfter.Dequeue() : "";
      }
      c.Response.StatusCode = code;
      if (after.Length > 0) c.Response.Headers["retry-after"] = after;
      byte[] b = Encoding.UTF8.GetBytes(reply);
      c.Response.ContentType = "application/json";
      c.Response.ContentLength64 = b.Length;
      c.Response.OutputStream.Write(b, 0, b.Length);
      c.Response.Close();
    }
  }
  public void Stop() { try { listener.Stop(); listener.Close(); } catch (Exception) { } }
}
"@
$evaluation = @'
{
  "observations":[{"moment":1,"seen":"Κοιτάζει τον τοίχο στην προσέγγιση."},{"moment":2,"seen":"Αρχίζει την περιστροφή."}],
  "points":[
   {"key":"t.wall","done":true,"sure":0.9,"moment":1,"why":"Κοιτάζει τον τοίχο πριν την τελευταία χεριά."},
   {"key":"t.rotation","done":false,"sure":0.8,"moment":2,"why":"Η περιστροφή αργεί."},
   {"key":"push.short","done":false,"sure":0.85,"why":"Μακριά επαφή με τον τοίχο."},
   {"key":"t.streamline","done":null,"sure":0.5,"why":"Δεν φαίνεται στα καρέ."},
   {"key":"u.kicks","done":true,"sure":0.4,"why":"Ίσως."},
   {"key":"ok.rotation_quick","done":false,"sure":0.7,"moment":2,"why":"Ανοιχτό σώμα στην περιστροφή."},
   {"key":"x.unknown","done":true,"sure":1,"why":"Όχι σημείο της λίστας."}],
  "summary":"Καλή προσέγγιση· η περιστροφή και η επαφή χάνουν χρόνο.",
  "priorities":["Πιο σφιχτή περιστροφή: πηγούνι στο στήθος.","Πιο σύντομη επαφή στον τοίχο."],
  "doubts":["Το σημάδι της αποκόλλησης ίσως ένα καρέ νωρίς."]}
'@
$replyObject = [Newtonsoft.Json.Linq.JObject]::Parse('{"id":"msg_test","type":"message","role":"assistant","model":"claude-opus-5-5","stop_reason":"end_turn","usage":{"input_tokens":31240,"output_tokens":2880},"content":[{"type":"thinking","thinking":"..."},{"type":"text","text":""}]}')
# (A string set into JSON by PowerShell would be parsed into an object: the text as a JSON string value.)
$replyObject['content'][1]['text'] = [Newtonsoft.Json.Linq.JValue]::CreateString($evaluation)
$reply = $replyObject.ToString([Newtonsoft.Json.Formatting]::None)
$toolObject = [Newtonsoft.Json.Linq.JObject]::Parse('{"type":"message","model":"claude-opus-5-5","stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":5},"content":[{"type":"tool_use","name":"swim_technique_evaluation","input":{}}]}')
$toolObject['content'][0]['input'] = [Newtonsoft.Json.Linq.JObject]::Parse($evaluation)
$toolReply = $toolObject.ToString([Newtonsoft.Json.Formatting]::None)
$tCall = T 'SwimAiCall'
$api = New-Object KvFakeClaudeApi
$port = 18000 + (Get-Random -Minimum 0 -Maximum 4000)
$api.Start($port)
$tCall.GetField('Endpoint', $all).SetValue($null, $api.Prefix + 'v1/messages')
$json = $body.ToString([Newtonsoft.Json.Formatting]::None)
$call = [Activator]::CreateInstance($tCall)
$api.Codes.Enqueue(200); $api.Replies.Enqueue($reply)
$answer = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ($answer.Ok -and $answer.Tool -eq 'json' -and $answer.InputTokens -eq 31240 -and $answer.OutputTokens -eq 2880 -and $null -ne $answer.Input['points']) 'the answer read: the JSON of the evaluation (after the thinking) and its tokens'
Check ([Math]::Abs($answer.Usd - (31240 * 4 + 2880 * 20) / 1e6) -lt 1e-9) ('its cost: ' + $answer.Usd)
Check ($api.Keys[0] -eq $fakeKey -and $api.Versions[0] -eq '2023-06-01') 'the key and the version of the API in the headers'
$sent = [Newtonsoft.Json.Linq.JObject]::Parse($api.Bodies[0])
Check ($sent['model'].ToString() -eq 'claude-opus-5-5' -and @($sent['messages'][0]['content']).Count -eq @($body['messages'][0]['content']).Count) 'the body arrived whole'
$api.Codes.Enqueue(200); $api.Replies.Enqueue($toolReply)
$byTool = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ($byTool.Ok -and $byTool.Tool -eq 'swim_technique_evaluation' -and @($byTool.Input['points']).Count -eq 7) 'an answer as a tool is read too'
$fenced = $tCall.GetMethod('JsonOf', $all).Invoke($null, @("Here it is:`n" + '```json' + "`n" + '{"summary":"ok","points":[]}' + "`n" + '```'))
Check ($null -ne $fenced -and $fenced['summary'].ToString() -eq 'ok') 'JSON in the fences of a code block is read'
$api.Codes.Enqueue(429); $api.Replies.Enqueue('{"type":"error","error":{"type":"rate_limit_error","message":"Too many"}}'); $api.RetryAfter.Enqueue('1')
$api.Codes.Enqueue(200); $api.Replies.Enqueue($reply); $api.RetryAfter.Enqueue('')
$before = $api.Bodies.Count
$again = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ($again.Ok -and $api.Bodies.Count - $before -eq 2) 'a busy API (429) is asked again after the time it says'
$api.Codes.Enqueue(401); $api.Replies.Enqueue('{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}')
$bad = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ((-not $bad.Ok) -and $bad.Status -eq 401 -and $bad.Error.Contains('401')) ('a wrong key: ' + $bad.Error)
$api.Codes.Enqueue(400); $api.Replies.Enqueue('{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}')
$poor = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ((-not $poor.Ok) -and $poor.Error.Contains('credits')) ('no credits: ' + $poor.Error)
$api.Codes.Enqueue(429); $api.Replies.Enqueue('{"type":"error","error":{"type":"rate_limit_error","message":"limit","details":{"error_code":"enforced_spend_limit_reached"}}}')
$before = $api.Bodies.Count
$limit = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ((-not $limit.Ok) -and $api.Bodies.Count - $before -eq 1) ('the monthly limit is not asked again: ' + $limit.Error)
$api.Codes.Enqueue(200); $api.Replies.Enqueue('{"type":"message","model":"claude-opus-5-5","stop_reason":"max_tokens","usage":{"input_tokens":10,"output_tokens":16000},"content":[{"type":"text","text":"{\"observations\":[{\"moment\":1,\"seen\":\"cut"}]}')
$cut = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ((-not $cut.Ok) -and $cut.OutputTokens -eq 16000) ('an answer cut by its length is not taken (its tokens counted): ' + $cut.Error)
$api.Codes.Enqueue(400); $api.Replies.Enqueue('{"type":"error","error":{"type":"invalid_request_error","message":"tool_choice: type \"tool\" and \"any\" are not supported for this model."}}')
$shape = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check ((-not $shape.Ok) -and $shape.Status -eq 400 -and $shape.Error.Contains('400')) ('a request the API does not take: its message shown (' + $shape.Error + ')')
$api.Stop()
Start-Sleep -Milliseconds 300
$off = $call.Send($fakeKey, $json, 'claude-opus-5-5')
Check (-not $off.Ok) ('no API there: ' + $off.Error)

# ---- Claude's answers into the list.
$apply = T 'SwimAiApply'; $tEval = T 'SwimTechEval'; $tScore = T 'SwimTechScore'
$eval = [Activator]::CreateInstance($tEval); $eval.PhaseId = $turn.Id; $eval.SheetId = $turnSheet.Id; $eval.Notes = 'Σημείωση του προπονητή.'
$own = [Activator]::CreateInstance($tScore); $own.Value = 1; $eval.Scores.Add('push.short', $own)
$measured = [Activator]::CreateInstance($tScore); $measured.Value = 0; $measured.Auto = $true; $measured.Measured = 0.9; $eval.Scores.Add('t.rotation', $measured)
$race.TechEvals.Add($eval)
$parsed = $apply.GetMethod('ParseTech').Invoke($null, @($answer.Input, $turnSheet, @($moments).Count))
Check ($parsed.Points.Count -eq 6 -and $parsed.Priorities.Count -eq 2 -and $parsed.Doubts.Count -eq 1 -and $parsed.Observations.Count -eq 2) 'the answer read: 6 points of the list (not the unknown one)'
$applied = $apply.GetMethod('Tech').Invoke($null, @($race, $turn, $turnSheet, $parsed, 'claude-opus-5-5', $day))
Write-Output ('applied: answered ' + $applied.Answered + ', proposed ' + $applied.Proposed + ', coach ' + $applied.Coach + ', measured ' + $applied.Measured + ', unsure ' + $applied.Unsure + ', not seen ' + $applied.NotSeen + ', errors ' + $applied.Errors)
Check ($applied.Proposed -eq 2 -and $applied.Coach -eq 1 -and $applied.Measured -eq 1 -and $applied.Unsure -eq 1 -and $applied.NotSeen -eq 1 -and $applied.Errors -eq 3) 'what went where'
$sc = $eval.Scores
Check ($sc['t.wall'].Value -eq 1 -and $sc['t.wall'].Auto -and $sc['t.wall'].ByAi) 'a point nobody scored: a proposal AI (✓)'
Check ($sc['ok.rotation_quick'].Value -eq 0 -and $sc['ok.rotation_quick'].ByAi) 'an error Claude saw: a proposal AI (✗)'
Check ($sc['push.short'].Value -eq 1 -and -not $sc['push.short'].Auto -and $eval.Ai['push.short'].Value -eq 0) 'the coach''s own score stays, Claude''s answer beside it'
Check ($sc['t.rotation'].Value -eq 0 -and $sc['t.rotation'].Auto -and -not $sc['t.rotation'].ByAi) 'the program''s measurement stays'
Check ((-not $sc.ContainsKey('t.streamline')) -and (-not $sc.ContainsKey('u.kicks')) -and $eval.Ai['t.streamline'].Value -eq -1) 'not visible and not sure enough: not in the list'
Check ($eval.Notes.StartsWith('Σημείωση του προπονητή.') -and $eval.Notes.Contains('AI pro (Claude Opus 5.5 · 6/11/2026') -and $eval.Notes.Contains('Προτεραιότητες: 1) ')) 'the notes: the coach''s first, then the block of AI pro'
$parsed2 = $apply.GetMethod('ParseTech').Invoke($null, @([Newtonsoft.Json.Linq.JObject]::Parse('{"points":[{"key":"t.wall","done":null,"sure":0.5,"why":"Δεν φαίνεται."}],"summary":"Δεύτερη απάντηση.","priorities":[]}'), $turnSheet, 3))
[void]$apply.GetMethod('Tech').Invoke($null, @($race, $turn, $turnSheet, $parsed2, 'claude-opus-5-5', $day))
$blocks = ([regex]::Matches($eval.Notes, [regex]::Escape('AI pro ('))).Count
Check ((-not $sc.ContainsKey('t.wall')) -and $blocks -eq 1 -and $eval.Notes.Contains('Δεύτερη απάντηση.') -and $eval.Notes.StartsWith('Σημείωση του προπονητή.')) 'a new answer: its proposal of before goes, one block of AI pro, the coach''s notes stay'
[void]$apply.GetMethod('Tech').Invoke($null, @($race, $turn, $turnSheet, $parsed, 'claude-opus-5-5', $day))
$errorsSheet = $one.Sheet('demo-errors-start')
Check (($apply.GetMethod('ValueOf').Invoke($null, @($errorsSheet, $true))) -eq 0 -and ($apply.GetMethod('ValueOf').Invoke($null, @($errorsSheet, $false))) -eq 1 -and ($apply.GetMethod('ValueOf').Invoke($null, @($turnSheet, $true))) -eq 1) 'a list of errors: done well = the error is not there'

# ---- The agreement and the calibration.
$same = 0; $total = 0
$agree = $apply.GetMethod('Agreement'); $args2 = @($race, $same, $total); [void]$agree.Invoke($null, $args2)
Check ($args2[1] -eq 0 -and $args2[2] -eq 1) ('agreement before the coach confirms: ' + $args2[1] + '/' + $args2[2])
$sc['t.wall'].Auto = $false
$args2 = @($race, 0, 0); [void]$agree.Invoke($null, $args2)
Check ($args2[1] -eq 1 -and $args2[2] -eq 2) ('the coach confirms an AI proposal: ' + $args2[1] + '/' + $args2[2])
$tCal = T 'SwimAiCalibration'
$cal = [Activator]::CreateInstance($tCal)
Check ($cal.Sync($race, $eval, $turnSheet) -and $cal.Samples.Count -eq 2) 'the calibration: the coach''s answers beside Claude''s'
foreach ($pid2 in @('turn-p2', 'turn-p3')) {
  $e2 = [Activator]::CreateInstance($tEval); $e2.PhaseId = $pid2; $e2.SheetId = $turnSheet.Id
  $c2 = [Activator]::CreateInstance($tScore); $c2.Value = 1; $e2.Scores.Add('push.short', $c2)
  $e2.Ai = [Activator]::CreateInstance([System.Collections.Generic.Dictionary``2].MakeGenericType([string], (T 'SwimAiMark')))
  $mk = [Activator]::CreateInstance((T 'SwimAiMark')); $mk.Value = 0; $mk.Sure = 0.8; $e2.Ai.Add('push.short', $mk)
  $race.TechEvals.Add($e2); [void]$cal.Sync($race, $e2, $turnSheet)
}
$criterion = (@($turnSheet.Items) | Where-Object { $_.Key -eq 'push.short' }).Criterion.Key
$st = @([string]$criterion, 0, 0, 0, 0); [void]$tCal.GetMethod('Stats').Invoke($cal, $st)
Check ($st[2] -eq 3 -and $st[1] -eq 0 -and $st[4] -eq 3) ('a point: 3 answers, Claude stricter than the coach 3 times (' + ($st -join ',') + ')')
$lines = $cal.Lines($turnSheet, 'el')
Check (@($lines | Where-Object { $_.Contains('key push.short') -and $_.Contains('be less strict') }).Count -eq 1) 'it goes with the next request'
Check ($cal.RemoveEval($race, 'turn-p2', $turnSheet.Id) -and $cal.Samples.Count -eq 3) 'a deleted evaluation leaves the calibration'
$ctx2 = $prompt.GetMethod('TechContext').Invoke($null, @($race, $turn, $turnSheet, $one, $measures, $athlete, $moments, 2, $images.Count, 'el', $cal))
Check ((-not $ctx2.Contains('be less strict')) -and $cal.Lines($turnSheet, 'el').Count -eq 0) 'fewer than 3 answers: nothing to say yet'

# ---- The notes and the comments of a race.
$nw = $apply.GetMethod('NotesWith').Invoke($null, @("Πρώτη.`r`n`r`nAI pro (παλιό):`r`nγραμμή`r`n`r`nΤελευταία.", "AI pro (νέο):`r`nνέα"))
Check ($nw.Contains('Πρώτη.') -and $nw.Contains('Τελευταία.') -and $nw.Contains('AI pro (νέο)') -and -not $nw.Contains('παλιό') -and $nw.IndexOf('Τελευταία.') -lt $nw.IndexOf('AI pro (νέο)')) 'the notes: the old block replaced, the coach''s paragraphs kept in their order'
# (A JSON object alone in @() would be taken apart: its properties as the arguments.)
$raceJson = [Newtonsoft.Json.Linq.JObject]::Parse('{"start":"Καλή αντίδραση (0.68 s).","swim":"","turns":"Αργή στροφή στα 50.","finish":"Καλός τερματισμός.","comparison":"Χάνει 0.4 s στη στροφή.","priorities":["Στροφή","Υποβρύχια"],"doubts":[]}')
$raceAnswer = $apply.GetMethod('ParseRace').Invoke($null, [object[]](,$raceJson))
$rb = $apply.GetMethod('RaceBlock').Invoke($null, @($raceAnswer, 'claude-opus-5-5', '6/11/2026 10:00'))
Check ($rb.Contains('Εκκίνηση: Καλή αντίδραση') -and -not $rb.Contains('Κολύμβηση: ') -and $rb.Contains('Προτεραιότητες: 1) Στροφή 2) Υποβρύχια')) 'the comments of a race'
$race.TechNotes = 'Του προπονητή.'
[void]$apply.GetMethod('Race').Invoke($null, @($race, $raceAnswer, 'claude-opus-5-5', $day))
Check ($race.TechNotes.StartsWith('Του προπονητή.') -and $race.TechNotes.Contains('AI pro (Claude Opus 5.5')) 'they go to the notes of the analysis (for the report)'
$raceContext = $prompt.GetMethod('RaceContext').Invoke($null, @($race, $computed, $phases, $one, $athlete, $moments, 1, 3, 'el'))
$namedRace = @('Δοκιμαστική Αθλήτρια', 'TEST-ID-123', 'Όμιλος Δοκιμής', '2010', 'Αγώνες Δοκιμής', 'Πόλη Δοκιμής') | Where-Object { $raceContext.Contains($_) }
Check ($namedRace.Count -eq 0) 'no name in the comments of a race either'

# ---- The windows (pictures for the review).
if ($windows -and $out) {
  function Picture($form, $name) {
    try {
      $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000); $form.ShowInTaskbar = $false
      $form.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
      $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
      $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
      $b.Save([System.IO.Path]::Combine($out, $name), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
      Write-Output ('picture: ' + $name)
    } catch { Write-Output ('picture ' + $name + ' not drawn: ' + $_.Exception.Message) }
    finally { try { $form.Close() } catch { } }
  }
  Picture ([Activator]::CreateInstance((T 'FormSwimAiPassword'))) 'ai_1_password.png'
  Picture ([Activator]::CreateInstance((T 'FormSwimAiKey'), [object[]]@($true))) 'ai_2_key.png'
  try {
    $mgrType = T 'SwimAnalysisManager'
    $mgr = $mgrType.GetProperty('Instance').GetValue($null)
    $settings = [Activator]::CreateInstance((T 'SwimSettings'))
    $mgrType.GetProperty('Settings').SetValue($mgr, $settings)
    $mgrType.GetProperty('Race').SetValue($mgr, $race)
    $mgrType.GetProperty('TechPhases').SetValue($mgr, $phases)
    $form = [Activator]::CreateInstance((T 'FormSwimAiPro'), [object[]]@($mgr))
    $box = (T 'FormSwimAiPro').GetField('txtResult', $all).GetValue($form)
    $block = $apply.GetMethod('TechBlock').Invoke($null, @($turnSheet, $parsed, 'claude-opus-5-5', ''))
    $rest = (($block -split "`r`n") | Select-Object -Skip 1) -join "`r`n"
    $box.Text = (([string][char]0x2501) * 2) + ' ' + $turn.Title + ' · ' + $turnSheet.Title.Text + ' ' + (([string][char]0x2501) * 2) + "`r`n" + $rest
    Picture $form 'ai_3_window.png'
  } catch { Write-Output ('picture of the window not drawn: ' + $_.Exception.Message) }
}

try { Remove-Item -Recurse -Force $settingsDir } catch { }
Write-Output ('AI pro: ' + $fails + ' checks failed')
if ($fails -gt 0) { exit 1 }
