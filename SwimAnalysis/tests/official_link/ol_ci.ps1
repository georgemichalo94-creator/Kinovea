# ol_ci.ps1: the CI check of the results link of a race (2.28.0): the city of a competition of the KOE live results from the place
# of its pool, a link that is searched as soon as it is pasted, the link of the competition kept for its next races, the details
# of the heat or final of a link into a FICTIONAL race (its day, round, gender, competition, city); then, when the KOE live results
# answer from here, a link of a heat of a recent competition read as the program reads it (only counts are written, no swimmer).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File ol_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
$a = [System.Reflection.Assembly]::LoadFrom($sm)
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$static = [System.Reflection.BindingFlags]'NonPublic,Public,Static'

$results = T 'SwimOfficialResults'
$manager = T 'SwimAnalysisManager'

# ---- The city of a competition from the place of its pool (as the KOE live results publish them).
$cities = @(
  @('ΠΟΣΕΙΔΩΝΙΟ-1- (ΘΕΣΣΑΛΟΝΙΚΗ)', 'ΘΕΣΣΑΛΟΝΙΚ', 'Θεσσαλονίκη', 'a pool and its city in brackets'),
  @('ΕΑΚΚΑ.A. (ΜΑΡΟΥΣΙ)', 'ΟΑΚΑ', 'Μαρούσι', 'a city with its accent back'),
  @('Ν.ΣΜΥΡΝΗΣ (Ν. ΣΜΥΡΝΗ)', 'ΝΕΑ ΣΜΥΡΝΗ', 'Νέα Σμύρνη', 'a city shortened by the results'),
  @('ΑΓ.ΝΙΚΟΛΑΟΥ (ΑΓΙΟΣ ΝΙΚΟΛΑΟΣ)', 'ΑΓΝΙΚΟΛ', 'Άγιος Νικόλαος', 'a city of two words'),
  @('ΕΘΝΙΚΟ ΒΟΛΟΥ (ΒΟΛΟΣ)', 'ΒΟΛΟΥ', 'Βόλος', 'a city that ends in a final sigma'),
  @('ΠΕΙΡΑΙΑ (ΠΕΙΡΑΙΑΣ)', 'ΠΕΙΡΑΙΑΣ', 'Πειραιάς', 'a city with its accent on the end'),
  @('ΑΓΝΩΣΤΗ ΠΙΣΙΝΑ', 'DUBLIN', 'Dublin', 'a pool not named: the short place'),
  @('ΑΓΝΩΣΤΗ ΠΙΣΙΝΑ', 'PARIS (FR)', 'Paris', 'a short place with its country'),
  @('MELBOURNE, AUSTRALIA', 'MELBOURNE', 'Melbourne, Australia', 'a place with its country'),
  @('ΠΑΝΕΛΛΗΝΙΑ', 'ΠΑΝΕΛ', 'ΠΑΝΕΛΛΗΝΙΑ', 'a Greek name not known: as published'),
  @('', '', '', 'no place: none'))
foreach ($c in $cities) {
  $got = $results::KoeCity($c[0], $c[1])
  Check ($got -ceq $c[2]) ('city: ' + $c[3] + $(if ($got -cne $c[2]) { ' (got ' + (($got.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' ') + ')' } else { '' }))
}

# ---- A link searched as soon as it is pasted (fictional codes).
$isLink = $manager.GetMethod('IsResultsLink', $static)
$links = @(
  @('https://live.koe.org.gr/#/swimming-result/1/1234/0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0', $true, 'a heat of the KOE live results'),
  @('https://live.koe.org.gr/#/athletic-sports-schedule/1/1234', $true, 'a competition of the KOE live results'),
  @('https://results.microplustimingservices.com/FICTIONAL2026/', $true, 'a Microplus results site'),
  @('https://www.worldaquatics.com/competitions/1234/fictional', $true, 'a World Aquatics competition'),
  @('live.koe.org.gr', $false, 'the site alone'),
  @('https://www.example.com/page', $false, 'another site'),
  @('a few words', $false, 'words'),
  @('', $false, 'nothing'))
foreach ($l in $links) {
  $got = $isLink.Invoke($null, [object[]]@($l[0]))
  Check ($got -eq $l[1]) ('searched when pasted: ' + $l[2] + ' (' + $got + ')')
}

# ---- The link kept for the next races of the competition: its competition, not its heat.
$compLink = $manager.GetMethod('CompetitionLink', $static)
$kept = $compLink.Invoke($null, [object[]]@('https://live.koe.org.gr/#/swimming-result/1/1234/0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0'))
Check ($kept -eq 'https://live.koe.org.gr/#/athletic-sports-schedule/1/1234') ('the link kept: the competition of the heat (' + $kept + ')')
$same = $compLink.Invoke($null, [object[]]@('https://www.worldaquatics.com/competitions/1234/fictional'))
Check ($same -eq 'https://www.worldaquatics.com/competitions/1234/fictional') 'another link kept as it is'

# ---- The details of a heat or final of a link into a FICTIONAL race.
$details = $manager.GetMethod('ApplyEventDetails', $static)
$tRace = T 'SwimRace'
$tEvent = T 'SwimEventInfo'
function NewRace() {
  $r = [Activator]::CreateInstance($tRace)
  $r.Date = '22.11.2026'; $r.Competition = ''; $r.Location = ''; $r.Round = ''; $r.AthleteGender = ''
  return $r
}
function NewEvent() {
  $e = [Activator]::CreateInstance($tEvent)
  $e.Round = 'final'; $e.Gender = 'M'; $e.Competition = 'FICTIONAL OPEN CHAMPIONSHIP'; $e.Location = 'Θεσσαλονίκη'
  $e.Date = [DateTime]::new(2025, 5, 16)
  return $e
}
$race = NewRace
$done = $details.Invoke($null, [object[]]@($race, (NewEvent)))
Check ($race.Date -eq '16.5.2025') ('the day of the heat: ' + $race.Date)
Check ($race.Round -eq 'Τελικός') 'the round of the heat'
Check ($race.AthleteGender -eq 'Ανδρών') 'the gender of the heat (not typed)'
Check ($race.Competition -eq 'FICTIONAL OPEN CHAMPIONSHIP') 'the competition (not typed)'
Check ($race.Location -eq 'Θεσσαλονίκη') 'the city (not typed)'
Check ($done.Count -eq 5) ('five details said in the status (' + $done.Count + ')')
$race = NewRace
$race.Competition = 'Open typed by the analyst'; $race.Location = 'Typed city'; $race.AthleteGender = 'Γυναικών'
$done = $details.Invoke($null, [object[]]@($race, (NewEvent)))
Check ($race.Competition -eq 'Open typed by the analyst' -and $race.Location -eq 'Typed city' -and $race.AthleteGender -eq 'Γυναικών') 'what the analyst typed stays'
$race = NewRace
$race.Date = '16.5.2025'; $race.Round = 'Τελικός'
$done = $details.Invoke($null, [object[]]@($race, (NewEvent)))
Check (-not ($done -contains '16.5.2025') -and -not ($done -contains 'Τελικός')) 'the same day and round: nothing said'

# ---- The English of the new texts.
$map = $a.GetType('Kinovea.ScreenManager.SwimLangEnglish', $true).GetField('Map', $static).GetValue($null)
$keys = @('🔗 Ο σύνδεσμος είναι για {0}· ο αγώνας έμεινε {1}: δεν μπήκαν επίσημοι χρόνοι.',
  "`nΜόλις επικολλήσεις τον σύνδεσμο, η αναζήτηση ξεκινά μόνη της. Ο σύνδεσμος ενός τελικού ή μιας σειράς των live της ΚΟΕ δίνει και το αγώνισμα, τη φάση, την ημερομηνία, τη διοργάνωση και τον τόπο.")
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts (' + $missing.Count + ' missing)')

# ---- Live: a heat of a recent competition of the KOE live results, read as the program reads its link (only counts written).
$tQuery = $a.GetType('Kinovea.ScreenManager.SwimAnalysisManager+OfficialQuery', $true)
$find = $manager.GetMethod('FindOfficial', $static)
function Query($link, $remembered) {
  $q = [Activator]::CreateInstance($tQuery, $true)
  $tQuery.GetField('Link').SetValue($q, $link)
  $tQuery.GetField('LinkRemembered').SetValue($q, $remembered)
  # A race of another event: the heat of the link decides.
  $tQuery.GetField('Distance').SetValue($q, 1500)
  $tQuery.GetField('Style').SetValue($q, 'fly')
  return $q
}
function Field($o, $n) { return $o.GetType().GetField($n).GetValue($o) }
$live = $false
try {
  $today = [DateTime]::Today
  $season = $(if ($today.Month -ge 9) { $today.Year + 1 } else { $today.Year })
  $comps = @()
  foreach ($s in @($season, ($season - 1))) { $comps += @($results::KoeCompetitions($s)) }
  $comps = @($comps | Where-Object { $_.From -ne $null -and $_.From -lt $today } | Sort-Object { $_.From } -Descending | Select-Object -First 8)
  Write-Output ('info competitions read: ' + $comps.Count)
  foreach ($c in $comps) {
    $units = @($results::KoeSchedule($c.Code))
    $u = $units | Where-Object { -not $_.Relay } | Select-Object -First 1
    if ($u -eq $null) { continue }
    $link = 'https://live.koe.org.gr/#/swimming-result/1/' + $c.Code + '/' + $u.UnitCode
    $found = $find.Invoke($null, [object[]]@((Query $link $false)))
    $choices = Field $found 'Choices'
    if ($choices -eq $null -or $choices.Count -eq 0) { continue }
    $live = $true
    $unit = Field $found 'LinkUnit'
    $comp = Field $found 'Competition'
    Check ($unit -ne $null -and $unit.UnitCode -eq $u.UnitCode) 'live: the heat of the link is the race''s, whatever the race''s event'
    Check ($comp -ne $null -and $comp.Code -eq $c.Code -and (Field $found 'CompetitionOfLink')) 'live: the competition of the link found in its season'
    Check ($comp -ne $null -and $comp.City.Length -gt 0) 'live: the city of the competition'
    Check ($unit -ne $null -and $unit.Date -ne $null -and $unit.Distance -gt 0 -and $unit.Style.Length -gt 0) 'live: the day and the event of the heat'
    Write-Output ('info swimmers of the heat to choose from: ' + $choices.Count)
    $kept = $find.Invoke($null, [object[]]@((Query $link $true)))
    Check ((Field $kept 'LinkUnit') -eq $null) 'live: a link kept from another race does not decide the event'
    break
  }
} catch { Write-Output ('info the KOE live results: ' + $_.Exception.Message) }
if (-not $live) { Write-Output 'info the KOE live results were not read from here: the live part is left out' }

if ($fails -gt 0) { Write-Output ('FAILED ' + $fails); exit 1 }
Write-Output 'all official link checks passed'
