using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

// VirtualPilot — flies simulated landings and taxis through the REAL TaxiGuidanceManager
// against the pilot's own navdata DB, steering only by what a blind pilot gets: the tone's
// pan and the spoken callouts. It flags what a pilot would have hit in the sim: false
// "missed" calls, guidance that never arrives, tone leading off the pavement, runways
// entered with no hold short, route changes while following the tone, and so on.
//
//   dotnet run --project tools/VirtualPilot -p:Platform=x64 -c Release -- <mode> [options]
//     mode: landings | taxi | all | wrongturn | missturn | departures | traffic | turnpicks | legends | crossthenhold | fetchosm
//     --airports N          busiest N airports by taxi-path count (default 100)
//     --clear-first-crossing  taxi, turnpicks: every flight is flown again with the first runway its route
//                           crosses cleared (LoadRoute's clearedCrossings), and checked: no stop there, the
//                           crossing counted down and named at its line, every other stop still made
//     --crossing-keys       taxi, turnpicks: every flight is flown three times more at its first crossing:
//                           Continue right after the first countdown call (no stop, its line once); cleared,
//                           the Hold short key right after the first "cleared to cross" (a stop there, never
//                           on a runway); cleared, the Hold short key just past the line ("Past the hold line")
//     --icao A,B,C          explicit airport list (overrides --airports)
//     --shard i/n           run only airports i, i+n, i+2n … (for parallel processes)
//     --taxi-per-airport K  gate→runway and exit→gate routes per airport (default 6)
//     --out file            findings (default virtualpilot_<mode>.txt)
//     --trace ICAO:RWY:EXIT[:straight]  or  ICAO:taxi:N   one scenario, frame-by-frame
//     --sim fs2020|fs2024   which simulator's navdata database to fly (or VP_SIM); default: the
//                           MSFS 2020 one when it is installed, else the MSFS 2024 one
//     --db file             a navdata database file by path (or VP_DB); overrides --sim's choice
//
// Nothing is written to the pilot's log folder (Log.Redirect), no audio device is opened
// (TaxiSteeringTone.Headless) and time is simulated (SimClock.Override), so a landing runs
// in a fraction of a second. See docs/virtual-pilot.md.

var opt = Options.Parse(args);
if (opt == null) return 2;

var simNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
MSFSBlindAssist.Utils.SimClock.Override = () => simNow;
TaxiSteeringTone.Headless = true;
#if !MAINAPP
if (Environment.GetEnvironmentVariable("VP_LEGACY_ANCHOR") == "1") TaxiGuidanceManager.LegacyExitAnchorForHarness = true;
if (Environment.GetEnvironmentVariable("VP_LEGACY_REVENTRY") == "1") TaxiGuidanceManager.LegacyReversedEntryForHarness = true;
if (Environment.GetEnvironmentVariable("VP_LEGACY_RWYSTART") == "1") TaxiGuidanceManager.LegacyRunwayStartForHarness = true;
if (Environment.GetEnvironmentVariable("VP_LEGACY_TURNSIDE") == "1") TaxiGuidanceManager.LegacyTurnSideForHarness = true;
#endif
var logSink = new List<string>();
bool captureLog = false;
MSFSBlindAssist.Utils.Logging.Log.Redirect = (file, msg) => { if (captureLog) logSink.Add($"[{file}] {msg}"); };

// The navdata flown: the pilot's own database for one simulator, found where the app finds it.
// A before/after pair only compares when both runs fly the SAME database, so the choice is printed
// and written into the findings header.
if (NavdataChoice.Resolve(opt.Db ?? Environment.GetEnvironmentVariable("VP_DB"),
                          opt.Sim ?? Environment.GetEnvironmentVariable("VP_SIM")) is not { } nav) return 2;
string db = nav.Path;
Console.WriteLine($"Navdata: {nav.Sim} ({db})");
var baseProvider = new LittleNavMapProvider(db, nav.Sim);
// The app's own provider stack: navdata + the OSM names/holding points, from a per-airport
// snapshot of the raw Overpass answer (runs/_osm, filled by `fetchosm`). The app fetches OSM
// live and keeps it in memory only; the snapshot pins the data so before/after runs compare
// the code, not OSM churn. VP_NOOSM=1 flies plain navdata.
var osmCache = new MSFSBlindAssist.Services.TaxiAugment.TaxiDataCache(ttlDays: 3650);
var augProvider = new MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider(baseProvider, osmCache,
    Array.Empty<MSFSBlindAssist.Services.TaxiAugment.ITaxiDataSource>(), new MSFSBlindAssist.Services.TaxiAugment.MergeOptions());
string osmDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "runs", "_osm");
osmDir = Path.GetFullPath(Environment.GetEnvironmentVariable("VP_OSMDIR") ?? osmDir);
bool useOsm = Environment.GetEnvironmentVariable("VP_NOOSM") != "1";
IAirportDataProvider provider = useOsm ? augProvider : baseProvider;

List<string> airports = opt.Icaos ?? BusiestAirports(db, opt.Airports);
if (opt.Shard is { } sh) airports = airports.Where((_, i) => i % sh.n == sh.i).ToList();

if (opt.Mode == "fetchosm") { await FetchOsm(); return 0; }

var findings = new List<Finding>();
StreamWriter? outWriter = null;
StreamWriter? resultsWriter = null;
var counts = new SortedDictionary<string, int>();
int landings = 0, taxis = 0;
var settings = new UserSettings { TaxiGuidanceToneVolume = 0.0, TaxiGuidanceHardPanTone = false };
var started = DateTime.Now;
string? depTraceFilter = null;
#if !OLDAPP
// The `legends` mode: one row per leg flown, and its CSV (<out>.legends.csv).
var legendRows = new List<LegendRow>();
StreamWriter? legendCsv = null;
// --clear-first-crossing: the hold-short stops (their route nodes) of the flight FlyTaxi flew last, which its
// cleared twin is compared with, and that twin's own record.
List<int>? lastFlightHoldNodes = null;
XcFlight? lastFlightXc = null;
// --crossing-keys: the key flight FlyTaxi flew last.
KeyFlight? lastFlightKeys = null;
#endif
// The route the last FlyTaxi loaded, written beside its CODES line as a ROUTE line (compare_routes.py).
string? lastRouteLine = null;

if (opt.Trace != null) { captureLog = true; RunTrace(opt.Trace); return 0; }

outWriter = new StreamWriter(opt.Out) { AutoFlush = true };
resultsWriter = new StreamWriter(Path.ChangeExtension(opt.Out, ".results.txt")) { AutoFlush = false };
var outFile = outWriter;
outFile.WriteLine($"# VirtualPilot {opt.Mode} — {airports.Count} airports — {nav.Sim} navdata — {DateTime.Now:yyyy-MM-dd HH:mm}");
#if !OLDAPP
if (opt.Mode == "legends")
{
    legendCsv = new StreamWriter(Path.ChangeExtension(opt.Out, ".legends.csv")) { AutoFlush = true };
    legendCsv.WriteLine(LegendRow.Header);
}
if (opt.Mode == "crossthenhold")
{
    legendCsv = new StreamWriter(Path.ChangeExtension(opt.Out, ".crossthenhold.csv")) { AutoFlush = true };
    legendCsv.WriteLine(LegendRow.CrossHeader);
}
#endif
int ai = 0;
foreach (var icao in airports)
{
    ai++;
    World? w;
    try { PrimeOsm(icao); w = World.Load(provider, icao); }
    catch (Exception ex) { Add(new Finding(icao, "LOAD_FAILED", "", ex.Message)); continue; }
    if (w == null) continue;
    try
    {
        if (opt.Mode is "landings" or "all") RunLandings(w);
        if (opt.Mode is "taxi" or "all") RunTaxis(w);
        if (opt.Mode is "wrongturn") RunWrongTurns(w);
        if (opt.Mode is "rwystop") RunRunwayStops(w);
        if (opt.Mode is "missedexit") RunMissedExits(w);
        if (opt.Mode is "departures" or "taxi2") RunDepartures(w);
#if !OLDAPP
        if (opt.Mode is "traffic") RunTraffic(w);
        if (opt.Mode is "turnpicks") RunTurnPicks(w);
        if (opt.Mode is "legends") RunLegEnds(w);
        if (opt.Mode is "crossthenhold") RunCrossThenHold(w);
        if (opt.Mode is "missturn") RunMissedHoldTurns(w);
#endif
    }
    catch (Exception ex) { Add(new Finding(icao, "HARNESS_EXCEPTION", "", ex.ToString().Split('\n')[0])); }
    if (ai % 10 == 0 || ai == airports.Count)
        Console.WriteLine($"  {ai}/{airports.Count} {icao}  landings={landings} taxis={taxis} findings={findings.Count}  ({(DateTime.Now - started).TotalSeconds:F0}s)");
}

outFile.WriteLine();
#if !OLDAPP
if (opt.Mode == "legends") { LegendSummary(outFile); legendCsv?.Dispose(); }
if (opt.Mode == "crossthenhold") { CrossSummary(outFile); legendCsv?.Dispose(); }
#endif
outFile.WriteLine($"# landings={landings} taxis={taxis}");
foreach (var kv in counts) outFile.WriteLine($"# {kv.Key,-28} {kv.Value}");
Console.WriteLine($"landings={landings} taxis={taxis}");
foreach (var kv in counts) Console.WriteLine($"  {kv.Key,-28} {kv.Value}");
outWriter.Dispose();
resultsWriter.Dispose();
return 0;

// ─────────────────────────────────────────────────────────────────────────────

void Codes(string kind, string icao, string key, List<Finding> fs)
{
    var codes = fs.Select(f => f.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
    resultsWriter?.WriteLine($"CODES|{kind}|{icao}|{key}|{(codes.Count == 0 ? "OK" : string.Join(",", codes))}");
    if (lastRouteLine != null) resultsWriter?.WriteLine($"ROUTE|{kind}|{icao}|{key}|{lastRouteLine}");
    lastRouteLine = null;
    foreach (var f in fs) Add(f);
}

// A loaded route as one results field set: metres, its taxiway names in order (unnamed pavement
// left out, a run of one name said once) and a hash of its node ids, so two runs can be compared
// route by route (compare_routes.py), not only by the findings each produced.
static string RouteLine(TaxiRoute route)
{
    var names = new List<string>();
    foreach (var s in route.Segments)
        if (s.TaxiwayName.Length > 0 && (names.Count == 0 || !names[^1].Equals(s.TaxiwayName, StringComparison.OrdinalIgnoreCase)))
            names.Add(s.TaxiwayName);
    uint hash = 2166136261;
    void Mix(int id) { foreach (byte b in BitConverter.GetBytes(id)) { hash ^= b; hash *= 16777619; } }
    if (route.Segments.Count > 0) Mix(route.Segments[0].FromNode.NodeId);
    foreach (var s in route.Segments) Mix(s.ToNode.NodeId);
    return FormattableString.Invariant($"{route.TotalDistanceMeters:F0}|{string.Join(",", names)}|{hash:x8}");
}

void Add(Finding f)
{
    findings.Add(f);
    counts[f.Code] = counts.TryGetValue(f.Code, out int c) ? c + 1 : 1;
    outWriter?.WriteLine(f.ToString());
}

void RunLandings(World w)
{
    foreach (var rwy in w.Runways)
    {
        List<LandingExit> exits;
        try { exits = w.Graph.GetLandingExits(rwy); } catch { continue; }
#if !MAINAPP
        foreach (var e0 in exits)   // as LandingExitForm does
            e0.RequiresTurnBack = LandingExitDestination.RequiresTurnBack(w.Graph, e0, exits, rwy, rwy.Heading);
#endif
        foreach (var exit in exits)
        {
            if (!LandingGeometry.Feasible(rwy, exit, out _)) continue;
            landings++;
            Codes("LANDING", w.Icao, $"{rwy.RunwayID} {Name(exit)} n{exit.NodeId}", FlyLanding(w, rwy, exits, exit, straight: false, trace: false));
            if (exit.ExitType != "End" && LandingGeometry.RoomPast(rwy, exit) > 600)
                Codes("STRAIGHT", w.Icao, $"{rwy.RunwayID} {Name(exit)} n{exit.NodeId}", FlyLanding(w, rwy, exits, exit, straight: true, trace: false));
        }
    }
}

List<(TaxiPlan plan, string kind)> PlanTaxis(World w)
{
    var list = new List<(TaxiPlan, string)>();
    int seed = 17; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    var rwyEnds = w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal).ToList();
    if (gates.Count == 0 || rwyEnds.Count == 0) return list;
    for (int k = 0; k < opt.TaxiPerAirport; k++)
    {
        var gate = gates[rnd.Next(gates.Count)];
        var rwy = rwyEnds[rnd.Next(rwyEnds.Count)];
        if (k % 2 == 0)
        {
            var plan = TaxiPlan.GateToRunway(w, gate, rwy);
            if (plan != null) list.Add((plan, "out"));
        }
        else
        {
            List<LandingExit> exits;
            try { exits = w.Graph.GetLandingExits(rwy); } catch { continue; }
            if (exits.Count == 0) continue;
            var plan = TaxiPlan.ExitToGate(w, rwy, exits, exits[rnd.Next(exits.Count)], gate);
            if (plan != null) list.Add((plan, "in"));
        }
    }
    return list;
}

List<Finding> RunWrongTurnsCollect(World w)
{
    var all = new List<Finding>();
    foreach (var (plan, kind) in PlanTaxis(w))
        if (kind == "out") all.AddRange(FlyWrongTurn(w, plan));
    return all;
}

// A route loaded while stopped on a runway, midway between two exits (RJAA 34L, 2026-11-06).
void RunRunwayStops(World w)
{
    int seed = 23; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    if (gates.Count == 0) return;
    foreach (var rwy in w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal))
    {
        List<LandingExit> exits;
        try { exits = w.Graph.GetLandingExits(rwy); } catch { continue; }
        var plan = TaxiPlan.RunwayStopToGate(w, rwy, exits, gates[rnd.Next(gates.Count)]);
        if (plan == null) continue;
        Codes("RWYSTOP", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: false, trace: false));
        Codes("RWYSTOP_CLR", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: true, trace: false));
        taxis += 2;
    }
}

// The pilot rolled past the planned exit, vacated at the next one, and loaded the gate route with the
// clearance given for the planned exit. The flight behind it (VTBS 02R, 2026-11-18: cleared E7,
// vacated one exit EARLY at E10) is the other way round and is replayed with --trace ICAO:from:.
void RunMissedExits(World w)
{
    int seed = 31; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    if (gates.Count == 0) return;
    foreach (var rwy in w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal))
    {
        List<LandingExit> exits;
        try { exits = w.Graph.GetLandingExits(rwy).OrderBy(e => e.DistanceFromThresholdFeet).ToList(); } catch { continue; }
        for (int k = 0; k < 2 && exits.Count >= 2; k++)
        {
            int i = rnd.Next(exits.Count - 1);
            var gate = gates[rnd.Next(gates.Count)];
            var planned = TaxiPlan.ExitToGate(w, rwy, exits, exits[i], gate);
            var taken = TaxiPlan.ExitToGate(w, rwy, exits, exits[i + 1], gate);
            if (planned == null || taken == null) continue;
            var clearance = ClearanceOf(w, planned);
            if (clearance == null) continue;
            var plan = TaxiPlan.FromPosition(w, taken.StartLat, taken.StartLon, taken.StartHeading, gate, clearance);
            plan.Describe = $"Rwy {rwy.RunwayID} cleared {exits[i].TaxiwayName} vacated {exits[i + 1].TaxiwayName} n{exits[i + 1].NodeId} -> {gate} via {string.Join(",", clearance)}";
            Codes("MISSEDEXIT", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: false, trace: false));
            taxis++;

            // The same clearance loaded before vacating: still on the runway, 150 m past the planned exit.
            double gapM = (exits[i + 1].DistanceFromThresholdFeet - exits[i].DistanceFromThresholdFeet) * 0.3048;
            if (gapM < 300) continue;
            double along = TaxiPlan.Bearing(exits[i].Latitude, exits[i].Longitude, exits[i + 1].Latitude, exits[i + 1].Longitude);
            var (rlat, rlon) = Geo.Move(exits[i].Latitude, exits[i].Longitude, along, 150);
            var rolling = TaxiPlan.FromPosition(w, rlat, rlon, along, gate, clearance);
            rolling.Describe = $"Rwy {rwy.RunwayID} cleared {exits[i].TaxiwayName} on the runway 150 m past it n{exits[i].NodeId} -> {gate} via {string.Join(",", clearance)}";
            Codes("MISSEDEXIT_RWY", w.Icao, rolling.Describe, FlyTaxi(w, rolling, withClearance: false, trace: false));
            taxis++;
        }
    }
}

// What ATC would say for a plan: the taxiway names of its shortest route, in order.
List<string>? ClearanceOf(World w, TaxiPlan plan)
{
    var probe = new TaxiGuidanceManager(new Cap(() => simNow));
    try
    {
        if (plan.Load(probe, provider, w, null) != null || probe.RouteForHarness is not { } route) return null;
        var seq = new List<string>();
        foreach (var s in route.Segments)
            if (!string.IsNullOrEmpty(s.TaxiwayName) && (seq.Count == 0 || seq[^1] != s.TaxiwayName)) seq.Add(s.TaxiwayName);
        return seq.Count == 0 ? null : seq;
    }
    finally { probe.Dispose(); }
}

void RunWrongTurns(World w)
{
    foreach (var (plan, kind) in PlanTaxis(w))
    {
        if (kind != "out") continue;
        Codes("WRONGTURN", w.Icao, plan.Describe, FlyWrongTurn(w, plan));
        taxis++;
    }
}

// Follow the route to a junction that has a branch leading (not on the route) to a runway hold
// line within 150 m, then deliberately turn into that branch and drive at the line. The app must
// say "off route" before the aircraft gets there.
List<Finding> FlyWrongTurn(World w, TaxiPlan plan)
{
    var found = new List<Finding>();
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        if (plan.Load(mgr, provider, w, null) != null || mgr.RouteForHarness is not { } route) return found;
        var onRoute = new HashSet<int>(route.Segments.Select(sg => sg.ToNode.NodeId));
        onRoute.Add(route.Segments[0].FromNode.NodeId);
        int jIdx = -1; List<TaxiNode>? branch = null;
        for (int i = 2; i < route.Segments.Count - 2 && branch == null; i++)
        {
            var jn = route.Segments[i].ToNode;
            if (!w.Graph.Adjacency.TryGetValue(jn.NodeId, out var edges)) continue;
            foreach (var e in edges)
            {
                if (onRoute.Contains(e.ToNodeId)) continue;
                // walk the branch up to 150 m looking for a hold-short node
                var path = new List<TaxiNode>(); int cur = e.ToNodeId, prev = jn.NodeId; double len = e.DistanceMeters;
                while (len < 150 && w.Graph.Nodes.TryGetValue(cur, out var cn))
                {
                    path.Add(cn);
                    if ((cn.Type == TaxiNodeType.HoldShort || cn.Type == TaxiNodeType.ILSHoldShort) && !onRoute.Contains(cn.NodeId)) { branch = path; jIdx = i; break; }
                    if (!w.Graph.Adjacency.TryGetValue(cur, out var nx)) break;
                    var next = nx.Where(x => x.ToNodeId != prev && !onRoute.Contains(x.ToNodeId)).OrderBy(x => Math.Abs(Geo.Norm(x.BearingDegrees - (path.Count > 1 ? 0 : e.BearingDegrees)))).FirstOrDefault();
                    if (next == null) break;
                    len += next.DistanceMeters; prev = cur; cur = next.ToNodeId;
                }
                if (branch != null) break;
            }
        }
        if (branch == null) return found;
        var hs = branch[^1];
        var junction = route.Segments[jIdx].ToNode;
        string where = plan.Describe + $" — wrong turn at n{junction.NodeId} toward hold n{hs.NodeId} ({hs.HoldShortName})";
        double branchTurn = Math.Abs(Geo.Norm(TaxiPlanBearingN(junction, branch[0]) - route.Segments[jIdx].BearingDegrees));

        bool wtTrace = depTraceFilter != null && where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase);
        if (depTraceFilter != null && !wtTrace) return found;
        mgr.StartGuidance(settings); mgr.ConsumeStartHoldCue();
        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0, t = 0, holdWait = 0;
        var pilot = new PilotModel(hdg, 0);
        bool turned = false, warned = false, rerouted = false; int bi = 0;
        while (t < route.TotalDistanceMeters / 3 + 200)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            foreach (var a in cap.TakeNew())
            {
                if (wtTrace)
                {
                    var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
                    Console.WriteLine($"  {t,6:F1}s {(turned ? "TURNED " : "")}SPEAK {a}   [ac {lat:F6},{lon:F6} on={(ru == null ? "-" : ru.Name1)} dHold={TaxiGraph.CalculateDistanceMeters(lat, lon, hs.Latitude, hs.Longitude):F0}m]");
                    if (a.Contains("Route changed") && mgr.RouteForHarness is { } nr)
                        foreach (var sg in nr.Segments.Take(6))
                        {
                            var r2 = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                            Console.WriteLine($"      {sg.TaxiwayName,-5} n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {sg.DistanceMeters:F0}m hold={sg.IsHoldShortPoint} {sg.HoldShortRunway} on={(r2 == null ? "-" : r2.Name1)} fromOn={(TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.FromNode.Latitude, sg.FromNode.Longitude)?.Name1 ?? "-")}");
                        }
                }
                if (turned && a.Contains("off route", StringComparison.OrdinalIgnoreCase)) warned = true;
                if (turned && a.Contains("Route changed", StringComparison.OrdinalIgnoreCase)) rerouted = true;
            }
            if (st == TaxiGuidanceState.HoldShort && gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            // Guidance already at the lineup / arrival: the "junction" lies beyond the route's end.
            if (!turned && (st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.Arrived))
            { if (Environment.GetEnvironmentVariable("VP_WTDEBUG") == "1") Console.WriteLine($"WT_END {w.Icao} {plan.Describe} state={st} t={t:F0} dJ={TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude):F0}"); return found; }
            if (!turned && TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude) < 4) turned = true;
            if (turned)
            {
                while (bi < branch.Count - 1 && TaxiGraph.CalculateDistanceMeters(lat, lon, branch[bi].Latitude, branch[bi].Longitude) < 6) bi++;
                double want = TaxiPlanBearing(lat, lon, branch[bi]);
                double err = Geo.Norm(want - hdg);
                hdg = (hdg + Math.Clamp(err, -12 * 0.1, 12 * 0.1) + 360) % 360;
                gs += Math.Clamp(10 - gs, -0.4, 0.15);
                (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
                double dHs = TaxiGraph.CalculateDistanceMeters(lat, lon, hs.Latitude, hs.Longitude);
                if (warned || dHs < 3)
                {
                    string code = warned ? "WRONG_TURN_WARNED" : "WRONG_TURN_SILENT";
                    string why = warned ? $"warned {dHs:F0} m before the line" : "reached the hold line with no off-route warning";
                    // Re-routed through the line instead: judge the NEW route there — no runway beyond
                    // the line, or a stop before it, is safe guidance, not a silent wrong turn.
                    if (!warned && rerouted && mgr.RouteForHarness is { } nr)
                    {
                        int k = nr.Segments.FindIndex(sg => sg.ToNode.NodeId == hs.NodeId || sg.FromNode.NodeId == hs.NodeId);
                        if (k >= 0)
                        {
                            bool held = false, runway = false; double run = 0;
                            for (int j = Math.Max(0, k - 1); j < nr.Segments.Count && run < 250; j++)
                            {
                                var sg = nr.Segments[j];
                                if (TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude) != null) { runway = true; break; }
                                if (sg.IsHoldShortPoint) held = true;
                                run += sg.DistanceMeters;
                            }
                            code = !runway ? "WRONG_TURN_REROUTED_NO_RUNWAY" : held ? "WRONG_TURN_REROUTED_HELD" : "WRONG_TURN_REROUTED_UNHELD";
                            why = !runway ? "re-routed through the line; the new route enters no runway beyond it"
                                : held ? "re-routed through the line with a stop before the runway" : "re-routed ONTO the runway past the line with no stop";
                        }
                    }
                    found.Add(new Finding(w.Icao, code, where, $"branch turns {branchTurn:F0}° off the route; {why}"));
                    return found;
                }
                continue;
            }
            double target = st == TaxiGuidanceState.HoldShort ? 0 : pilot.TaxiTarget(pan, cap);
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
        }
        if (Environment.GetEnvironmentVariable("VP_WTDEBUG") == "1") Console.WriteLine($"WT_END {w.Icao} {plan.Describe} timeout state={mgr.State} turned={turned} t={t:F0} dJ={TaxiGraph.CalculateDistanceMeters(lat, lon, junction.Latitude, junction.Longitude):F0}");
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "WRONG_TURN_EXCEPTION", plan.Describe, ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

#if !OLDAPP
// ── Missed turns before a hold ───────────────────────────────────────────────
// Per taxi plan: the first hold on the way that has, within 250 m of route before it, a junction
// where the taxiway the route came in on carries straight on off the route, with the hold node 25
// to 150 m to the side of that line. The pilot follows the tone to the junction and then rolls
// straight on, once at 10 kt and once at 20 kt. A missed turn is answered by the off-route
// recalculation (MISSED_TURN_REROUTED: "Route changed", or "Off route." with why the route was
// kept). A hold-short stop while the aircraft is still farther from the hold's runway than the
// hold node is a stop on the wrong taxiway (MISSED_TURN_STOPPED_BEHIND_LINE).
void RunMissedHoldTurns(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
        foreach (double kt in new[] { 10.0, 20.0 })
        {
            var fs = FlyMissedHoldTurn(w, plan, kt);
            if (fs == null) continue;   // no such junction on this route, or the pilot never came to it
            if (depTraceFilter != null) foreach (var f in fs) Console.WriteLine("FINDING " + f);
            Codes("MISSTURN" + (int)kt, w.Icao, plan.Describe, fs);
            taxis++;
        }
}

List<Finding>? FlyMissedHoldTurn(World w, TaxiPlan plan, double missKt)
{
    var found = new List<Finding>();
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        if (plan.Load(mgr, provider, w, null) != null || mgr.RouteForHarness is not { } route) return null;
        var segs = route.Segments;
        var onRoute = new HashSet<int>(segs.Select(sg => sg.ToNode.NodeId)) { segs[0].FromNode.NodeId };
        int hIdx = -1, jIdx = -1; List<TaxiNode>? straight = null; double planSide = 0, planAlong = 0;
        for (int i = 2; i < segs.Count - 1 && straight == null; i++)   // never the last segment: that hold is the destination's own
        {
            if (!segs[i].IsHoldShortPoint) continue;
            var hn = segs[i].ToNode;
            double back = 0;
            for (int k = i; k >= 2 && back < 250 && straight == null; k--)
            {
                if (k < i && segs[k].IsHoldShortPoint) break;   // an earlier hold stands between
                back += segs[k].DistanceMeters;
                var jn = segs[k].FromNode;
                double inBrg = segs[k - 1].BearingDegrees;
                double dH = Geo.Dist(jn.Latitude, jn.Longitude, hn.Latitude, hn.Longitude);
                double rel = Geo.Norm(TaxiPlanBearingN(jn, hn) - inBrg) * Math.PI / 180;
                double along = dH * Math.Cos(rel), side = Math.Abs(dH * Math.Sin(rel));
                if (along <= 0 || side <= 25 || side > 150) continue;
                if (!w.Graph.Adjacency.TryGetValue(jn.NodeId, out var edges)) continue;
                var e = edges.Where(x => !onRoute.Contains(x.ToNodeId) && Math.Abs(Geo.Norm(x.BearingDegrees - inBrg)) <= 15)
                             .OrderBy(x => Math.Abs(Geo.Norm(x.BearingDegrees - inBrg))).FirstOrDefault();
                if (e == null) continue;
                var path = new List<TaxiNode>(); int cur = e.ToNodeId, prev = jn.NodeId; double len = e.DistanceMeters, brg = e.BearingDegrees;
                while (w.Graph.Nodes.TryGetValue(cur, out var cn))
                {
                    path.Add(cn);
                    if (len >= 300 || !w.Graph.Adjacency.TryGetValue(cur, out var nx)) break;
                    var next = nx.Where(x => x.ToNodeId != prev && Math.Abs(Geo.Norm(x.BearingDegrees - brg)) <= 30)
                                 .OrderBy(x => Math.Abs(Geo.Norm(x.BearingDegrees - brg))).FirstOrDefault();
                    if (next == null) break;
                    len += next.DistanceMeters; prev = cur; cur = next.ToNodeId; brg = next.BearingDegrees;
                }
                if (path.Count == 0 || len < along + 20) continue;   // the taxiway ends before coming level with the hold
                straight = path; hIdx = i; jIdx = k; planSide = side; planAlong = along;
            }
        }
        if (straight == null) return null;
        var hold = segs[hIdx].ToNode; var junction = segs[jIdx].FromNode;
        double holdBrg = segs[hIdx].BearingDegrees;
        string where = plan.Describe + $" — missed the turn at n{junction.NodeId} before the hold n{hold.NodeId} ({segs[hIdx].HoldShortRunway})";
        string shape = $"hold {planSide:F0} m to the side of the taxiway, {planAlong:F0} m on, its segment {segs[hIdx].DistanceMeters:F0} m long and "
                     + $"{Math.Abs(Geo.Norm(holdBrg - segs[jIdx - 1].BearingDegrees)):F0}° off the taxiway";
        bool mtTrace = depTraceFilter != null && where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase);
        if (depTraceFilter != null && !mtTrace) return null;
        mgr.StartGuidance(settings); mgr.ConsumeStartHoldCue();
        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0, t = 0, holdWait = 0, missOdo = 0, minHold = double.MaxValue;
        var pilot = new PilotModel(hdg, 0);
        bool missing = false; int bi = 0; string lastHold = "";
        double budget = route.TotalDistanceMeters / 3 + 400;
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            var said = cap.TakeNew();
            double dHold = Geo.Dist(lat, lon, hold.Latitude, hold.Longitude);
            foreach (var a in said)
            {
                if (a.Contains("Hold short", StringComparison.OrdinalIgnoreCase)) lastHold = a;
                if (mtTrace) Console.WriteLine($"  {t,6:F1}s {(missing ? "MISSED " : "")}SPEAK {a}   [ac {lat:F6},{lon:F6} hdg={hdg:F0} gs={gs:F1} pan={pan:F2} seg={mgr.SegmentIndexForHarness} dHold={dHold:F0}m]");
            }
            if (!missing)
            {
                if (st == TaxiGuidanceState.HoldShort && gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
                if (st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.Arrived) return null;
                if (mgr.SegmentIndexForHarness is int sNow && sNow > hIdx) return null;   // past the hold without coming to the junction
                if (st == TaxiGuidanceState.Taxiing && Geo.Dist(lat, lon, junction.Latitude, junction.Longitude) < 15) missing = true;
                else
                {
                    double target = st == TaxiGuidanceState.HoldShort ? 0 : pilot.TaxiTarget(pan, cap);
                    pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
                    (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
                    continue;
                }
            }
            minHold = Math.Min(minHold, dHold);
            double? behind = MetresBehindHoldLine(w, lat, lon, hold);
            string at = $"{missOdo:F0} m past the junction at {gs:F0} kt, {dHold:F0} m from the hold node, heading {Math.Abs(Geo.Norm(hdg - holdBrg)):F0}° off its segment, "
                      + (behind is double bl ? $"{bl:F0} m behind the line" : "the hold guards no runway") + $" ({lat:F6},{lon:F6}); {shape}";
            if (st == TaxiGuidanceState.HoldShort)
            {
                // 26, not 25: the app's capture circle is measured with its own distance function.
                string code = dHold <= 26 ? "MISSED_TURN_HELD_IN_CIRCLE"
                            : behind is double b && b > 10 ? "MISSED_TURN_STOPPED_BEHIND_LINE" : "MISSED_TURN_STOPPED_AT_LINE";
                found.Add(new Finding(w.Icao, code, where, $"\"{lastHold}\" {at}"));
                return found;
            }
            if (said.Any(a => a.Contains("Route changed", StringComparison.OrdinalIgnoreCase) || a.Contains("Off route.", StringComparison.Ordinal)))
            {
                found.Add(new Finding(w.Icao, "MISSED_TURN_REROUTED", where, at));
                return found;
            }
            if (st != TaxiGuidanceState.Taxiing || missOdo > 300
                || bi == straight.Count - 1 && Geo.Dist(lat, lon, straight[bi].Latitude, straight[bi].Longitude) < 6)
            {
                found.Add(new Finding(w.Icao, "MISSED_TURN_NOTHING", where, $"state {st}, nearest {minHold:F0} m; {at}"));
                return found;
            }
            while (bi < straight.Count - 1 && Geo.Dist(lat, lon, straight[bi].Latitude, straight[bi].Longitude) < 6) bi++;
            hdg = (hdg + Math.Clamp(Geo.Norm(TaxiPlanBearing(lat, lon, straight[bi]) - hdg), -12 * 0.1, 12 * 0.1) + 360) % 360;
            gs += Math.Clamp(missKt - gs, -0.4, 0.15);
            double stepM = gs * 0.5144 * 0.1;
            (lat, lon) = Geo.Move(lat, lon, hdg, stepM); missOdo += stepM;
        }
        return null;
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "MISSED_TURN_EXCEPTION", plan.Describe, ex.Message)); return found; }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
}

// How much farther from the runway a hold node guards the aircraft is than the node itself
// (negative: nearer, or across the runway); null when the node guards no runway.
static double? MetresBehindHoldLine(World w, double lat, double lon, TaxiNode hold)
{
    var runway = RunwayIncursionWatch.GuardedRunway(hold.Latitude, hold.Longitude, RunwayPavement.BuildShapes(w.Graph.RunwayCenterlines));
    if (runway == null) return null;
    double node = runway.Project(hold.Latitude, hold.Longitude).Lateral;
    return runway.Project(lat, lon).Lateral * Math.Sign(node) - Math.Abs(node);
}
#endif

static double TaxiPlanBearing(double la1, double lo1, TaxiNode n)
    => (Math.Atan2((n.Longitude - lo1) * Math.Cos(la1 * Math.PI / 180), n.Latitude - la1) * 180 / Math.PI + 360) % 360;
static double TaxiPlanBearingN(TaxiNode a, TaxiNode b) => TaxiPlanBearing(a.Latitude, a.Longitude, b);

// The hold-short nodes an incursion-guard call can be about — "Crossing <name>." or "Passing the <name> hold line.",
// <name> being a node's HoldShortName ("runway" when it has none) — every such node within the guard's 40 m (45 m
// here: the guard measures with its own fast distance), nearest first. Navdata often draws one line as two nodes a
// few metres apart, so the call is about one of them, not necessarily the nearest. Null for anything else
// ("Crossing taxiway Y6." names no hold). Every sentence of the frame's one utterance is tried, so a hold-line
// call after another "Crossing …" ("Crossing taxiway Y6. Crossing runway 01C at Y5.") is still seen.
static (List<TaxiNode> nodes, string call, bool crossing)? HoldLineCall(TaxiGraph g, string utterance, double lat, double lon)
{
    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                 utterance, @"(?<=^|\. )(?:(Crossing) (.+?)|Passing the (.+?) hold line)\.(?= |$)"))
    {
        bool crossing = m.Groups[1].Success;
        string name = crossing ? m.Groups[2].Value : m.Groups[3].Value;
        var nodes = new List<(TaxiNode n, double d)>();
        foreach (var n in g.Nodes.Values)
        {
            if (n.Type != TaxiNodeType.HoldShort && n.Type != TaxiNodeType.ILSHoldShort) continue;
            string spoken = string.IsNullOrEmpty(n.HoldShortName) ? "runway" : n.HoldShortName;
            if (!string.Equals(spoken, name, StringComparison.Ordinal)) continue;
            double d = TaxiGraph.CalculateDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < 45) nodes.Add((n, d));
        }
        if (nodes.Count > 0)
            return (nodes.OrderBy(x => x.d).Select(x => x.n).ToList(), m.Value.TrimStart('.', ' '), crossing);
    }
    return null;
}

void RunTaxis(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
    {
        Codes("TAXI", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: false, trace: false));
#if !OLDAPP
        if (opt.ClearFirstCrossing) FlyClearedFirstCrossing(w, plan, withClearance: false, "TAXI_XC", plan.Describe);
        if (opt.CrossingKeys) FlyCrossingKeys(w, plan, withClearance: false, "TAXI", plan.Describe);
#endif
        Codes("TAXI_CLR", w.Icao, plan.Describe, FlyTaxi(w, plan, withClearance: true, trace: false));
#if !OLDAPP
        if (opt.ClearFirstCrossing) FlyClearedFirstCrossing(w, plan, withClearance: true, "TAXI_CLR_XC", plan.Describe);
        if (opt.CrossingKeys) FlyCrossingKeys(w, plan, withClearance: true, "TAXI_CLR", plan.Describe);
#endif
        taxis += 2;
    }
}

#if !OLDAPP
// ── The crossing keys (--crossing-keys) ──────────────────────────────────────
// The flight FlyTaxi has just flown, flown three times more at its first crossing (KeyFlight): EARLY, nothing cleared,
// Continue (Input Y) pressed right after the first countdown call for that stop; HOLD, the crossing cleared at load (as
// --clear-first-crossing), the Hold short key (Input Alt+Y) pressed right after the first "… cleared to cross."; PAST,
// cleared, the key pressed just past the line. Each its own CODES kind (kind + "_KEY_" + variant) and KEYS| line; a
// plan whose first crossing the variant cannot be flown at is counted ("~keys VARIANT skipped: why"), not flown.
void FlyCrossingKeys(World w, TaxiPlan plan, bool withClearance, string kind, string key)
{
    foreach (string variant in new[] { KeyFlight.Early, KeyFlight.Hold, KeyFlight.Past })
    {
        plan.KeyVariant = variant;
        plan.ClearFirstCrossing = variant != KeyFlight.Early;
        try
        {
            var fs = FlyTaxi(w, plan, withClearance, trace: false);
            if (lastFlightKeys is not { } kf) continue;   // not traced
            if (kf.Skip.Length == 0 && !kf.Flown) kf.Skip = "not flown";   // the clearance's own probe found no route
            string ckey = "~keys " + variant + " " + (kf.Skip.Length > 0 ? "skipped: " + kf.Skip : kf.Outcome(fs));
            counts[ckey] = counts.TryGetValue(ckey, out int c) ? c + 1 : 1;
            if (kf.Skip.Length > 0) continue;
            taxis++;
            resultsWriter?.WriteLine($"KEYS|{kind}_KEY_{variant}|{w.Icao}|{key}|{kf.ResultFields()}");
            if (depTraceFilter != null) { Console.WriteLine("KEYS " + kf.ResultFields()); foreach (var f in fs) Console.WriteLine("FINDING " + f); }
            Codes($"{kind}_KEY_{variant}", w.Icao, key, fs);
        }
        finally { plan.KeyVariant = null; plan.ClearFirstCrossing = false; plan.ClearedCrossings = null; plan.XcBaseHoldNodes = null; }
    }
}
#endif

#if !OLDAPP
// ── Cleared crossings (--clear-first-crossing) ───────────────────────────────
// The flight FlyTaxi has just flown, flown again with the first runway its route crosses cleared: loaded once,
// the first crossing read from the route's runway events, and loaded again with clearedCrossings: [that runway],
// as the planner hands a crossing the pilot was cleared for. Its own CODES line (kind + "_XC") and an XC| line
// in the results; compared with the flight just flown, whose stops every other runway must still have.
void FlyClearedFirstCrossing(World w, TaxiPlan plan, bool withClearance, string kind, string key)
{
    plan.XcBaseHoldNodes = lastFlightHoldNodes == null ? null : new List<int>(lastFlightHoldNodes);
    plan.ClearFirstCrossing = true;
    try
    {
        var fs = FlyTaxi(w, plan, withClearance, trace: false);
        if (lastFlightXc is not { } xc) return;   // no runway crossed (counted as "~xc no runway crossed"), or not traced
        taxis++;
        counts["~xc " + xc.Outcome] = counts.TryGetValue("~xc " + xc.Outcome, out int c) ? c + 1 : 1;
        resultsWriter?.WriteLine($"XC|{kind}|{w.Icao}|{key}|{xc.ResultFields()}");
        if (depTraceFilter != null) { Console.WriteLine("XC " + xc.ResultFields()); foreach (var f in fs) Console.WriteLine("FINDING " + f); }
        Codes(kind, w.Icao, key, fs);
    }
    finally { plan.ClearFirstCrossing = false; plan.ClearedCrossings = null; plan.XcBaseHoldNodes = null; }
}

/// <summary>An edge clear of every runway's clear margin, sampled every 5 m, both ends included.</summary>
static bool EdgeClearOfRunways(TaxiGraph g, TaxiNode a, TaxiNode b)
{
    int n = Math.Max(1, (int)Math.Ceiling(Geo.Dist(a.Latitude, a.Longitude, b.Latitude, b.Longitude) / 5));
    for (int k = 0; k <= n; k++)
        if (!ClearOfRunwaysAt(g, a.Latitude + (b.Latitude - a.Latitude) * k / n, a.Longitude + (b.Longitude - a.Longitude) * k / n)) return false;
    return true;
}

/// <summary>The first runway the route crosses, not already passed, in route order (where the route reaches it).</summary>
static TaxiRouteRunwayEvent? FirstCrossing(TaxiRoute route)
    => route.RunwayEvents.Where(e => e.Kind == RunwayEventKind.Crossing && !e.Passed)
            .OrderBy(e => e.ReachNodeIndex < 0 ? int.MaxValue : e.ReachNodeIndex).FirstOrDefault();

/// <summary>
/// An excursion off a picked route before its turn and before a stop (a harness construction, not the app's "long
/// detour" refusal, which the pick outcome DETOUR and its TURN_DETOUR line record): from a junction on the route at least 100 m
/// from its start and 60 m before <paramref name="beforeSegment"/> (the earlier of the pick's departure and the
/// stop's segment), the branch leaving the route 45 to 135 degrees off its way on, followed straightest (no bend
/// over 60 degrees) over nodes the route never uses, no hold line and clear of every runway, until it is 120 m long
/// and 60 m from the route (at most 300 m). The pilot drives it as the miss flight drives the charted way, then
/// holds that heading; guidance answers with a route change, which keeps the pick's tail.
/// </summary>
static (List<TaxiNode> path, (double lat, double lon) at)? ExcursionBefore(TaxiGraph g, TaxiRoute route, int beforeSegment)
{
    var onRoute = new HashSet<int>(route.Segments.Select(s => s.ToNode.NodeId)) { route.Segments[0].FromNode.NodeId };
    var cum = new double[route.Segments.Count + 1];
    for (int i = 0; i < route.Segments.Count; i++) cum[i + 1] = cum[i] + route.Segments[i].DistanceMeters;
    if (beforeSegment < 2 || beforeSegment > route.Segments.Count) return null;
    double FromRoute(TaxiNode n) => route.Segments.Min(s => Geo.LateralToSegmentM(n.Latitude, n.Longitude,
        s.FromNode.Latitude, s.FromNode.Longitude, s.ToNode.Latitude, s.ToNode.Longitude));
    for (int i = beforeSegment - 2; i >= 0; i--)
    {
        if (cum[i + 1] < 100 || cum[beforeSegment] - cum[i + 1] < 60) continue;
        var jn = route.Segments[i].ToNode;
        double onWay = route.Segments[i + 1].BearingDegrees;
        if (!g.Adjacency.TryGetValue(jn.NodeId, out var edges)) continue;
        foreach (var e in edges.Where(e => !onRoute.Contains(e.ToNodeId))
                               .OrderBy(e => Math.Abs(Math.Abs(Geo.Norm(e.BearingDegrees - onWay)) - 90)).ThenBy(e => e.ToNodeId))
        {
            double off = Math.Abs(Geo.Norm(e.BearingDegrees - onWay));
            if (off < 45 || off > 135) continue;
            var path = new List<TaxiNode>(); var seen = new HashSet<int> { jn.NodeId };
            int cur = e.ToNodeId; double len = e.DistanceMeters, brg = e.BearingDegrees; bool ok = true;
            TaxiNode prevNode = jn;
            while (true)
            {
                if (len > 300 || !g.Nodes.TryGetValue(cur, out var cn) || onRoute.Contains(cur) || !seen.Add(cur)
                    || cn.Type is TaxiNodeType.HoldShort or TaxiNodeType.ILSHoldShort || !EdgeClearOfRunways(g, prevNode, cn))
                { ok = false; break; }
                path.Add(cn); prevNode = cn;
                if (len >= 120 && FromRoute(cn) >= 60) break;
                var next = g.Adjacency.TryGetValue(cur, out var nx)
                    ? nx.Where(x => !seen.Contains(x.ToNodeId)).OrderBy(x => Math.Abs(Geo.Norm(x.BearingDegrees - brg))).ThenBy(x => x.ToNodeId).FirstOrDefault()
                    : null;
                if (next == null || Math.Abs(Geo.Norm(next.BearingDegrees - brg)) > 60) { ok = false; break; }
                len += next.DistanceMeters; brg = next.BearingDegrees; cur = next.ToNodeId;
            }
            if (ok) return (path, (jn.Latitude, jn.Longitude));
        }
    }
    return null;
}
#endif

#if !OLDAPP
// ── Turn picks ───────────────────────────────────────────────────────────────
// Per taxi plan: the clearance a controller would give (the shortest route's taxiway names), then,
// for every row and both sides, the Turn pick a pilot could set on that row. Each pick is loaded as
// the planner loads it and what became of it is written out (a TURN| line): a pick is honored or
// SAID, never dropped in silence, and the route really turns the way it reports. A pick that
// changes the route is flown following the tone (TURN_CHARTED / TURN_OFFCHART, beside TURN_BASE:
// the same clearance with no pick), then flown once more with the pilot taking the charted way at
// the junction instead (TURN_MISS). turn_report.py reads the results.
void RunTurnPicks(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
    {
        plan.TurnPicks = null; plan.FixedSeq = null; plan.DivertPath = null;
        var r0 = LoadProbe(w, plan, null, out _, out _);
        if (r0 == null) continue;
        var seq = new List<string>();
        foreach (var s in r0.Segments)
            if (!string.IsNullOrEmpty(s.TaxiwayName) && (seq.Count == 0 || seq[^1] != s.TaxiwayName)) seq.Add(s.TaxiwayName);
        if (seq.Count == 0) continue;
        plan.FixedSeq = seq;
        var baseRoute = LoadProbe(w, plan, seq, out _, out _);
        if (baseRoute == null) { plan.FixedSeq = null; continue; }   // the clearance itself is refused: nothing to compare a pick with
        string key = plan.Describe + " via " + string.Join(",", seq);
        Codes("TURN_BASE", w.Icao, key, FlyTaxi(w, plan, withClearance: false, trace: false)); taxis++;
        if (opt.ClearFirstCrossing) FlyClearedFirstCrossing(w, plan, withClearance: false, "TURN_BASE_XC", key);
        if (opt.CrossingKeys) FlyCrossingKeys(w, plan, withClearance: false, "TURN_BASE", key);
        for (int row = 0; row < seq.Count; row++)
        {
            int met = 0;
            foreach (var side in new[] { TurnSide.Left, TurnSide.Right })
            {
                plan.TurnPicks = new Dictionary<int, TurnSide> { [row] = side }; plan.DivertPath = null;
                string pk = $"{key} ## row {row + 1} {seq[row]} {side}";
                string where = plan.Describe + plan.PickSuffix();
                var route = LoadProbe(w, plan, seq, out string said, out string? err);
                var applied = route?.TurnPicks.FirstOrDefault(p => p.RowIndex == row);
                string sideWord = side == TurnSide.Left ? "left" : "right";
                string outcome =
                    route == null ? "LOAD_FAILED"
                    : said.Contains($"no {sideWord} turn onto taxiway", StringComparison.OrdinalIgnoreCase) ? "NO_TURN"
                    : said.Contains("does not lead to", StringComparison.OrdinalIgnoreCase) ? "NO_LEAD"
                    : said.Contains("would be a long detour", StringComparison.OrdinalIgnoreCase) ? "DETOUR"
                    : said.Contains($"onto taxiway {seq[row]} does not pass", StringComparison.OrdinalIgnoreCase) ? "NO_PIN"
                    : applied == null ? "SILENT"
                    : applied.IsOffChart ? "OFFCHART" : applied.ChangedRoute ? "CHARTED" : "MET";
                if (outcome == "MET") met++;
                counts["~pick " + outcome] = counts.TryGetValue("~pick " + outcome, out int oc) ? oc + 1 : 1;
                var fs = new List<Finding>();
                double? turned = route != null && applied != null ? TurnOnto(route, applied, plan.StartHeading) : null;
                if (depTraceFilter != null && where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"=== {where}\nPICK {outcome}: {said}\n     " + (applied == null ? "no applied pick" :
                        $"departure n{applied.DepartureNodeId}, join n{applied.JoinNodeId}, segments {applied.FirstSegmentAfterDeparture}..{applied.FirstTaxiwaySegment}, "
                        + $"start heading {plan.StartHeading:F0}, measured turn {turned:F0} (negative is left), {route!.TotalDistanceMeters:F0} m against {baseRoute.TotalDistanceMeters:F0} m"));
                if (outcome == "LOAD_FAILED") fs.Add(new Finding(w.Icao, "TURN_PICK_BROKE_THE_LOAD", where, err ?? "no route"));
                if (outcome == "SILENT") fs.Add(new Finding(w.Icao, "TURN_PICK_SILENT", where, $"neither applied nor said: {said}"));
                if (outcome == "OFFCHART" && !said.Contains("off-chart", StringComparison.OrdinalIgnoreCase))
                    fs.Add(new Finding(w.Icao, "OFFCHART_NOTE_MISSING", where, said));
                if (outcome == "CHARTED" && !said.Contains("as picked", StringComparison.OrdinalIgnoreCase))
                    fs.Add(new Finding(w.Icao, "TURN_CHARTED_NOT_CONFIRMED", where, said));
                if (route != null && applied is { ChangedRoute: true } && BackOnTheOtherArm(w.Graph, route, applied) is { } backOn)
                    fs.Add(new Finding(w.Icao, backOn.travels ? "TURN_TRAVELS_THE_OTHER_ARM" : "TURN_CROSSES_THE_OTHER_ARM", where, backOn.what));
                if (turned is double dTurn && (side == TurnSide.Left ? dTurn > 25 : dTurn < -25))
                    fs.Add(new Finding(w.Icao, outcome == "MET" ? "TURN_MET_WRONG_SIDE" : "TURN_HONORED_WRONG_SIDE", where,
                        $"the route turns {Math.Abs(dTurn):F0}° {(dTurn > 0 ? "right" : "left")} onto {seq[row]}"));
                resultsWriter?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"TURN|{w.Icao}|{pk}|{outcome}|{route?.TotalDistanceMeters ?? 0:F0}|{baseRoute.TotalDistanceMeters:F0}|{(applied?.IsOffChart == true ? applied.OffChartLengthMeters : 0):F0}|{turned:F0}"));

                if (outcome is "CHARTED" or "OFFCHART")
                {
                    fs.AddRange(FlyTaxi(w, plan, withClearance: false, trace: false)); taxis++;
                    Codes("TURN_" + outcome, w.Icao, pk, fs);
                    if (opt.ClearFirstCrossing)
                    {
                        FlyClearedFirstCrossing(w, plan, withClearance: false, "TURN_" + outcome + "_XC", pk);
                        // The kept-tail recalculation: an excursion off the route before the turn and before the cleared
                        // stop, flown with nothing cleared and then cleared; the stop is either in the kept tail (at or
                        // after the turn's departure node) or on the stretch the recalculation builds afresh.
                        // Read from the picked route as loaded (clearing moves no stop), not from the cleared flight,
                        // which a --trace filter can skip.
                        if (FirstCrossing(route!) is { Held: true, StopNodeIndex: > 0 } fc)
                        {
                            int stopSeg = fc.StopNodeIndex - 1;
                            bool inTail = stopSeg >= applied!.FirstSegmentAfterDeparture;
                            if (ExcursionBefore(w.Graph, route!, Math.Min(stopSeg, applied.FirstSegmentAfterDeparture)) is { } dt)
                            {
                                string dk = pk + (inTail ? " ## excursion, cleared stop in the kept tail" : " ## excursion, cleared stop before the turn");
                                plan.DivertPath = dt.path; plan.DivertAt = dt.at; plan.DivertIsExcursion = true;
                                Codes("TURN_EXCURSION", w.Icao, dk, FlyTaxi(w, plan, withClearance: false, trace: false)); taxis++;
                                FlyClearedFirstCrossing(w, plan, withClearance: false, "TURN_EXCURSION_XC", dk);
                                plan.DivertPath = null; plan.DivertIsExcursion = false;
                            }
                            else counts["~xc excursion: no junction before the turn"] = counts.TryGetValue("~xc excursion: no junction before the turn", out int nd) ? nd + 1 : 1;
                        }
                    }
                    // After the cleared flights, which compare with the stops of the flight just before them.
                    if (opt.CrossingKeys) FlyCrossingKeys(w, plan, withClearance: false, "TURN_" + outcome, pk);
                    if (ChartedWayAt(route!, baseRoute, applied!) is { } dv)
                    {
                        plan.DivertPath = dv.path; plan.DivertAt = dv.at;
                        Codes("TURN_MISS", w.Icao, pk, FlyTaxi(w, plan, withClearance: false, trace: false)); taxis++;
                        if (opt.ClearFirstCrossing) FlyClearedFirstCrossing(w, plan, withClearance: false, "TURN_MISS_XC", pk);
                        plan.DivertPath = null;
                    }
                }
                else Codes("TURN_" + outcome, w.Icao, pk, fs);
            }
            if (met == 2) Add(new Finding(w.Icao, "TURN_BOTH_SIDES_MET", $"{key} ## row {row + 1} {seq[row]}", "Left and Right are both reported as the turn the route already makes"));
        }
        plan.TurnPicks = null; plan.FixedSeq = null;
    }
}

/// <summary>Loads a plan as the planner would and returns its route with everything the pilot is told about it.</summary>
TaxiRoute? LoadProbe(World w, TaxiPlan plan, List<string>? seq, out string said, out string? err)
{
    var m = new TaxiGuidanceManager(new Cap(() => simNow));
    try
    {
        err = plan.Load(m, provider, w, seq == null ? null : new List<string>(seq));
        said = ((m.LastRouteSummary ?? "") + " " + (StartWarning(m) ?? "")).Trim();
        return err == null ? m.RouteForHarness : null;
    }
    finally { try { m.StopGuidance(); m.Dispose(); } catch { } }
}

/// <summary>
/// Which way a route turns onto a picked row's taxiway, measured HERE and not by the app: every
/// change of heading added up, from the heading the aircraft reaches the junction with, through the
/// connection, and along the taxiway for 60 m or until the route leaves it (the turn onto the next
/// taxiway is the next row's). Added up and not the net difference, because a turn of more than 180
/// degrees (out of a stand and back along the taxiway) reads the wrong way round as a net figure.
/// Negative is left. A second opinion on the app's own turn test, so only a clear contradiction counts.
/// </summary>
static double? TurnOnto(TaxiRoute route, AppliedTurnPick p, double startHeading)
{
    int a = p.FirstSegmentAfterDeparture, j = p.FirstTaxiwaySegment;
    if (a < 0 || j < a || j >= route.Segments.Count) return null;
    double heading = a > 0 ? route.Segments[a - 1].BearingDegrees : startHeading, total = 0, past = 0;
    for (int k = a; k < route.Segments.Count; k++)
    {
        if (k > j && !string.Equals(route.Segments[k].TaxiwayName, p.TaxiwayName, StringComparison.OrdinalIgnoreCase)) break;
        total += Geo.Norm(route.Segments[k].BearingDegrees - heading);
        heading = route.Segments[k].BearingDegrees;
        if (k >= j && (past += route.Segments[k].DistanceMeters) >= 60) break;
    }
    return total;
}

/// <summary>
/// Does a route that honored a pick come back onto the arm of the taxiway the pick turned AWAY from?
/// Worked out HERE from the graph: the arm is every node reached along the taxiway's own edges from
/// the join's other neighbours and not also from the node the route went to (a ring has no other arm).
/// TRAVELS: a later segment named for the taxiway runs between two nodes of that arm. CROSSES: the
/// route only passes a node of it, on another taxiway (a junction; the route's last node, its
/// destination, does not count).
/// </summary>
static (bool travels, string what)? BackOnTheOtherArm(TaxiGraph graph, TaxiRoute route, AppliedTurnPick p)
{
    int j = p.FirstTaxiwaySegment;
    if (j < 0 || j >= route.Segments.Count) return null;
    int u = route.Segments[j].FromNode.NodeId, v = route.Segments[j].ToNode.NodeId;
    bool OnTaxiway(TaxiEdge e) => string.Equals(e.TaxiwayName, p.TaxiwayName, StringComparison.OrdinalIgnoreCase);
    HashSet<int> Reach(IEnumerable<int> from)
    {
        var seen = new HashSet<int>(); var queue = new Queue<int>();
        foreach (int id in from) if (id != u && seen.Add(id)) queue.Enqueue(id);
        while (queue.Count > 0)
            if (graph.Adjacency.TryGetValue(queue.Dequeue(), out var edges))
                foreach (var e in edges)
                    if (OnTaxiway(e) && e.ToNodeId != u && seen.Add(e.ToNodeId)) queue.Enqueue(e.ToNodeId);
        return seen;
    }
    var arm = Reach(graph.Adjacency.TryGetValue(u, out var fromU) ? fromU.Where(e => OnTaxiway(e) && e.ToNodeId != v).Select(e => e.ToNodeId) : Enumerable.Empty<int>());
    arm.ExceptWith(Reach(new[] { v }));
    for (int k = j + 1; k < route.Segments.Count; k++)
    {
        var s = route.Segments[k];
        if (string.Equals(s.TaxiwayName, p.TaxiwayName, StringComparison.OrdinalIgnoreCase)
            && arm.Contains(s.FromNode.NodeId) && arm.Contains(s.ToNode.NodeId))
            return (true, $"segment {k} of {route.Segments.Count} runs along {p.TaxiwayName} from n{s.FromNode.NodeId} to n{s.ToNode.NodeId}, the other way from the join n{u}");
    }
    for (int k = j + 1; k < route.Segments.Count - 1; k++)
        if (arm.Contains(route.Segments[k].ToNode.NodeId))
            return (false, $"segment {k} of {route.Segments.Count} ({route.Segments[k].TaxiwayName}) passes n{route.Segments[k].ToNode.NodeId}, on {p.TaxiwayName} the other way from the join n{u}");
    return null;
}

/// <summary>Where the picked route leaves the charted one at or after the pick's junction, and the charted way on from there.</summary>
static (List<TaxiNode> path, (double lat, double lon) at)? ChartedWayAt(TaxiRoute picked, TaxiRoute charted, AppliedTurnPick p)
{
    var chartedFrom = new Dictionary<int, int>();   // node -> the charted segment leaving it (first visit)
    for (int i = 0; i < charted.Segments.Count; i++) chartedFrom.TryAdd(charted.Segments[i].FromNode.NodeId, i);
    for (int i = Math.Max(1, p.FirstSegmentAfterDeparture); i < picked.Segments.Count; i++)
    {
        var sg = picked.Segments[i];
        if (!chartedFrom.TryGetValue(sg.FromNode.NodeId, out int ci)) return null;   // the charted route never comes here
        if (charted.Segments[ci].ToNode.NodeId == sg.ToNode.NodeId) continue;         // still the same way
        var path = new List<TaxiNode>(); double len = 0;
        for (int k = ci; k < charted.Segments.Count && len < 250; k++) { path.Add(charted.Segments[k].ToNode); len += charted.Segments[k].DistanceMeters; }
        return (path, (sg.FromNode.Latitude, sg.FromNode.Longitude));
    }
    return null;
}

// ── Leg ends: "hold short of taxiway B" on a leg with no destination ─────────────
// A Progressive Taxi leg, "via A, hold short of taxiway B", loaded as the form loads it: the junction
// is the first by taxi distance from the node the aircraft is on (FindTaxiwayIntersectionNodes +
// NearestByTaxiDistance, which leave out a junction inside a runway's clear margin), the wingspan
// 36 m. Per airport, up to K ordered pairs (A, B) that meet clear of every runway, in a seeded order:
//   plain: start on A, 250 to 600 m along A from the nearest junction and facing it (only where the
//          form's own pick is that junction);
//   turn:  for up to K pairs where A meets B on both sides of a node M at which a taxiway Z joins A,
//          start on Z 150 to 400 m before M, facing M, where A leaves M to each side (a T), the form's
//          junction is on one of M's two arms and the clearance's route turns onto A at M, and load
//          "via Z, A" with the Turn on A's row set Left, then Right (spec 8.1: a turn word is the Turn
//          pick).
// Each leg is flown following the tone; the pilot brakes on any line ending "Stop." and the flight
// ends at a standstill after the leg has ended (ProgressiveHold). Where it ended and where the aircraft
// came to rest are measured HERE, along the route and on over the graph to the junction the app holds
// short of (JunctionAhead; the node is the app's, read by reflection, with the harness's own pick of it
// beside it as a cross-check). After every load the route is checked: each Turn pick's indices
// still name its departure and join (LEGEND_PICK_INDEX), no off-chart leg is split
// (LEGEND_OFFCHART_SPLIT) or ends the route (LEGEND_ENDS_OFFCHART), a pick that changed the route
// passes no node on B between its turn and its stop (LEGEND_PICK_NOT_FIRST_JUNCTION), and a pick the leg
// ends before (dropped from the route) is never said as made (LEGEND_DROPPED_PICK_SPOKEN). The rest of the
// route is read too: LEG_PICK_LINK_CROSSES_B on the charted link to the picked turn,
// LEG_PASSES_B_JUNCTION without such a pick, LEG_PASSES_B_IN_RUNWAY_MARGIN for a meeting inside a
// runway's clear margin, which the app skips by design. While flying, LEG_ENDED_ON_RUNWAY when the leg
// ends, or its countdown says "Slow down" or "Stop", with the aircraft on runway pavement (spec 8.5, 8.8).
// A stop can insert a node into the airport's shared graph, as at the pilot's Calculate in the app,
// so legs are flown in a fixed order and only nodes present at load are chosen as starts.
void RunLegEnds(World w)
{
    var g = w.Graph;
    int seed = 41; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var original = new HashSet<int>(g.Nodes.Keys);
    var pairNodes = TaxiwayPairNodes(g);
    var order = pairNodes.Keys.Select(k => (key: k, r: rnd.Next())).OrderBy(x => x.r).ThenBy(x => x.key, StringComparer.Ordinal)
                              .Select(x => x.key).ToList();
    int plain = 0, turns = 0;
    foreach (var key in order)
    {
        if (plain >= opt.TaxiPerAirport && turns >= opt.TaxiPerAirport) break;
        var ab = key.Split('\u0001');
        string a = ab[0], b = ab[1];
        var junctions = PairJunctions(g, pairNodes, key, a, b);
        if (junctions.Count == 0) continue;   // they meet only inside a runway's clear margin
        if (plain < opt.TaxiPerAirport && PlainLegStart(w, a, b, junctions, original, rnd) is { } pl)
        {
            plain++;
            FlyAndRecordLeg(w, pl);
        }
        if (turns < opt.TaxiPerAirport && TurnLegStart(w, a, b, junctions, original, rnd) is { } tl)
        {
            turns++;
            foreach (var side in new[] { TurnSide.Left, TurnSide.Right })
            {
                tl.Pick = side;
                FlyAndRecordLeg(w, tl);
            }
        }
    }
}

/// <summary>Every ordered pair of named taxiways sharing a node ("A\u0001B"), with the nodes they share.</summary>
static SortedDictionary<string, List<int>> TaxiwayPairNodes(TaxiGraph g)
{
    var pairNodes = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
    foreach (var (id, edges) in g.Adjacency)
    {
        var names = edges.Select(e => e.TaxiwayName).Where(n => !string.IsNullOrWhiteSpace(n))
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var a in names)
            foreach (var b in names)
                if (!a.Equals(b, StringComparison.OrdinalIgnoreCase))
                {
                    string k = a + "\u0001" + b;
                    if (!pairNodes.TryGetValue(k, out var l)) pairNodes[k] = l = new List<int>();
                    l.Add(id);
                }
    }
    return pairNodes;
}

/// <summary>Where A meets B clear of every runway's margin, in every component they share a node in (FindTaxiwayIntersectionNodes).</summary>
static HashSet<int> PairJunctions(TaxiGraph g, SortedDictionary<string, List<int>> pairNodes, string key, string a, string b)
{
    var junctions = new HashSet<int>();
    foreach (int comp in pairNodes[key].Select(n => g.Nodes.TryGetValue(n, out var nn) ? nn.ComponentId : -1).Distinct())
        foreach (int j in g.FindTaxiwayIntersectionNodes(a, b, comp)) junctions.Add(j);
    return junctions;
}

void FlyAndRecordLeg(World w, LegPlan lp)
{
    var fs = new List<Finding>();
    bool traced = depTraceFilter != null && lp.Describe().Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase);
    var row = FlyLeg(w, lp, traced, fs);
    taxis++;
    if (depTraceFilter != null)
    {
        if (!traced) return;
        legendRows.Add(row);
        foreach (var f in fs) Console.WriteLine("FINDING " + f);
        return;
    }
    legendRows.Add(row);
    legendCsv?.WriteLine(lp.Kind == "cross" ? row.CsvCross() : row.Csv());
    Codes(lp.Kind == "cross" ? "CROSSHOLD" : "LEGEND", w.Icao, lp.Describe(), fs);
}

// ── Cross then hold: "cross runway R, hold short of taxiway B" ─────────────────
// Stage 1's final review: the KCLE shape (P across runway 24L, then hold short of L), where B meets the route just
// past a runway R the route crosses. Per airport, up to K pairs (A, B), in a seeded order, where the way along A to
// the A/B junction the form picks crosses R and R's far pavement edge is 150 m or less before that junction: a start
// on A 150 to 1,200 m before the junction (clear of every runway, facing it), whose route as the form loads it
// ("via A", no destination, hold short of taxiway B, wingspan 36 m) crosses R first. It is flown with that crossing
// cleared (clearedCrossings: [R]) and measured as a `legends` leg is, plus: HoldShort entered at R's stop, the
// "Crossing runway R" line, the "… cleared to cross." countdown, and where the stop for B lies against R.
void RunCrossThenHold(World w)
{
    var g = w.Graph;
    int seed = 43; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var original = new HashSet<int>(g.Nodes.Keys);
    var pairNodes = TaxiwayPairNodes(g);
    var order = pairNodes.Keys.Select(k => (key: k, r: rnd.Next())).OrderBy(x => x.r).ThenBy(x => x.key, StringComparer.Ordinal)
                              .Select(x => x.key).ToList();
    int legs = 0;
    foreach (var key in order)
    {
        if (legs >= opt.TaxiPerAirport) break;
        var ab = key.Split('\u0001');
        string a = ab[0], b = ab[1];
        var junctions = PairJunctions(g, pairNodes, key, a, b);
        if (junctions.Count == 0) continue;
        if (CrossLegStart(w, a, b, junctions, original, rnd) is not { } lp) continue;
        legs++;
        FlyAndRecordLeg(w, lp);
    }
}

/// <summary>
/// A cross-then-hold leg's start: a node on A 150 to 1,200 m along A before the nearest A/B junction, a usable start
/// (UsableLegStart), whose way along A to that junction crosses one runway's pavement and no other, the far edge 150 m
/// or less before the junction, where the form picks that junction, and whose route as loaded crosses that runway first
/// (ProbeCrossLeg). Up to 40 candidates in a seeded order, the pair skipped unless an edge of A within 450 m of a
/// junction touches runway pavement.
/// </summary>
LegPlan? CrossLegStart(World w, string a, string b, HashSet<int> junctions, HashSet<int> original, Random rnd)
{
    var g = w.Graph;
    var (dist, toward) = AlongTaxiway(g, junctions, a, blocked: -1, maxM: 1300);
    bool nearRunway = dist.Where(kv => kv.Value <= 450 && toward.ContainsKey(kv.Key))
                          .Any(kv => RunwayAlong(g, new List<int> { kv.Key, toward[kv.Key] }).R != null);
    if (!nearRunway) return null;
    var cands = dist.Where(kv => kv.Value >= 150 && kv.Value <= 1200 && original.Contains(kv.Key))
                    .Select(kv => kv.Key).OrderBy(x => x).ToList();
    int probes = 0;
    for (int tries = 0; tries < 40 && probes < 4 && cands.Count > 0; tries++)
    {
        int i = rnd.Next(cands.Count); int id = cands[i]; cands.RemoveAt(i);
        if (!UsableLegStart(g, id)) continue;
        var chain = new List<int> { id };
        for (int c = id, guard = 0; !junctions.Contains(c) && guard < 100000; guard++) { c = toward[c]; chain.Add(c); }
        var (r, far, total, other) = RunwayAlong(g, chain);
        if (r == null || other || double.IsNaN(far) || total - far > 150) continue;
        int jn = chain[^1];
        var sn = g.Nodes[id];
        if (FormLegEnd(g, sn.Latitude, sn.Longitude, a, b) != jn) continue;
        var nx = g.Nodes[toward[id]];
        var lp = new LegPlan
        {
            Icao = w.Icao, Kind = "cross", A = a, B = b, StartNode = id, Dest = jn, AlongM = dist[id],
            Lat = sn.Latitude, Lon = sn.Longitude, Hdg = TaxiPlan.Bearing(sn.Latitude, sn.Longitude, nx.Latitude, nx.Longitude),
            Seq = new List<string> { a }, ClearRunway = r.Name1, CrossRunway = r, EdgeToJunctionM = total - far,
        };
        probes++;
        if (ProbeCrossLeg(w, lp)) return lp;
    }
    return null;
}

/// <summary>The leg loaded as the form loads it with nothing cleared: its route must cross the runway first. Records whether it stopped for it.</summary>
bool ProbeCrossLeg(World w, LegPlan lp)
{
    var m = new TaxiGuidanceManager(new Cap(() => simNow));
    try
    {
        m.AircraftWingspanMetres = 36;
        string? err = m.LoadRoute(provider, w.Icao, lp.Lat, lp.Lon, lp.Hdg, lp.Dest, $"hold short of taxiway {lp.B}",
            new List<string>(lp.Seq), null,
            destinationHeading: null, destinationThresholdLat: null, destinationThresholdLon: null, destinationHeadingTrue: null,
            isRunwayDestination: false, prebuiltGraph: w.Graph, announceSummary: true,
            progressiveTerminator: new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, lp.B));
        if (err != null || m.CurrentRoute is not { Segments.Count: > 0 } r) return false;
        var first = FirstCrossing(r);
        if (first == null || !XcFlight.NamesRunway("runway " + first.Designator, lp.ClearRunway!)) return false;
        lp.RunwayHeldAtLoad = first.Held;
        return true;
    }
    finally { try { m.StopGuidance(); m.Dispose(); } catch { } }
}

/// <summary>
/// Runway pavement along a chain of nodes, sampled every 2 m: the first runway entered, the metres along the chain to
/// the last point on its pavement (its far edge, NaN when none), the chain's length, and whether the way meets any
/// other runway's pavement, or that runway's again after leaving it.
/// </summary>
static (TaxiGraph.RunwayCenterline? R, double FarEdgeM, double TotalM, bool Other) RunwayAlong(TaxiGraph g, List<int> chain)
{
    TaxiGraph.RunwayCenterline? r = null; double far = double.NaN, total = 0; bool left = false, other = false;
    for (int i = 0; i + 1 < chain.Count; i++)
    {
        if (!g.Nodes.TryGetValue(chain[i], out var p) || !g.Nodes.TryGetValue(chain[i + 1], out var q)) break;
        double len = Geo.Dist(p.Latitude, p.Longitude, q.Latitude, q.Longitude);
        int n = Math.Max(1, (int)Math.Ceiling(len / 2));
        for (int k = i == 0 ? 0 : 1; k <= n; k++)
        {
            double f = (double)k / n;
            var under = RouteRunwayCrossings.RunwayUnder(g.RunwayCenterlines,
                p.Latitude + (q.Latitude - p.Latitude) * f, p.Longitude + (q.Longitude - p.Longitude) * f);
            if (under == null) { if (r != null) left = true; continue; }
            r ??= under;
            if (!ReferenceEquals(under, r) || left) other = true;
            else far = total + len * f;
        }
        total += len;
    }
    return (r, far, total, other);
}

void CrossSummary(TextWriter? file)
{
    void Line(string s) { Console.WriteLine(s); file?.WriteLine("# " + s); }
    var rows = legendRows;
    if (rows.Count == 0) { Line("cross then hold: no legs"); return; }
    var ended = rows.Where(r => r.Ended).ToList();
    var stand = ended.Select(r => r.StandM).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
    var past = rows.Select(r => r.StopPastEdgeM).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
    Line(string.Create(CultureInfo.InvariantCulture,
        $"cross then hold: {rows.Count} legs; runway stopped for with nothing cleared {rows.Count(r => r.RunwayHeldAtLoad)}; "
        + $"cleared {string.Join(", ", rows.GroupBy(r => r.XcOutcome).OrderBy(gr => gr.Key, StringComparer.Ordinal).Select(gr => $"{gr.Key} {gr.Count()}"))}"));
    Line(string.Create(CultureInfo.InvariantCulture,
        $"held at the runway {rows.Count(r => r.HeldAtRunway > 0)}; \"Crossing runway\" lines 0/1/2+: {rows.Count(r => r.CrossingLines == 0)}/"
        + $"{rows.Count(r => r.CrossingLines == 1)}/{rows.Count(r => r.CrossingLines > 1)}; said before its segment {rows.Count(r => r.CrossingEarly > 0)}; "
        + $"\"cleared to cross\" said {rows.Count(r => r.ClearedApproach > 0)}"));
    Line(string.Create(CultureInfo.InvariantCulture,
        $"ended {ended.Count} ({100.0 * ended.Count / rows.Count:F1} %), at or past the junction {ended.Count(r => r.EndM <= 0 || r.StandM <= 0)}, "
        + $"standstill before the junction min {(stand.Count > 0 ? stand[0] : double.NaN):F1} m median {MedianOf(stand):F1} m; "
        + $"LEG_ENDED_ON_RUNWAY {rows.Sum(r => r.EndedOnRunway)}"));
    Line(string.Create(CultureInfo.InvariantCulture,
        $"stop for B: before the runway {rows.Count(r => r.StopBeforeRunway)}, clear of every runway's margin {rows.Count(r => r.StopClearOfMargin)}, "
        + $"its 25 m approach clear {rows.Count(r => r.Approach25Clear)}; past the runway's far edge min {(past.Count > 0 ? past[0] : double.NaN):F1} m "
        + $"median {MedianOf(past):F1} m"));
}

/// <summary>A plain leg: a node on A 250 to 600 m along A from the nearest junction, facing it.</summary>
LegPlan? PlainLegStart(World w, string a, string b, HashSet<int> junctions, HashSet<int> original, Random rnd)
{
    var g = w.Graph;
    var (dist, toward) = AlongTaxiway(g, junctions, a, blocked: -1, maxM: 700);
    var cands = dist.Where(kv => kv.Value >= 250 && kv.Value <= 600 && original.Contains(kv.Key))
                    .Select(kv => kv.Key).OrderBy(x => x).ToList();
    for (int tries = 0; tries < 8 && cands.Count > 0; tries++)
    {
        int i = rnd.Next(cands.Count); int id = cands[i]; cands.RemoveAt(i);
        if (!UsableLegStart(g, id)) continue;
        int j = id;
        for (int guard = 0; !junctions.Contains(j) && guard < 100000; guard++) j = toward[j];
        var sn = g.Nodes[id];
        // The junction the form would end the leg at: only where it is the one the aircraft faces.
        if (FormLegEnd(g, sn.Latitude, sn.Longitude, a, b) != j) continue;
        var nx = g.Nodes[toward[id]];
        return new LegPlan
        {
            Icao = w.Icao, Kind = "plain", A = a, B = b, StartNode = id, Dest = j, AlongM = dist[id],
            Lat = sn.Latitude, Lon = sn.Longitude, Hdg = TaxiPlan.Bearing(sn.Latitude, sn.Longitude, nx.Latitude, nx.Longitude),
            Seq = new List<string> { a },
        };
    }
    return null;
}

/// <summary>
/// A turn leg: a node M on A with exactly two A neighbours and an edge on another taxiway Z, where A
/// meets B (clear of every runway) within 1,200 m on both sides; the start is on Z, 150 to 400 m along
/// Z from M, facing M, on a stretch of Z that touches neither A nor B before M, and arriving along it A
/// leaves M 30 to 150 degrees to each side (a T).
/// </summary>
LegPlan? TurnLegStart(World w, string a, string b, HashSet<int> junctions, HashSet<int> original, Random rnd)
{
    var g = w.Graph;
    var ms = new List<(int m, string z)>();
    foreach (var (id, edges) in g.Adjacency)
    {
        if (!original.Contains(id) || edges.Any(e => Named(e.TaxiwayName, b))) continue;
        if (edges.Where(e => Named(e.TaxiwayName, a)).Select(e => e.ToNodeId).Distinct().Count() != 2) continue;
        foreach (var z in edges.Select(e => e.TaxiwayName)
                     .Where(n => !string.IsNullOrWhiteSpace(n) && !Named(n, a) && !Named(n, b))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            ms.Add((id, z));
    }
    ms = ms.OrderBy(x => x.m).ThenBy(x => x.z, StringComparer.Ordinal).ToList();
    for (int tries = 0; tries < 12 && ms.Count > 0; tries++)
    {
        int i = rnd.Next(ms.Count); var (m, z) = ms[i]; ms.RemoveAt(i);
        var mn = g.Nodes[m];
        if (!ClearOfRunwaysAt(g, mn.Latitude, mn.Longitude)) continue;
        var arms = new List<int>();
        foreach (int nb in g.Adjacency[m].Where(e => Named(e.TaxiwayName, a)).Select(e => e.ToNodeId).Distinct())
        {
            var (d, _) = AlongTaxiway(g, new[] { nb }, a, blocked: m, maxM: 1200);
            int jn = d.Where(kv => junctions.Contains(kv.Key)).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key)
                      .Select(kv => kv.Key).DefaultIfEmpty(-1).First();
            if (jn >= 0) arms.Add(jn);
        }
        if (arms.Count != 2 || arms[0] == arms[1]) continue;
        var (zd, zToward) = AlongTaxiway(g, new[] { m }, z, blocked: -1, maxM: 420);
        var zc = zd.Where(kv => kv.Value >= 150 && kv.Value <= 400 && original.Contains(kv.Key))
                   .Select(kv => kv.Key).OrderBy(x => x).ToList();
        while (zc.Count > 0)
        {
            int k = rnd.Next(zc.Count); int s = zc[k]; zc.RemoveAt(k);
            if (!UsableLegStart(g, s) || HasEdgeNamed(g, s, a) || HasEdgeNamed(g, s, b)) continue;
            bool clean = true; int beforeM = s;
            for (int c = zToward[s]; c != m; c = zToward[c])
            {
                if (HasEdgeNamed(g, c, a) || HasEdgeNamed(g, c, b)) { clean = false; break; }
                beforeM = c;
            }
            if (!clean) continue;
            // A T: arriving along Z, one arm of A leaves 30 to 150 degrees to the left and the other as
            // much to the right, so both Turn picks name a real turn.
            var bm = g.Nodes[beforeM];
            double arrive = TaxiPlan.Bearing(bm.Latitude, bm.Longitude, mn.Latitude, mn.Longitude);
            var rel = g.Adjacency[m].Where(e => Named(e.TaxiwayName, a)).Select(e => e.ToNodeId).Distinct()
                       .Select(id => Geo.Norm(TaxiPlan.Bearing(mn.Latitude, mn.Longitude, g.Nodes[id].Latitude, g.Nodes[id].Longitude) - arrive))
                       .ToList();
            if (!(rel.Count == 2 && rel.Any(r => r <= -30 && r >= -150) && rel.Any(r => r >= 30 && r <= 150))) continue;
            var sn = g.Nodes[s]; var nx = g.Nodes[zToward[s]];
            // The form's end is a junction on one of M's two arms, and the clearance's own route (no
            // pick) turns onto A at M: a turn the pilot could be told to make either way.
            int dest = FormLegEnd(g, sn.Latitude, sn.Longitude, a, b);
            if (dest != arms[0] && dest != arms[1]) continue;
            var plainWay = new TaxiRouter(g).FindConstrainedPath(s, dest, new List<string> { z, a });
            int atM = plainWay?.Segments.FindIndex(sg => sg.FromNode.NodeId == m) ?? -1;
            if (atM < 0 || !Named(plainWay!.Segments[atM].TaxiwayName, a)) continue;
            return new LegPlan
            {
                Icao = w.Icao, Kind = "turn", A = a, B = b, Z = z, M = m, StartNode = s, Dest = dest, AlongM = zd[s],
                Lat = sn.Latitude, Lon = sn.Longitude, Hdg = TaxiPlan.Bearing(sn.Latitude, sn.Longitude, nx.Latitude, nx.Longitude),
                Seq = new List<string> { z, a },
            };
        }
    }
    return null;
}

/// <summary>Loads one leg as the form does, checks the route, flies it and measures where it ended.</summary>
LegendRow FlyLeg(World w, LegPlan lp, bool trace, List<Finding> fs)
{
    string where = lp.Describe();
    if (trace) Console.WriteLine("=== " + w.Icao + " " + where);
    var g = w.Graph;
    var row = new LegendRow
    {
        Icao = w.Icao, Kind = lp.Kind, A = lp.A, B = lp.B, Z = lp.Z ?? "", Pick = lp.Pick?.ToString() ?? "none",
        StartM = lp.AlongM, FormDest = lp.Dest,
    };
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        mgr.AircraftWingspanMetres = 36;
        string? err = mgr.LoadRoute(provider, w.Icao, lp.Lat, lp.Lon, lp.Hdg, lp.Dest, $"hold short of taxiway {lp.B}",
            new List<string>(lp.Seq), null,
            destinationHeading: null, destinationThresholdLat: null, destinationThresholdLon: null, destinationHeadingTrue: null,
            isRunwayDestination: false, prebuiltGraph: g, announceSummary: true,
            progressiveTerminator: new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, lp.B),
            userTurnPicks: lp.Pick is { } pickSide ? new Dictionary<int, TurnSide> { [1] = pickSide } : null,
            clearedCrossings: lp.ClearRunway is { } clearRwy ? new[] { new ClearedCrossing(clearRwy) } : null);
        string said = ((mgr.LastRouteSummary ?? "") + " " + (StartWarning(mgr) ?? "")).Trim();
        var route = mgr.CurrentRoute;
        if (trace) Console.WriteLine($"LOAD {err ?? "ok"}: {said}");
        row.Runway = lp.ClearRunway ?? ""; row.EdgeToJunctionM = lp.EdgeToJunctionM; row.RunwayHeldAtLoad = lp.RunwayHeldAtLoad;
        if (err != null || route == null || route.Segments.Count == 0)
        {
            row.Outcome = lp.Pick == null ? "" : "LOAD_FAILED";
            row.Note = "refused: " + (err ?? "no route");
            fs.Add(new Finding(w.Icao, "LEG_ROUTE_REFUSED", where, err ?? "no route"));
            return row;
        }

        // What became of the pick, as the summary note says it (the turnpicks mode's reading).
        AppliedTurnPick? applied = null;
        if (lp.Pick is { } side)
        {
            applied = route.TurnPicks.FirstOrDefault(p => p.RowIndex == 1);
            string sideWord = side == TurnSide.Left ? "left" : "right";
            row.Outcome =
                said.Contains($"no {sideWord} turn onto taxiway", StringComparison.OrdinalIgnoreCase) ? "NO_TURN"
                : said.Contains("does not lead to", StringComparison.OrdinalIgnoreCase) ? "NO_LEAD"
                : said.Contains("would be a long detour", StringComparison.OrdinalIgnoreCase) ? "DETOUR"
                : said.Contains($"onto taxiway {lp.A} does not pass", StringComparison.OrdinalIgnoreCase) ? "NO_PIN"
                : applied == null ? "SILENT"
                : applied.IsOffChart ? "OFFCHART" : applied.ChangedRoute ? "CHARTED" : "MET";
            // SILENT: the pick is off the route, refused by nothing. The leg ends before the picked turn (B lies less
            // than the setback past it; owner, 2026-11-25: "Stop before the turn"), so the turn is never made and
            // must never be announced as picked, nor as an off-chart leg.
            if (applied == null
                && (said.Contains($"onto taxiway {lp.A}, as picked", StringComparison.OrdinalIgnoreCase)
                    || said.Contains($"off-chart {sideWord} turn onto taxiway {lp.A}", StringComparison.OrdinalIgnoreCase)))
                fs.Add(new Finding(w.Icao, "LEGEND_DROPPED_PICK_SPOKEN", where, $"the pick is off the route, yet said: {said}"));
        }

        // The route as loaded: the stop placement left every pick's indices and the off-chart leg whole.
        int n = route.Segments.Count;
        foreach (var p in route.TurnPicks)
        {
            bool depOk = p.FirstSegmentAfterDeparture >= 0 && p.FirstSegmentAfterDeparture < n
                         && route.Segments[p.FirstSegmentAfterDeparture].FromNode.NodeId == p.DepartureNodeId;
            // A leg that ends on the pick's own link (no usable node before its turn: the pick is kept, TWH-2) has
            // no segment from the join; its join must then be the route's last node, or lie past it on the link.
            bool endsOnItsLink = p.FirstTaxiwaySegment >= n && depOk;
            bool joinOk = endsOnItsLink || (p.FirstTaxiwaySegment >= 0 && p.FirstTaxiwaySegment < n
                          && route.Segments[p.FirstTaxiwaySegment].FromNode.NodeId == p.JoinNodeId);
            if (!depOk || !joinOk)
                fs.Add(new Finding(w.Icao, "LEGEND_PICK_INDEX", where,
                    $"row {p.RowIndex} onto {p.TaxiwayName}: segment {p.FirstSegmentAfterDeparture} should leave n{p.DepartureNodeId}, "
                    + $"segment {p.FirstTaxiwaySegment} should leave n{p.JoinNodeId} ({n} segments)"));
        }
        for (int k = 0; k + 1 < n; k++)
            if (route.Segments[k].IsOffChartTurn && route.Segments[k + 1].IsOffChartTurn)
            {
                fs.Add(new Finding(w.Icao, "LEGEND_OFFCHART_SPLIT", where, $"segments {k} and {k + 1} of {n} are both off-chart"));
                break;
            }
        // An off-chart leg may end a leg only with its pick kept, so it is announced: the leg end could not stop before
        // that turn (TWH-2). One that ends the route with no such pick is never said.
        if (route.Segments[^1].IsOffChartTurn
            && !route.TurnPicks.Any(p => p.IsOffChart && p.FirstSegmentAfterDeparture <= n - 1 && p.FirstTaxiwaySegment > n - 1))
            fs.Add(new Finding(w.Icao, "LEGEND_ENDS_OFFCHART", where, $"the last of {n} segments is off-chart"));

        int appJunction = AppJunctionOf(mgr);
        if (appJunction < 0)   // the field renamed or gone: the harness's own pick stands in, and says so
            fs.Add(new Finding(w.Icao, "LEG_APP_JUNCTION_UNREAD", where, "TaxiGuidanceManager._destinationNodeId could not be read; measured to the harness's own pick"));
        var ahead = JunctionAhead(g, route, lp.A, lp.B, appJunction);
        row.HarnessJunction = ahead?.harnessNode ?? -1;
        row.RouteM = route.TotalDistanceMeters;
        row.LastSegmentM = route.Segments[^1].DistanceMeters;
        row.LastTaxiway = route.Segments[^1].TaxiwayName ?? "";
        if (ahead is { } ah0) { row.SetbackM = ah0.metres; row.Junction = ah0.node; }
        else fs.Add(new Finding(w.Icao, "LEG_JUNCTION_NOT_FOUND", where, $"no node on {lp.B} within 400 m of the route's end n{route.Segments[^1].ToNode.NodeId}"));

        // The leg stops before the FIRST meeting with B along its own way (from a pick's join, the way
        // picked): the route passes no other node on B before its stop. Judged along the route, not by
        // a walk of the graph, which at a Y-shaped junction goes back up the branch the route left. The
        // whole route is read: a picked route can cross B on the charted link to its turn
        // (LEG_PICK_LINK_CROSSES_B: KDEN "D, D8, right onto D8, hold short of WC"). EVERY node on B is
        // classified, each with its own finding: stopping at the first would let an informational one
        // (on the link, or inside a runway's margin) hide a later breach between the turn and the stop.
        for (int k = 0; k < n; k++)
        {
            var node = route.Segments[k].ToNode;
            if (node.NodeId == (ahead?.node ?? -1) || !HasEdgeNamed(g, node.NodeId, lp.B)) continue;
            bool inMargin = !ClearOfRunwaysAt(g, node.Latitude, node.Longitude);
            bool beforeTheTurn = applied != null && k < applied.FirstTaxiwaySegment;
            string code = inMargin ? "LEG_PASSES_B_IN_RUNWAY_MARGIN"
                : beforeTheTurn ? (applied!.ChangedRoute ? "LEG_PICK_LINK_CROSSES_B" : "LEG_PASSES_B_JUNCTION")
                : applied is { ChangedRoute: true } ? "LEGEND_PICK_NOT_FIRST_JUNCTION" : "LEG_PASSES_B_JUNCTION";
            fs.Add(new Finding(w.Icao, code, where,
                $"segment {k} of {n} reaches n{node.NodeId} on {lp.B}{(inMargin ? ", inside a runway's clear margin" : "")}; "
                + $"the leg stops before n{(ahead?.node ?? -1)}"));
        }
        if (trace)
        {
            Console.WriteLine($"ROUTE {n} segs {route.TotalDistanceMeters:F0} m; junction n{(ahead?.node ?? -1)} (the harness's own pick n{row.HarnessJunction}), stop {(ahead?.metres ?? double.NaN):F1} m before it; form dest n{lp.Dest}");
            for (int k = 0; k < n; k++)
            {
                var sg = route.Segments[k];
                Console.WriteLine($"  seg{k,-3} {sg.TaxiwayName,-8} {sg.DistanceMeters,6:F1} m brg {sg.BearingDegrees,3:F0} n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId}"
                    + $"{(sg.IsOffChartTurn ? " OFFCHART" : "")}{(sg.IsHoldShortPoint ? " HOLD " + sg.HoldShortRunway : "")}");
            }
            foreach (var p in route.TurnPicks)
                Console.WriteLine($"  PICK row {p.RowIndex} {p.Side} onto {p.TaxiwayName}: departure n{p.DepartureNodeId} seg {p.FirstSegmentAfterDeparture}, join n{p.JoinNodeId} seg {p.FirstTaxiwaySegment}, changed={p.ChangedRoute} offchart={p.IsOffChart}");
            // The graph around the stop, two edges out: what the junction measurement walks.
            var near = new List<int> { route.Segments[^1].ToNode.NodeId };
            for (int hop = 0; hop < 2; hop++)
                foreach (int id in near.ToList())
                    if (g.Adjacency.TryGetValue(id, out var es)) near.AddRange(es.Select(e => e.ToNodeId).Where(x => !near.Contains(x)));
            foreach (int id in near.Distinct())
                if (g.Adjacency.TryGetValue(id, out var es))
                    Console.WriteLine($"  NEAR n{id}: {string.Join(", ", es.Select(e => $"{(e.TaxiwayName.Length > 0 ? e.TaxiwayName : "(unnamed)")}->n{e.ToNodeId} {e.DistanceMeters:F0} m"))}");
        }

        // crossthenhold: the crossing as cleared at load, and where the stop for B lies against that runway: the metres
        // along the route from the runway's far pavement edge to the stop (NaN, StopBeforeRunway, when the route never
        // reaches its pavement), the stop clear of every runway's margin, and the 25 m before it clear too.
        XcFlight? xc = null;
        if (lp.ClearRunway is { } crossed && lp.CrossRunway is { } crossR)
        {
            xc = XcFlight.Setup(route, crossed);
            row.XcOutcome = xc.Outcome;
            var shape = RunwayShape.For(crossR);
            var stopNode = route.Segments[^1].ToNode;
            double back = 0, pastEdge = double.NaN;
            for (int k = n - 1; k >= 0 && double.IsNaN(pastEdge); k--)
            {
                var sg = route.Segments[k];
                int steps = Math.Max(1, (int)Math.Ceiling(sg.DistanceMeters / 1.0));
                for (int q = 0; q <= steps; q++)
                {
                    double f = (double)q / steps;   // from the segment's end back to its start
                    double la = sg.ToNode.Latitude + (sg.FromNode.Latitude - sg.ToNode.Latitude) * f;
                    double lo = sg.ToNode.Longitude + (sg.FromNode.Longitude - sg.ToNode.Longitude) * f;
                    if (shape.Contains(la, lo, 0.0)) { pastEdge = back + sg.DistanceMeters * f; break; }
                }
                back += sg.DistanceMeters;
            }
            row.StopPastEdgeM = pastEdge;
            row.StopBeforeRunway = double.IsNaN(pastEdge);
            row.StopClearOfMargin = ClearOfRunwaysAt(g, stopNode.Latitude, stopNode.Longitude);
            bool approachClear = true; double walked = 0;
            for (int k = n - 1; k >= 0 && walked < 25 && approachClear; k--)
            {
                var sg = route.Segments[k];
                for (double m0 = 0; m0 <= sg.DistanceMeters && walked + m0 <= 25; m0 += 1.0)
                {
                    double f = sg.DistanceMeters < 1e-6 ? 0 : m0 / sg.DistanceMeters;
                    if (!ClearOfRunwaysAt(g, sg.ToNode.Latitude + (sg.FromNode.Latitude - sg.ToNode.Latitude) * f,
                                             sg.ToNode.Longitude + (sg.FromNode.Longitude - sg.ToNode.Longitude) * f)) { approachClear = false; break; }
                }
                walked += sg.DistanceMeters;
            }
            row.Approach25Clear = approachClear;
            if (trace)
            {
                Console.WriteLine($"  CLEARED runway {crossed}: {xc.Outcome}, stop n{xc.StopNode?.NodeId} \"{xc.Label}\"; with nothing cleared it {(lp.RunwayHeldAtLoad ? "stopped" : "did not stop")} for it");
                Console.WriteLine($"  STOP for {lp.B} n{stopNode.NodeId}: {(row.StopBeforeRunway ? "before the runway" : $"{pastEdge:F1} m past its far edge")}, clear of every margin {row.StopClearOfMargin}, 25 m approach clear {approachClear}");
            }
        }
        var legPrevState = mgr.State;

        mgr.StartGuidance(settings);
        var cue = mgr.ConsumeStartHoldCue();
        if (trace && cue != null) Console.WriteLine($"  START-HOLD {cue}");
        if (xc?.StopNode != null && mgr.State == TaxiGuidanceState.HoldShort && route.Segments[0].FromNode.NodeId == xc.StopNode.NodeId) row.HeldAtRunway++;
        legPrevState = mgr.State;
        double lat = lp.Lat, lon = lp.Lon, hdg = lp.Hdg, gs = 0, t = 0, holdWait = 0, still = 0;
        var pilot = new PilotModel(hdg, 0);
        bool braking = false;
        double? endT = null; (double lat, double lon) endAt = default;
        double budget = route.TotalDistanceMeters / 3.0 + 240;
        string countdownLead = $"Hold short taxiway {lp.B} in";
        // VP_LEG_BRAKE_ON_SLOWDOWN=1: the pilot brakes on the leg end's own "… Slow down." too, as the guide lets a pilot
        // stop after it ("stop where BA tells you to: you do not need to creep up to the end", STR-25).
        bool brakeOnSlowDown = Environment.GetEnvironmentVariable("VP_LEG_BRAKE_ON_SLOWDOWN") == "1";
        // When the countdown comes to each stop, the leg's end and a cleared crossing included (CountdownTiming:
        // HOLD_COUNTDOWN_LATE, HOLD_COUNTDOWN_MISSING), measured along the route by the harness itself.
        var cdTiming = new CountdownTiming();
        double odo = 0;
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            bool enteredHold = st == TaxiGuidanceState.HoldShort && legPrevState != TaxiGuidanceState.HoldShort;
            bool legEnded = st == TaxiGuidanceState.ProgressiveHold && endT == null;
            if (xc?.StopNode != null)
            {
                // A hold entered at the cleared runway's stop: the stop segment's end node (HoldShort leaves the cursor past it).
                if (enteredHold
                    && mgr.CurrentRoute is { } rNow && mgr.SegmentIndexForHarness is int si && si > 0 && si - 1 < rNow.Segments.Count
                    && rNow.Segments[si - 1].ToNode.NodeId == xc.StopNode.NodeId)
                    row.HeldAtRunway++;
                xc.Track(mgr.CurrentRoute, lat, lon);
            }
            legPrevState = st;
            var saidNow = cap.TakeNew();
            cdTiming.Frame(w.Icao, where, fs, st, mgr.CurrentRoute, mgr.SegmentIndexForHarness, lat, lon, gs, odo, saidNow,
                stopped: enteredHold || legEnded, trace, legEnd: true, cleared: true);
            foreach (var s in saidNow)
            {
                if (trace) Console.WriteLine($"  {t,6:F1}s SPEAK {s}");
                xc?.Hear(s, lat, lon, mgr.CurrentRoute, mgr.SegmentIndexForHarness);
                string said1 = s.TrimEnd();
                if (said1.EndsWith("Stop.", StringComparison.Ordinal) || said1.Contains(" Stop. ", StringComparison.Ordinal)) braking = true;
                if (s.Contains(countdownLead, StringComparison.OrdinalIgnoreCase))
                {
                    if (brakeOnSlowDown && s.Contains("Slow down.", StringComparison.Ordinal)) braking = true;
                    row.Countdown++;
                    row.Tiers += s.Contains("Slow down.", StringComparison.Ordinal) ? "S" : s.Contains("Stop.", StringComparison.Ordinal) ? "X" : "O";
                    // BA never asks for a stop on a runway (spec 8.8): no "Slow down" or "Stop" of the leg end
                    // while the aircraft is on runway pavement.
                    if ((s.Contains("Slow down.", StringComparison.Ordinal) || s.Contains("Stop.", StringComparison.Ordinal))
                        && RouteRunwayCrossings.RunwayUnder(g.RunwayCenterlines, lat, lon) is { } rwySaid)
                        fs.Add(new Finding(w.Icao, "LEG_ENDED_ON_RUNWAY", where,
                            $"\"{s.Trim()}\" spoken on runway {rwySaid.Name1}/{rwySaid.Name2} pavement at {gs:F1} kt ({lat:F6},{lon:F6})"));
                }
                if (s.Contains("Route changed", StringComparison.OrdinalIgnoreCase)) row.Recalcs++;
            }
            if (st == TaxiGuidanceState.ProgressiveHold && endT == null)
            {
                endT = t; endAt = (lat, lon); braking = true;
                // Not yet turned onto the last segment's line: the 25 m capture reached round a corner.
                if (mgr.CurrentRoute is { Segments.Count: > 0 } rEnd)
                    row.EndHeadingOffDeg = Math.Abs(Geo.Norm(hdg - rEnd.Segments[^1].BearingDegrees));
                if (trace) Console.WriteLine($"  {t,6:F1}s LEG ENDED at {gs:F1} kt ({lat:F6},{lon:F6})");
                // A leg never ends while the aircraft is on a runway (spec 8.5).
                if (RouteRunwayCrossings.RunwayUnder(g.RunwayCenterlines, lat, lon) is { } rwyEnd)
                    fs.Add(new Finding(w.Icao, "LEG_ENDED_ON_RUNWAY", where,
                        $"the leg ended on runway {rwyEnd.Name1}/{rwyEnd.Name2} pavement at {gs:F1} kt ({lat:F6},{lon:F6})"));
            }
            if (st == TaxiGuidanceState.HoldShort && gs < 0.3)
            {
                holdWait += 0.1;
                if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; braking = false; }
            }
            if (endT != null && gs < 0.05) break;
            if (st is TaxiGuidanceState.Arrived or TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.Inactive && t > 2) break;
            // Stopped on "Stop." with nothing to end the leg or hold: the pilot would wait for ever.
            if (braking && gs < 0.05 && st == TaxiGuidanceState.Taxiing) { still += 0.1; if (still > 15) break; }
            else still = 0;
            double target = braking || st is TaxiGuidanceState.HoldShort or TaxiGuidanceState.ProgressiveHold ? 0 : pilot.TaxiTarget(pan, cap);
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
            odo += gs * 0.5144 * 0.1;
            if (trace && ((int)Math.Round(t * 10)) % 10 == 0)
                Console.WriteLine($"  {t,6:F1}s {st,-15} seg={mgr.SegmentIndexForHarness} ({lat:F6},{lon:F6}) hdg={hdg:F0} gs={gs:F1} pan={pan:F2}{(braking ? " BRAKING" : "")}");
        }

        // Measured on the route as it stood when the flight ended (a route change re-places the stop).
        var aheadNow = JunctionAhead(g, mgr.CurrentRoute ?? route, lp.A, lp.B, AppJunctionOf(mgr)) ?? ahead;
        row.Ended = endT != null;
        if (endT is double te && aheadNow is { } ah)
        {
            row.Seconds = te;
            row.EndM = SignedMetresToEnd(ah.line, endAt.lat, endAt.lon);
            row.StandM = SignedMetresToEnd(ah.line, lat, lon);
            var jn = g.Nodes[ah.node];
            row.StandStraightM = Geo.Dist(lat, lon, jn.Latitude, jn.Longitude);
            if (row.EndM <= 0 || row.StandM <= 0)
                fs.Add(new Finding(w.Icao, "LEG_AT_OR_PAST_JUNCTION", where, $"ended {row.EndM:F1} m and came to rest {row.StandM:F1} m before the junction n{ah.node}"));
        }
        if (endT == null)
        {
            string at = aheadNow is { } ah2 ? $"{SignedMetresToEnd(ah2.line, lat, lon):F0} m before the junction n{ah2.node}" : "no junction found";
            row.Note = $"not ended after {t:F0} s: {at}, {gs:F1} kt, state {mgr.State}{(braking ? brakeOnSlowDown ? ", braked on Slow down or Stop." : ", braked on Stop." : "")}";
            fs.Add(new Finding(w.Icao, "LEG_NOT_ENDED", where, row.Note));
        }
        if (xc != null)
        {
            row.CrossingLines = xc.Line; row.CrossingEarly = xc.Early; row.ClearedApproach = xc.ApproachBeforeLine;
            row.EndedOnRunway = fs.Count(f => f.Code == "LEG_ENDED_ON_RUNWAY");
            string at = $"runway {xc.Runway} at n{xc.StopNode?.NodeId} (\"{xc.Label}\", {xc.Outcome})";
            if (xc.Outcome is "NOT_APPLIED")
                fs.Add(new Finding(w.Icao, "XC_NOT_APPLIED", where, $"runway {xc.Runway}: the load cleared no crossing"));
            if (xc.Outcome is "CLEARED" or "START" && row.HeldAtRunway > 0 && !xc.Moved)
                fs.Add(new Finding(w.Icao, "XC_HELD_AT_CLEARED_CROSSING", where, $"{at}: entered HoldShort there {row.HeldAtRunway} time(s)"));
            fs.AddRange(XcRecalcFindings(w, where, xc, at));
            if (xc.StopNode != null && xc.Line != 1 && !xc.Moved)
                fs.Add(new Finding(w.Icao, xc.Line == 0 ? (xc.Reached ? "XC_LINE_NOT_SAID" : "XC_STOP_NOT_REACHED") : "XC_LINE_SAID_TWICE", where,
                    $"{at}: {xc.Line} \"Crossing runway {xc.Runway}\" line(s) within 100 m of the stop"));
            if (xc.Outcome == "CLEARED" && xc.Line > 0 && xc.ApproachBeforeLine == 0 && !xc.Moved)
                fs.Add(new Finding(w.Icao, "XC_APPROACH_NOT_SAID", where, $"{at}: the line \"{xc.LineText}\" came with no \"… cleared to cross.\" before it (stop segment {xc.StopSegmentM:F0} m)"));
            if (xc.Early > 0)
                fs.Add(new Finding(w.Icao, "XC_CROSSING_SAID_BEFORE_ITS_SEGMENT", where, $"{at}: \"{xc.EarlyText}\" said before guidance reached the stop's segment, then the line \"{xc.LineText}\""));
            if (row.StopBeforeRunway || !row.StopClearOfMargin)
                fs.Add(new Finding(w.Icao, row.StopBeforeRunway ? "XC_B_STOP_BEFORE_RUNWAY" : "XC_B_STOP_IN_RUNWAY_MARGIN", where,
                    $"the stop for {lp.B} at n{route.Segments[^1].ToNode.NodeId}: {(row.StopBeforeRunway ? "the route ends before runway " + xc.Runway : "inside a runway's clear margin")}"));
            if (trace) Console.WriteLine("  XC " + xc.ResultFields());
        }
        if (trace)
            Console.WriteLine($"  RESULT ended={row.Ended} t={row.Seconds:F1} s end {row.EndM:F1} m standstill {row.StandM:F1} m before the junction; {row.Countdown} countdown line(s)");
    }
    catch (Exception ex)
    {
        row.Note = "exception: " + ex.GetType().Name + ": " + ex.Message;
        fs.Add(new Finding(w.Icao, "LEG_EXCEPTION", where, row.Note));
    }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return row;
}

void LegendSummary(TextWriter? file)
{
    void Line(string s) { Console.WriteLine(s); file?.WriteLine("# " + s); }
    var groups = new (string label, List<LegendRow> rows)[]
    {
        ("without picks", legendRows.Where(r => r.Pick == "none").ToList()),
        ("with picks", legendRows.Where(r => r.Pick != "none").ToList()),
        ("picks that changed the route", legendRows.Where(r => r.Outcome is "CHARTED" or "OFFCHART").ToList()),
    };
    foreach (var (label, rows) in groups)
    {
        if (rows.Count == 0) { Line($"{label}: no legs"); continue; }
        var ended = rows.Where(r => r.Ended).ToList();
        var stand = ended.Select(r => r.StandM).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        var endM = ended.Select(r => r.EndM).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
        Line(string.Create(CultureInfo.InvariantCulture,
            $"{label}: {rows.Count} legs, {ended.Count} ended ({100.0 * ended.Count / rows.Count:F1} %); before the junction at standstill "
            + $"min {(stand.Count > 0 ? stand[0] : double.NaN):F1} m, median {MedianOf(stand):F1} m; when the leg ended min "
            + $"{(endM.Count > 0 ? endM[0] : double.NaN):F1} m, median {MedianOf(endM):F1} m; at or past the junction "
            + $"{ended.Count(r => r.EndM <= 0 || r.StandM <= 0)}"));
    }
}

static double MedianOf(List<double> sorted)
    => sorted.Count == 0 ? double.NaN
     : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

static bool Named(string? name, string taxiway) => string.Equals(name, taxiway, StringComparison.OrdinalIgnoreCase);

static bool HasEdgeNamed(TaxiGraph g, int nodeId, string taxiway)
    => g.Adjacency.TryGetValue(nodeId, out var es) && es.Any(e => Named(e.TaxiwayName, taxiway));

static bool ClearOfRunwaysAt(TaxiGraph g, double lat, double lon)
    => RouteRunwayCrossings.RunwayWithinClearMargin(g.RunwayCenterlines, lat, lon) == null;

/// <summary>A start the form would take as the aircraft's own node: no stand, clear of every runway, nearest to itself.</summary>
static bool UsableLegStart(TaxiGraph g, int id)
{
    if (!g.Nodes.TryGetValue(id, out var n) || n.Type == TaxiNodeType.Parking) return false;
    if (!ClearOfRunwaysAt(g, n.Latitude, n.Longitude)) return false;
    return g.FindNearestNode(n.Latitude, n.Longitude, excludeBridgeOnlyStandStubs: true)?.NodeId == id;
}

/// <summary>The junction the form passes for "hold short of taxiway": the first by taxi distance from the aircraft's node.</summary>
static int FormLegEnd(TaxiGraph g, double lat, double lon, string lastTaxiway, string target)
{
    var sn = g.FindNearestNode(lat, lon, excludeBridgeOnlyStandStubs: true);
    if (sn == null) return -1;
    var dists = new TaxiRouter(g).ComputeGraphDistancesFrom(sn.NodeId);
    return TaxiwayHoldPlacement.NearestByTaxiDistance(
        g.FindTaxiwayIntersectionNodes(g.ResolveTaxiwayName(lastTaxiway), g.ResolveTaxiwayName(target), sn.ComponentId), dists);
}

/// <summary>
/// Metres along one taxiway's own edges from the nearest of <paramref name="sources"/>, never through
/// <paramref name="blocked"/>, and for each node reached the next node toward that source.
/// </summary>
static (Dictionary<int, double> dist, Dictionary<int, int> toward) AlongTaxiway(TaxiGraph g, IEnumerable<int> sources, string taxiway, int blocked, double maxM)
{
    var dist = new Dictionary<int, double>(); var toward = new Dictionary<int, int>();
    var pq = new PriorityQueue<int, double>(); var done = new HashSet<int>();
    foreach (int s in sources) { dist[s] = 0; pq.Enqueue(s, 0); }
    while (pq.TryDequeue(out int cur, out double d))
    {
        if (!done.Add(cur)) continue;
        if (d > maxM) break;
        if (!g.Adjacency.TryGetValue(cur, out var edges)) continue;
        foreach (var e in edges)
        {
            if (e.ToNodeId == blocked || !Named(e.TaxiwayName, taxiway)) continue;
            double nd = d + e.DistanceMeters;
            if (!dist.TryGetValue(e.ToNodeId, out double od) || nd < od) { dist[e.ToNodeId] = nd; toward[e.ToNodeId] = cur; pq.Enqueue(e.ToNodeId, nd); }
        }
    }
    return (dist, toward);
}

/// <summary>
/// The junction a leg holds short of, and how far its stop is from it, never back through the node
/// before the stop. WHICH node: the one the app holds short of (<paramref name="appJunction"/>, its
/// destination node, which a "hold short of taxiway" stop leaves on the junction), read and not
/// worked out, because a stop moved back before a turn sits where the taxiway forks and a walk from it
/// can take the other branch (KLSV "via E, F, hold short of J": the J junction is 12 m along F, the
/// stop 35 m back on E, and the nearest J node along E lies the other way). The harness's own pick
/// (<c>harnessNode</c>: the nearest node on B along A or the last segment's taxiway, else over any
/// edge) is kept beside it as a cross-check, and used when the app's is not reachable. HOW FAR: the
/// shortest way over any edge not on B, as the route the stop was cut from can leave A for a stretch
/// (KORD "via P1, DD, left onto DD": DD, 30 m of P1, then DD into P, which along DD alone is 100 m).
/// With a line from up to 400 m back along the route, through the stop, to the junction, to measure
/// the aircraft on.
/// </summary>
static (int node, double metres, List<(double Lat, double Lon)> line, int harnessNode)? JunctionAhead(TaxiGraph g, TaxiRoute route, string a, string b, int appJunction)
{
    if (route.Segments.Count == 0) return null;
    var last = route.Segments[^1];
    var stop = last.ToNode; int before = last.FromNode.NodeId;
    var line = new List<(double Lat, double Lon)> { (stop.Latitude, stop.Longitude) };
    double back = 0;
    for (int k = route.Segments.Count - 1; k >= 0 && back < 400; k--)
    {
        var fn = route.Segments[k].FromNode;
        line.Add((fn.Latitude, fn.Longitude));
        back += route.Segments[k].DistanceMeters;
    }
    line.Reverse();
    string own = last.TaxiwayName ?? "";
    var (cleared, _, _) = WalkFromStop(g, stop.NodeId, before, b, e => Named(e.TaxiwayName, a) || Named(e.TaxiwayName, own));
    var (nearest, dist, from) = WalkFromStop(g, stop.NodeId, before, b, e => !Named(e.TaxiwayName, b));
    int harnessNode = HasEdgeNamed(g, stop.NodeId, b) ? stop.NodeId : cleared >= 0 && dist.ContainsKey(cleared) ? cleared : nearest;
    int j = appJunction >= 0 && dist.ContainsKey(appJunction) ? appJunction : harnessNode;
    if (j < 0) return null;
    var path = new List<int>();
    for (int c = j; c != stop.NodeId; c = from[c]) path.Add(c);
    path.Reverse();
    foreach (int id in path) line.Add((g.Nodes[id].Latitude, g.Nodes[id].Longitude));
    return (j, dist[j], line, harnessNode);
}

/// <summary>The node the app routes to (<c>_destinationNodeId</c>), read by reflection: for a "hold short of taxiway" leg, the junction its stop is set back from. -1 when it cannot be read.</summary>
static int AppJunctionOf(TaxiGuidanceManager m)
    => typeof(TaxiGuidanceManager).GetField("_destinationNodeId", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(m) is int id ? id : -1;

/// <summary>Shortest ways from a stop over the edges <paramref name="use"/> allows, never into <paramref name="before"/>, up to 400 m; with the nearest node that has an edge on B (-1 when none).</summary>
static (int nearestOnB, Dictionary<int, double> dist, Dictionary<int, int> from) WalkFromStop(TaxiGraph g, int stop, int before, string b, Func<TaxiEdge, bool> use)
{
    var dist = new Dictionary<int, double> { [stop] = 0 }; var from = new Dictionary<int, int>();
    var pq = new PriorityQueue<int, double>(); var done = new HashSet<int>();
    int nearest = -1;
    pq.Enqueue(stop, 0);
    while (pq.TryDequeue(out int cur, out double d))
    {
        if (!done.Add(cur)) continue;
        if (d > 400) break;
        if (nearest < 0 && cur != stop && HasEdgeNamed(g, cur, b)) nearest = cur;
        if (!g.Adjacency.TryGetValue(cur, out var edges)) continue;
        foreach (var e in edges)
        {
            if (e.ToNodeId == before || !use(e)) continue;
            double nd = d + e.DistanceMeters;
            if (!dist.TryGetValue(e.ToNodeId, out double od) || nd < od) { dist[e.ToNodeId] = nd; from[e.ToNodeId] = cur; pq.Enqueue(e.ToNodeId, nd); }
        }
    }
    return (nearest, dist, from);
}

/// <summary>Metres from a point to the END of a line, along it: negative once past the end (and more than the line's length before its start).</summary>
static double SignedMetresToEnd(List<(double Lat, double Lon)> q, double lat, double lon)
{
    double kx = 111320.0 * Math.Cos(lat * Math.PI / 180), ky = 110540.0;
    var len = new double[Math.Max(0, q.Count - 1)];
    double total = 0;
    for (int i = 0; i + 1 < q.Count; i++)
    {
        len[i] = Math.Sqrt(Math.Pow((q[i + 1].Lon - q[i].Lon) * kx, 2) + Math.Pow((q[i + 1].Lat - q[i].Lat) * ky, 2));
        total += len[i];
    }
    if (q.Count < 2) return q.Count == 1 ? -Math.Sqrt(Math.Pow((q[0].Lon - lon) * kx, 2) + Math.Pow((q[0].Lat - lat) * ky, 2)) : double.NaN;
    double best = double.MaxValue, along = 0, cum = 0;
    for (int i = 0; i + 1 < q.Count; i++)
    {
        double ax = (q[i].Lon - lon) * kx, ay = (q[i].Lat - lat) * ky;
        double dx = (q[i + 1].Lon - q[i].Lon) * kx, dy = (q[i + 1].Lat - q[i].Lat) * ky, l2 = dx * dx + dy * dy;
        double tr = l2 < 1e-9 ? 0 : -(ax * dx + ay * dy) / l2, tc = Math.Clamp(tr, 0, 1);
        double dd = Math.Sqrt(Math.Pow(ax + tc * dx, 2) + Math.Pow(ay + tc * dy, 2));
        if (dd < best - 1e-9)
        {
            best = dd;
            // Past either end of the whole line, measure on along the end segment's own direction.
            double tt = (i == q.Count - 2 && tr > 1) || (i == 0 && tr < 0) ? tr : tc;
            along = cum + tt * len[i];
        }
        cum += len[i];
    }
    return total - along;
}

// ── Ground traffic (GroundTrafficMonitor driven headlessly through IGroundTrafficSimSource) ──
// Simulated AI aircraft placed around real taxi routes; the monitor is wired exactly as MainForm
// wires it (SuppressCheck, RouteContextProvider = the manager's GetGroundTrafficContext) and ticked
// once per simulated second, as its 1 s timer does in the app.
void RunTraffic(World w)
{
    foreach (var (plan, _) in PlanTaxis(w))
        foreach (var sc in new[] { "beside", "onroute", "queue", "runway", "runway_empty" })
        {
            if ((sc == "queue" || sc.StartsWith("runway")) && plan.DestRunway == null) continue;
            taxis++;
            Codes("TRAFFIC_" + sc.ToUpperInvariant(), w.Icao, plan.Describe, FlyTraffic(w, plan, sc, trace: false));
        }
}

List<Finding> FlyTraffic(World w, TaxiPlan plan, string sc, bool trace)
{
    var found = new List<Finding>();
    string where = $"{sc} {plan.Describe}";
    var cap = new Cap(() => simNow);
    var tcap = new TrafficCap();
    var mgr = new TaxiGuidanceManager(cap);
    var src = new FakeTrafficSource();
    GroundTrafficMonitor? mon = null;
    try
    {
        string? err = plan.Load(mgr, provider, w, null);
        var route = mgr.RouteForHarness;
        if (err != null || route == null || route.Segments.Count < 3) return found;

        var line = new RouteLine(route);
        double total = line.Total;
        var tr = new List<SimTraffic>();
        var pool = SimTraffic.Pool();
        int pick = 0;
        SimTraffic Place(double d, double lateralM, string role)
        {
            var (la, lo, b) = line.At(d);
            if (lateralM != 0) (la, lo) = Geo.Move(la, lo, b + 90, lateralM);
            var (cs, al, ty) = pool[pick++ % pool.Length];
            var t0 = new SimTraffic { Id = (uint)(100 + tr.Count), Lat = la, Lon = lo, Heading = b,
                                      Callsign = cs, Airline = al, Type = ty, Role = role, RouteD = d };
            t0.Spoken = GroundTrafficLogic.SpokenName(al, cs, ty);
            tr.Add(t0); return t0;
        }

        switch (sc)
        {
            case "beside":
                if (total < 400) return found;
                foreach (double f in new[] { 0.3, 0.55, 0.8 })
                {
                    var (la, lo, b) = line.At(total * f);
                    // 100 m: clearly beside, past both NEAR_ROUTE (60 m) and "very close" (250 ft).
                    var (pla, plo) = Geo.Move(la, lo, b + 90, 100);
                    if (line.DistanceTo(pla, plo) < 90) continue;   // the route curves back near it
                    Place(total * f, 100, "beside");
                }
                if (tr.Count == 0) return found;
                break;
            case "onroute":
                if (total < 500) return found;
                Place(total * 0.55, 0, "onroute").OnRoute = true;
                break;
            case "queue":
                if (total < 450) return found;
                foreach (double back in new[] { 35.0, 105.0, 175.0 }) Place(total - back, 0, "queue").OnRoute = true;
                break;
            case "runway":
                if (plan.Rwy == null) return found;
                {
                    var r = plan.Rwy;
                    var (rla, rlo) = Geo.Move(r.StartLat, r.StartLon, r.Heading, 900);
                    var (cs, al, ty) = pool[pick++ % pool.Length];
                    tr.Add(new SimTraffic { Id = 200, Lat = rla, Lon = rlo, Heading = r.Heading, Gs = 60, Callsign = cs, Airline = al, Type = ty,
                                            Role = "rolling", Spoken = GroundTrafficLogic.SpokenName(al, cs, ty), Straight = true, Dormant = true });
                    var (fla, flo) = Geo.Move(r.StartLat, r.StartLon, r.Heading + 180, 4 * 1852);
                    (cs, al, ty) = pool[pick++ % pool.Length];
                    tr.Add(new SimTraffic { Id = 201, Lat = fla, Lon = flo, Heading = r.Heading, Gs = 140, AltFt = 1300, OnGround = false,
                                            Callsign = cs, Airline = al, Type = ty, Role = "final", Spoken = GroundTrafficLogic.SpokenName(al, cs, ty), Straight = true, Dormant = true });
                }
                break;
            case "runway_empty":
                break;
            default:
                return found;
        }

        mon = new GroundTrafficMonitor(tcap, src, startTimers: false);
        mon.SuppressCheck = () => mgr.State == TaxiGuidanceState.Inactive || mgr.State == TaxiGuidanceState.LandingRollout;
        mon.RouteContextProvider = () => mgr.GetGroundTrafficContext();
        src.Traffic = tr;

        mgr.StartGuidance(settings);
        mgr.ConsumeStartHoldCue();
        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0;
        var pilot = new PilotModel(hdg, 0);
        double t = 0, holdWait = 0, stoppedBehind = 0;
        double budget = total / 3.0 + 420;
        int step = 0;
        bool atDestHold = false; double destHoldSince = -1;
        var releaseAt = new Dictionary<uint, double>();
        var expectMovingBy = new Dictionary<uint, double>();   // id -> time by which "ahead is moving" is due
        var lastNode = route.Segments[^1].ToNode;
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1; step++;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            if (st is TaxiGuidanceState.Arrived or TaxiGuidanceState.LiningUp) break;
            if (st == TaxiGuidanceState.Inactive && t > 2) break;
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            cap.TakeNew();

            bool destHold = st == TaxiGuidanceState.HoldShort && plan.DestRunway != null
                            && Geo.Dist(lat, lon, lastNode.Latitude, lastNode.Longitude) < 60;
            if (destHold && !atDestHold)
            {
                atDestHold = true; destHoldSince = t;
                foreach (var a in tr) a.Dormant = false;   // runway traffic appears as the pilot arrives at the hold
            }
            if (st == TaxiGuidanceState.HoldShort && gs < 0.3)
            {
                holdWait += 0.1;
                if (holdWait > (destHold ? 30 : 2)) { mgr.ContinuePastHoldShort(); holdWait = 0; }
            }

            // The pilot stops behind ground traffic directly ahead.
            SimTraffic? ahead = null; double aheadD = double.MaxValue;
            foreach (var a in tr)
            {
                if (!a.OnGround || a.Dormant) continue;
                double d = Geo.Dist(lat, lon, a.Lat, a.Lon);
                double rel = Math.Abs(Geo.Norm(Bearing2(lat, lon, a.Lat, a.Lon) - hdg));
                if (d < 75 && rel < 25 && d < aheadD) { ahead = a; aheadD = d; }
            }
            double target = st == TaxiGuidanceState.HoldShort ? 0 : pilot.TaxiTarget(pan, cap);
            if (ahead != null) target = 0;
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
            stoppedBehind = ahead != null && gs < 0.5 ? stoppedBehind + 0.1 : 0;

            // Scripted departures of the traffic ahead.
            if (sc == "onroute" && stoppedBehind > 15 && tr[0].MovingSince < 0) Release(tr[0]);
            if (sc == "queue" && stoppedBehind > 20 && releaseAt.Count == 0)
                for (int k = 0; k < tr.Count; k++) releaseAt[tr[k].Id] = t + 10 * k;
            foreach (var a in tr)
                if (a.MovingSince < 0 && releaseAt.TryGetValue(a.Id, out double rt) && t >= rt) Release(a);
            void Release(SimTraffic a)
            {
                a.MovingSince = t; a.Gs = 20;   // faster than the pilot's 15 kt: pulling away, never being caught
                if (ahead == a && gs < 0.5) expectMovingBy[a.Id] = t + 10;
            }

            if (step % 10 != 0) continue;
            // ── one simulated second: move traffic, feed the monitor, tick it ──
            foreach (var a in tr)
            {
                if (a.Gs <= 0 || a.Dormant) continue;
                double m = a.Gs * 0.5144;
                if (a.Straight || a.RouteD >= total) { if (a.PastEndAt < 0) a.PastEndAt = t; (a.Lat, a.Lon) = Geo.Move(a.Lat, a.Lon, a.Heading, m); a.RouteD += m; }
                else { a.RouteD += m; var (la2, lo2, b2) = line.At(a.RouteD); a.Lat = la2; a.Lon = lo2; a.Heading = b2; }
                if (!a.OnGround) a.AltFt = Math.Max(0, a.AltFt - 12);
            }
            src.Position = new MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition
                { Latitude = lat, Longitude = lon, HeadingMagnetic = hdg, GroundSpeedKnots = gs, SimOnGround = 1 };
            tcap.Begin(t, st);
            mon.TickForHarness();
            src.CompleteSweep();
            if (tcap.ImmediateThisTick > 1)
                found.Add(new Finding(w.Icao, "TRAFFIC_MULTI_IMMEDIATE", where, $"{tcap.ImmediateThisTick} interrupting callouts in one poll at t={t:F0}s: {string.Join(" | ", tcap.ThisTick)}"));
            if (trace) foreach (var m in tcap.ThisTick) Console.WriteLine($"  {t,6:F1}s TRAFFIC {m}   [{st} gs={gs:F1}]");
            if (trace && Environment.GetEnvironmentVariable("VP_TRDBG") == "1") foreach (var a in tr.Where(z => !z.Dormant)) Console.WriteLine($"  {t,6:F1}s DBG {a.Spoken} gs={a.Gs:F0} hdg={a.Heading:F0} routeD={a.RouteD:F0}/{total:F0} dist={Geo.Dist(lat, lon, a.Lat, a.Lon):F0}m pilotHdg={hdg:F0} pilotGs={gs:F1}");
        }

        // ── checks ──
        var said = tcap.All;
        string AllText() => string.Join(" | ", said.Select(x => x.Text));
        foreach (var x in said)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(x.Text, @"\bclear\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                found.Add(new Finding(w.Icao, "TRAFFIC_SAID_CLEAR", where, x.Text));
            foreach (var a in tr)
                if (x.Text.Contains(a.Callsign, StringComparison.OrdinalIgnoreCase) && !a.Spoken.Contains(a.Callsign, StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TRAFFIC_RAW_CALLSIGN", where, x.Text));
            bool generic = x.Text.StartsWith("Number ") || x.Text.StartsWith("First in") || x.Text.StartsWith("Move up")
                           || x.Text.StartsWith("Runway ") || x.Text.Contains("no traffic seen", StringComparison.OrdinalIgnoreCase);
            if (!generic && !tr.Any(a => x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_NAME_UNKNOWN", where, x.Text));
            if (x.Text.StartsWith("Move up") && x.Holding)
                found.Add(new Finding(w.Icao, "TRAFFIC_MOVE_UP_WHILE_HELD", where, $"t={x.T:F0}s {x.Text}"));
        }
        if (sc == "beside")
            foreach (var x in said)
                if (x.Text.StartsWith("Stop,") || x.Text.StartsWith("Slow down") || x.Text.Contains("on your route"))
                    found.Add(new Finding(w.Icao, "TRAFFIC_FALSE_ALARM_BESIDE", where, $"t={x.T:F0}s {x.Text}"));
        if (sc == "onroute" && !said.Any(x => x.Text.Contains("on your route") && x.Text.Contains(tr[0].Spoken, StringComparison.OrdinalIgnoreCase)))
            found.Add(new Finding(w.Icao, "TRAFFIC_ON_ROUTE_NOT_SAID", where, AllText()));
        foreach (var (id, by) in expectMovingBy)
        {
            var a = tr.First(z => z.Id == id);
            if (!said.Any(x => x.T <= by + 1 && x.T >= a.MovingSince && x.Text.Contains("ahead is moving") && x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_AHEAD_MOVING_NOT_SAID", where, $"{a.Spoken} left at t={a.MovingSince:F0}s; said: {AllText()}"));
        }
        // Traffic queued BEHIND the first aircraft on the route must not be called separately.
        if (sc == "queue")
        {
            double firstRelease = tr.Where(a => a.MovingSince >= 0).Select(a => a.MovingSince).DefaultIfEmpty(double.MaxValue).Min();
            var named = said.Where(x => x.T < firstRelease && (x.Text.Contains("on your route") || x.Text.StartsWith("Slow down")))
                            .Select(x => tr.FirstOrDefault(a => x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase))?.Id)
                            .Where(id => id != null).Distinct().Count();
            if (named > 1)
                found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_EACH_CALLED", where, $"{named} queued aircraft called separately: {AllText()}"));
        }
        // "Stop"/"Slow down" about an aircraft that has already started pulling away.
        foreach (var x in said)
            if (x.Text.StartsWith("Stop,") || x.Text.StartsWith("Slow down"))
                foreach (var a in tr)
                    if (a.MovingSince >= 0 && x.T >= a.MovingSince + 3 && !a.Straight
                        && x.Text.Contains(a.Spoken, StringComparison.OrdinalIgnoreCase))
                        found.Add(new Finding(w.Icao, "TRAFFIC_STOP_FOR_DEPARTING", where, $"t={x.T:F0}s {x.Text} ({a.Spoken} moving since t={a.MovingSince:F0}s){(a.PastEndAt >= 0 && a.PastEndAt <= x.T ? " PASTEND" : "")}"));
        if (sc == "queue")
        {
            var q = said.FirstOrDefault(x => x.Text.StartsWith("Number ") || x.Text.StartsWith("First in"));
            if (q == null) found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_NOT_SAID", where, AllText()));
            else if (!q.Text.StartsWith("Number 4 in the departure queue"))
                found.Add(new Finding(w.Icao, "TRAFFIC_QUEUE_WRONG", where, $"expected Number 4 in the departure queue, said \"{q.Text}\""));
        }
        if (sc.StartsWith("runway") && atDestHold && plan.DestRunway is { } dr)
        {
            var afterHold = said.Where(x => x.T >= destHoldSince).ToList();
            string st2 = string.Join(" | ", afterHold.Select(x => x.Text));
            if (!afterHold.Any(x => x.Text.StartsWith("Runway ")))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_WATCH_SILENT", where, $"held at runway {dr} {t - destHoldSince:F0}s; said: {AllText()}"));
            else if (sc == "runway")
            {
                if (!st2.Contains(tr[0].Spoken, StringComparison.OrdinalIgnoreCase)) found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_OCCUPANT_MISSED", where, st2));
                if (!st2.Contains("final", StringComparison.OrdinalIgnoreCase) || !st2.Contains(tr[1].Spoken, StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_FINAL_MISSED", where, st2));
            }
            else if (!afterHold.Any(x => x.Text.Contains("no traffic seen", StringComparison.OrdinalIgnoreCase)))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_EMPTY_WRONG", where, st2));
            if (!afterHold.Where(x => x.Text.StartsWith("Runway ")).Any(x => x.Text.Contains(dr, StringComparison.OrdinalIgnoreCase)
                    || (plan.Rwy != null && x.Text.Contains(ReciprocalOf(dr), StringComparison.OrdinalIgnoreCase)))
                && afterHold.Any(x => x.Text.StartsWith("Runway ")))
                found.Add(new Finding(w.Icao, "TRAFFIC_RUNWAY_WATCH_WRONG_RUNWAY", where, $"holding for {dr}; said: {st2}"));
        }
        if (trace) Console.WriteLine($"ROUTE {total:F0} m, traffic {tr.Count}, said {said.Count}");
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "TRAFFIC_EXCEPTION", where, ex.ToString().Split('\n')[0])); }
    finally { try { mon?.Dispose(); mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}
static double Bearing2(double la1, double lo1, double la2, double lo2)
    => (Math.Atan2((lo2 - lo1) * Math.Cos(la1 * Math.PI / 180), la2 - la1) * 180 / Math.PI + 360) % 360;
static string ReciprocalOf(string rwy)
{
    var m = System.Text.RegularExpressions.Regex.Match(rwy, @"^(\d{1,2})([LCR]?)$");
    if (!m.Success) return rwy;
    int n = int.Parse(m.Groups[1].Value); int r = n > 18 ? n - 18 : n + 18;
    string side = m.Groups[2].Value switch { "L" => "R", "R" => "L", var x => x };
    return r.ToString("00") + side;
}
#endif

void RunTrace(string spec)
{
    var parts = spec.Split(':');
    PrimeOsm(parts[0].ToUpperInvariant());
    var w = World.Load(provider, parts[0].ToUpperInvariant());
    if (w == null) { Console.WriteLine("airport not found"); return; }
    if (parts.Length >= 3 && parts[1].Equals("wrongturn", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:wrongturn:<text in the scenario description>
        depTraceFilter = string.Join(":", parts.Skip(2));
        foreach (var f in RunWrongTurnsCollect(w)) Console.WriteLine("FINDING " + f);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
#if !OLDAPP
    if (parts.Length >= 4 && parts[1].Equals("traffic", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:traffic:<scenario>:<text in the plan description>
        foreach (var (tp, _) in PlanTaxis(w))
            if (tp.Describe.Contains(string.Join(":", parts.Skip(3)), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var f in FlyTraffic(w, tp, parts[2], trace: true)) Console.WriteLine("FINDING " + f);
                if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
                break;
            }
        return;
    }
    if (parts.Length >= 5 && parts[1].Equals("pick", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:pick:<lat>,<lon>,<heading>:<runway>:<clearance>[:miss]
        //   one clearance from a position to a runway; a Turn pick is =L or =R after its taxiway,
        //   e.g. KBOS:pick:42.363953,-71.006981,170:33L:H,C=L   (":miss": the pilot takes the charted
        //   way at the junction instead of the picked turn). ATC's "hold short of runway X" after a
        //   taxiway is @X, e.g. OMDB:pick:25.251047,55.363293,120:30R:K,K7@30L: the pick is loaded as
        //   the form's row pick, and the clearance is voiced through the SayIntentions parser too.
        var pos = parts[2].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var rwy = w.Runways.First(r => r.RunwayID.Equals(parts[3], StringComparison.OrdinalIgnoreCase));
        var seq = new List<string>(); var picks = new Dictionary<int, TurnSide>(); var holds = new Dictionary<int, string>();
        foreach (var item in parts[4].Split(','))
        {
            var at = item.Split('@');
            var kv = at[0].Split('=');
            if (kv.Length > 1) picks[seq.Count] = kv[1].StartsWith("L", StringComparison.OrdinalIgnoreCase) ? TurnSide.Left : TurnSide.Right;
            if (at.Length > 1) holds[seq.Count] = at[1].Trim();
            seq.Add(kv[0].Trim());
        }
        var tp = TaxiPlan.GateToRunway(w, new ParkingSpot { Latitude = pos[0], Longitude = pos[1] }, rwy, pos[2]);
        if (tp == null) { Console.WriteLine("no entry onto that runway found"); return; }
        tp.Describe = $"{parts[2]} -> Runway {rwy.RunwayID}"; tp.FixedSeq = seq; tp.TurnPicks = null;
        if (holds.Count > 0)
        {
            tp.HoldPicks = holds;
            foreach (var f in SpokenClearance.Check(w, tp.Describe, seq, holds, rwy.RunwayID)) Console.WriteLine("FINDING " + f);
        }
        if (parts.Length > 5 && parts[5].Equals("miss", StringComparison.OrdinalIgnoreCase) && picks.Count > 0)
        {
            var charted = LoadProbe(w, tp, seq, out _, out _);
            tp.TurnPicks = picks;
            var picked = LoadProbe(w, tp, seq, out _, out _);
            var changed = picked?.TurnPicks.FirstOrDefault(p => p.ChangedRoute);
            if (charted == null || picked == null || changed == null || ChartedWayAt(picked, charted, changed) is not { } dv)
            { Console.WriteLine("no pick changes this route, or the charted way does not part from it: nothing to miss"); return; }
            tp.DivertPath = dv.path; tp.DivertAt = dv.at;
        }
        tp.TurnPicks = picks.Count > 0 ? picks : null;
        foreach (var f in FlyTaxi(w, tp, withClearance: false, trace: true)) Console.WriteLine("FINDING " + f);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("missturn", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:missturn:<text in the scenario description>  (e.g. "before the hold n2753")
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunMissedHoldTurns(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("turn", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:turn:<text in the scenario description>  (e.g. "turn Left onto C")
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunTurnPicks(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("legend", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:legend:<text in the leg's description>  (e.g. "via A hold short of B", "turn Left onto A")
        //   every leg of the airport is flown as the run flies it (a stop can insert a node into the shared
        //   graph, so a later leg depends on the earlier ones); only the matching ones are printed
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunLegEnds(w);
        Console.WriteLine(LegendRow.Header);
        foreach (var r in legendRows) Console.WriteLine(r.Csv());
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("crosshold", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:crosshold:<text in the leg's description>  (e.g. "via P cross runway 24L hold short of L")
        //   every crossthenhold leg of the airport is flown as the run flies it; only the matching ones are printed
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunCrossThenHold(w);
        Console.WriteLine(LegendRow.CrossHeader);
        foreach (var r in legendRows) Console.WriteLine(r.CsvCross());
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
#endif
    if (parts.Length >= 3 && parts[1].Equals("any", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:any:<text in the scenario description> — every taxi and departure scenario
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunTaxis(w); RunDepartures(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("rwystop", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:rwystop:<text in the scenario description>
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunRunwayStops(w);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("missedexit", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:missedexit:<text in the scenario description>
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunMissedExits(w);
        return;
    }
    if (parts.Length >= 4 && parts[1].Equals("from", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:from:<lat>,<lon>,<heading>:<text in the gate's name>[:A,B,C]
        // A route loaded where a real flight loaded it, with the clearance it had (from the taxi log).
        var pos = parts[2].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var seq = parts.Length > 4 ? parts[4].Split(',').ToList() : null;
        var gate = w.Parking.FirstOrDefault(p => p.ToString().Contains(parts[3], StringComparison.OrdinalIgnoreCase)
                                                 && w.NodeNear(p.Latitude, p.Longitude, 100) != null);
        if (gate == null) { Console.WriteLine($"no gate matching \"{parts[3]}\""); return; }
        foreach (var f in FlyTaxi(w, TaxiPlan.FromPosition(w, pos[0], pos[1], pos[2], gate, seq), withClearance: false, trace: true))
            Console.WriteLine("FINDING " + f);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("dep", StringComparison.OrdinalIgnoreCase))
    {
        // --trace ICAO:dep:<text in the scenario description>  (e.g. "HP A1 -> Runway 27R")
        depTraceFilter = string.Join(":", parts.Skip(2));
        RunDepartures(w);
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    if (parts.Length >= 3 && parts[1].Equals("taxi", StringComparison.OrdinalIgnoreCase))
    {
        int n = int.Parse(parts[2]);
        var plans = PlanTaxis(w);
        TaxiPlan? plan = n < plans.Count ? plans[n].plan : null;
        Console.WriteLine($"{plans.Count} plans; #{n}: {plan?.Describe}");
        if (plan == null) { Console.WriteLine("no plan"); return; }
        bool clr = parts.Length > 3 && parts[3] == "clearance";
#if !OLDAPP
        if (opt.ClearFirstCrossing)
        {
            // The flight with nothing cleared first, untraced, to compare the cleared one's other stops with.
            FlyTaxi(w, plan, clr, trace: false);
            plan.XcBaseHoldNodes = lastFlightHoldNodes; plan.ClearFirstCrossing = true;
        }
#endif
        foreach (var f in FlyTaxi(w, plan, clr, trace: true)) Console.WriteLine("FINDING " + f);
#if !OLDAPP
        if (lastFlightXc is { } xcT) Console.WriteLine("XC " + xcT.ResultFields());
#endif
        if (Environment.GetEnvironmentVariable("VP_ALLLOG") == "1") foreach (var l in logSink) Console.WriteLine("LOG " + l);
        return;
    }
    var rw = w.Runways.First(r => r.RunwayID.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
    var ex = w.Graph.GetLandingExits(rw);
#if !MAINAPP
    foreach (var e0 in ex)
        e0.RequiresTurnBack = LandingExitDestination.RequiresTurnBack(w.Graph, e0, ex, rw, rw.Heading);
#endif
    // EXIT is a taxiway name or "n<nodeId>" (names repeat: KMSP 04 has three B junctions).
    var e = parts[2].StartsWith("n") && int.TryParse(parts[2][1..], out int nid)
        ? ex.First(x => x.NodeId == nid)
        : ex.First(x => x.TaxiwayName.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
    {
        int d0 = LandingExitDestination.Resolve(w.Graph, e, ex, rw, rw.Heading, out _, out _, out string src);
#if MAINAPP
        int d1 = d0;
        Console.WriteLine($"EXIT n{e.NodeId} dest={d0}({src})");
#else
        int d1 = LandingExitDestination.CorrectBackwardsStart(w.Graph, e, rw, rw.Heading, d0);
        Console.WriteLine($"EXIT n{e.NodeId} turnBack={e.RequiresTurnBack} dest={d0}({src}) corrected={d1}");
#endif
        var pr = new TaxiRouter(w.Graph).FindShortestPath(e.NodeId, d1);
        if (pr != null) foreach (var sg in pr.Segments.Take(8))
            Console.WriteLine($"   path n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {sg.TaxiwayName} {sg.DistanceMeters:F0} m brg={sg.BearingDegrees:F0} to={new LandingGeometry(rw).Describe(sg.ToNode.Latitude, sg.ToNode.Longitude, new LandingGeometry(rw).Along(e.Latitude, e.Longitude))}");
    }
    bool straight = parts.Length > 3 && parts[3] == "straight";
    foreach (var f in FlyLanding(w, rw, ex, e, straight, trace: true)) Console.WriteLine("FINDING " + f);
    foreach (var l in logSink.Where(l => Environment.GetEnvironmentVariable("VP_ALLLOG") == "1" || l.Contains("Exit path") || l.Contains("OVERSHOOT") || l.Contains("HANDOFF"))) Console.WriteLine("LOG " + l);
}


// ── Online taxi data snapshot ───────────────────────────────────────────────
// The app's two online sources, in its own order (MainForm: OsmTaxiSource, then XplaneAptDatSource):
// runs/_osm/ICAO.json (the raw Overpass answer) and runs/_osm/ICAO.apt.dat (the X-Plane Gateway's
// recommended scenery for the airport). A snapshot without the .apt.dat flies OSM alone, as before.
void PrimeOsm(string icao)
{
    if (!useOsm) return;
    var sources = new List<MSFSBlindAssist.Services.TaxiAugment.AirportTaxiData>();
    string f = Path.Combine(osmDir, icao + ".json");
    if (File.Exists(f))
        try { sources.Add(MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.Parse(File.ReadAllText(f))); }
        catch (Exception ex) { Console.WriteLine($"  OSM parse failed {icao}: {ex.Message}"); }
    string a = Path.Combine(osmDir, icao + ".apt.dat");
    if (File.Exists(a))
        try { sources.Add(MSFSBlindAssist.Services.TaxiAugment.AptDatParser.Parse(File.ReadAllText(a))); }
        catch (Exception ex) { Console.WriteLine($"  apt.dat parse failed {icao}: {ex.Message}"); }
    if (sources.Count > 0) osmCache.Save(icao, sources);
}

// The X-Plane Gateway's apt.dat text for an airport, fetched as XplaneAptDatSource fetches it; null when
// the Gateway has none.
async Task<string?> FetchAptDat(HttpClient http, string icao)
{
    const string api = "https://gateway.x-plane.com/apiv1";
    try
    {
        using var airport = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"{api}/airport/{icao}"));
        if (!airport.RootElement.TryGetProperty("airport", out var ap) || !ap.TryGetProperty("recommendedSceneryId", out var id)) return null;
        using var scenery = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"{api}/scenery/{id.GetInt64()}"));
        if (!scenery.RootElement.TryGetProperty("scenery", out var sc) || !sc.TryGetProperty("masterZipBlob", out var blob)) return null;
        string? b64 = blob.GetString();
        if (string.IsNullOrEmpty(b64)) return null;
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(Convert.FromBase64String(b64)));
        var dat = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
        if (dat == null) return null;
        using var reader = new StreamReader(dat.Open());
        return await reader.ReadToEndAsync();
    }
    catch (Exception ex) { Console.WriteLine($"  {icao}: apt.dat {ex.GetType().Name}"); return null; }
}

async Task FetchOsm()
{
    Directory.CreateDirectory(osmDir);
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MSFSBlindAssist/1.0 (VirtualPilot snapshot)");
    string[] mirrors = { "https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
        "https://overpass.private.coffee/api/interpreter", "https://z.overpass-api.de/api/interpreter" };
    int mi = 0;
    foreach (var icao in airports)
    {
        string fa = Path.Combine(osmDir, icao + ".apt.dat");
        if (!File.Exists(fa) && await FetchAptDat(http, icao) is { } text)
        {
            File.WriteAllText(fa, text);
            Console.WriteLine($"  {icao}: apt.dat, {text.Length / 1024} KB");
        }
        string f = Path.Combine(osmDir, icao + ".json");
        if (File.Exists(f)) continue;
        var ap = baseProvider.GetAirport(icao);
        if (ap == null) continue;
        string q = MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.BuildQuery(ap.Latitude, ap.Longitude);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string url = mirrors[mi % mirrors.Length];
            try
            {
                using var resp = await http.PostAsync(url, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", q) }));
                string body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode && body.TrimStart().StartsWith("{"))
                {
                    var d = MSFSBlindAssist.Services.TaxiAugment.OsmTaxiSource.Parse(body);
                    File.WriteAllText(f, body);
                    Console.WriteLine($"  {icao}: {d.Taxiways.Count} taxiways, {d.HoldingPoints.Count} holding points ({url})");
                    break;
                }
                Console.WriteLine($"  {icao}: {(int)resp.StatusCode} from {url}");
            }
            catch (Exception ex) { Console.WriteLine($"  {icao}: {ex.GetType().Name} from {url}"); }
            mi++;
            await Task.Delay(3000);
        }
        await Task.Delay(1500);
    }
}

// ── departures, crossings, clearances ───────────────────────────────────────
// Per airport: named (OSM) holding-point departures, intersection departures, full-length
// backtrack departures, gate→gate taxis across the field, and ATC clearances that carry
// explicit "hold short of runway X" instructions — each flown through FlyTaxi with the
// expectations the pilot would have (held at the named line, lined up where selected).
void RunDepartures(World w)
{
    int seed = 29; foreach (char ch in w.Icao) seed = seed * 31 + ch;
    var rnd = new Random(seed);
    var gates = w.Parking.Where(p => w.NodeNear(p.Latitude, p.Longitude, 100) != null)
                         .OrderBy(p => p.ToString(), StringComparer.Ordinal).ToList();
    if (gates.Count == 0) return;
    var rwyEnds = w.Runways.Where(r => !r.IsClosed).OrderBy(r => r.RunwayID, StringComparer.Ordinal).ToList();
    var named = augProvider.GetNamedHoldingPoints(w.Icao);
    ParkingSpot Gate() => gates[rnd.Next(gates.Count)];

    foreach (var rwy in rwyEnds)
    {
        // Named holding points (up to 3 per runway: first full-length, first partway, one more).
        var hps = DepartureRig.ResolveHoldingPoints(w, rwy, named, gates[0]);
        var pick = new List<(TaxiGraph.RunwayIntersection e, string kind, bool partway)>();
        var f0 = hps.FirstOrDefault(h => !h.partway); if (f0.e != null) pick.Add(f0);
        var p0 = hps.FirstOrDefault(h => h.partway); if (p0.e != null) pick.Add(p0);
        var rest = hps.Where(h => !pick.Contains(h)).ToList();
        if (rest.Count > 0) pick.Add(rest[rnd.Next(rest.Count)]);
        foreach (var h in pick)
        {
            var g = Gate();
            var tp = TaxiPlan.Departure(w, g, rwy, "HP", h.e, h.partway, h.kind);
            if (tp == null) continue;
            Codes("HP", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false));
            Codes("HP_CLR", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: true, trace: false));
            taxis += 2;
        }
        // Intersection departures (up to 2 per runway).
        var lineup = DepartureRig.Lineup(w, rwy);
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var ixGate = Gate();
#if OLDAPP
        var ixs = w.Graph.GetRunwayIntersections(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, lineup.lat, lineup.lon);
#else
        var ixs = w.Graph.GetRunwayIntersections(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, lineup.lat, lineup.lon,
            ixGate.Latitude, ixGate.Longitude);
#endif
        foreach (var ix in ixs.OrderBy(_ => rnd.Next()).Take(2).OrderBy(x => x.AlongMetersFromThreshold))
        {
            var tp = TaxiPlan.Departure(w, ixGate, rwy, "IX", ix, true, "");
            if (tp == null) continue;
            Codes("IX", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false));
            taxis++;
        }
        // Full-length backtrack, where the runway list would say "(backtrack required)".
        {
            var g = Gate();
#if MAINAPP
            if (false)
#else
            if (w.Graph.RunwayNeedsBacktrack(lineup.lat, lineup.lon, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, g.Latitude, g.Longitude))
#endif
            {
                var tp = TaxiPlan.Backtrack(w, g, rwy);
                if (tp != null) { Codes("BT", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: false, trace: false)); taxis++; }
            }
        }
    }
    // Gate → gate across the field (crossings), and gate → runway with ATC "hold short" picks.
    for (int k = 0; k < opt.TaxiPerAirport; k++)
    {
        var a = Gate(); var b = Gate();
        var tp = TaxiPlan.GateToGate(w, a, b);
        if (tp != null && tp.StartLat != 0)
        {
            Codes("G2G", w.Icao, tp.Describe, FlyTaxi(w, tp, withClearance: true, trace: false));
            taxis++;
        }
        if (rwyEnds.Count == 0) continue;
        var r = rwyEnds[rnd.Next(rwyEnds.Count)];
        var rp = TaxiPlan.GateToRunway(w, Gate(), r);
        if (rp != null)
        {
            rp.AddHoldPicks = true; rp.PickDestination = k % 3 == 2; rp.PickReciprocal = k % 2 == 1;
            Codes("HSCLR", w.Icao, rp.Describe, FlyTaxi(w, rp, withClearance: true, trace: false));
            taxis++;
        }
    }
}

// ── one landing ──────────────────────────────────────────────────────────────
List<Finding> FlyLanding(World w, Runway rwy, List<LandingExit> exits, LandingExit exit, bool straight, bool trace)
{
    var found = new List<Finding>();
    string where = $"{rwy.RunwayID} {Name(exit)} ({exit.ExitType} {exit.ExitAngleDegrees:F0}°){(straight ? " straight-roll" : "")}";
    var geo = new LandingGeometry(rwy);
    LandingGeometry.Feasible(rwy, exit, out double exitAlong);
    double exitSpeedKt = ExitTurnsBack(exit) && !straight ? 8 : exit.ExitType == "High-speed" ? 35 : 18;   // warned: slow for a sharp turn back
    double tdAlong = Math.Max(300, exitAlong - 1600);
    var (lat, lon) = geo.At(tdAlong, 0);
    double hdg = rwy.Heading;
    double gs = Math.Min(130, Kt(Math.Sqrt(Math.Pow(exitSpeedKt * 0.5144, 2) + 2 * 1.6 * (exitAlong - tdAlong))));

    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        string? err = mgr.LoadRoute(provider, w.Icao, lat, lon, hdg, exit.NodeId,
            $"Taxiway {Name(exit)}", taxiwaySequence: null, userHoldShortIndices: null,
            destinationHeading: null, destinationThresholdLat: exit.Latitude,
            destinationThresholdLon: exit.Longitude, destinationHeadingTrue: null,
            isRunwayDestination: false, prebuiltGraph: w.Graph, announceSummary: false,
#if MAINAPP
            landingRolloutRoute: true);
        if (err != null)
            mgr.BeginLandingRolloutNoGraph(exit, rwy.Heading, rwy, exits, lat, lon, settings,
                w.Graph, provider, w.Icao, gs);
        else
        {
            mgr.StartGuidance(settings);   // as LandingExitPlanner.StartExitGuidance
            mgr.BeginLandingRollout(exit, rwy.Heading, rwy, exits, lat, lon, gs);
        }
#else
            landingRolloutRoute: true);
        if (err != null)
            mgr.BeginLandingRolloutNoGraph(exit, rwy.Heading, rwy, exits, lat, lon, settings,
                w.Graph, provider, w.Icao, gs);
        else
        {
            mgr.StartGuidance(settings, announceStart: false);   // as LandingExitPlanner.StartExitGuidance
            mgr.BeginLandingRollout(exit, rwy.Heading, rwy, exits, lat, lon, gs);
        }
#endif

        var pilot = new PilotModel(hdg, gs);
        double t = 0, maxOffPavement = 0; string offWhere = "";
        bool arrived = false, sawMissed = false, onRunwayAtArrival = false;
        double straightAlongAtMiss = double.NaN, holdWait = 0;
        TaxiRoute? routeAtMiss = null;
        string missText = "";
        bool hairpinChecked = false, dirContraReported = false; double handoffT = -100;
        string? arrivalText = null;
        var stateLog = new List<string>();
        TaxiGuidanceState prevState = mgr.State;
        double odo = 0;
        while (t < 400)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            routeAtMiss = mgr.RouteForHarness;
            mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            var st = mgr.State;
            if (st != prevState)
            {
                stateLog.Add($"{t:F1}s {prevState}->{st}"); prevState = st;
                if (prevState == TaxiGuidanceState.Taxiing && !straight && !hairpinChecked && mgr.RouteForHarness is { } hr && hr.Segments.Count > 1)
                {
                    hairpinChecked = true;
                    handoffT = t;
                    var firstLeg = hr.Segments.FirstOrDefault(sg => sg.DistanceMeters >= 3.0);
                    if (firstLeg != null && Math.Abs(Geo.Norm(firstLeg.BearingDegrees - hdg)) > 120)
                        found.Add(new Finding(w.Icao, "HANDOFF_ROUTE_STARTS_BACKWARDS", where, $"first leg {firstLeg.BearingDegrees:F0}° with the aircraft on {hdg:F0}°, {geo.Describe(lat, lon, exitAlong)}"));
                    double run = 0;
                    for (int k = 1; k < hr.Segments.Count && run < 150; k++)
                    {
                        run += hr.Segments[k - 1].DistanceMeters;
                        double turn = Math.Abs(Geo.Norm(hr.Segments[k].BearingDegrees - hr.Segments[k - 1].BearingDegrees));
                        if (turn > 120 && hr.Segments[k].DistanceMeters > 3)
                        { found.Add(new Finding(w.Icao, "EXIT_ROUTE_HAIRPIN", where, $"the route off the runway turns {turn:F0}° {run:F0} m after the handoff — a {exit.ExitType} exit that needs a U-turn")); break; }
                    }
                }
                if (trace && mgr.RouteForHarness is { } rr)
                {
                    Console.WriteLine($"  ROUTE now {rr.Segments.Count} segs:");
                    Console.WriteLine($"  aircraft {geo.Describe(lat, lon, exitAlong)} hdg={hdg:F0}; exit node n{exit.NodeId} apron n{exit.ApronNodeId}");
                    foreach (var sg in rr.Segments.Take(14))
                        Console.WriteLine($"     {sg.TaxiwayName,-6} n{sg.FromNode.NodeId}->n{sg.ToNode.NodeId} {geo.Describe(sg.ToNode.Latitude, sg.ToNode.Longitude, exitAlong)} brg={sg.BearingDegrees:F0} hold={sg.IsHoldShortPoint} [{string.Join(",", (w.Graph.Adjacency.TryGetValue(sg.FromNode.NodeId, out var ee) ? ee : new()).Select(x => (x.TaxiwayName == "" ? "-" : x.TaxiwayName) + "->n" + x.ToNodeId))}]");
                }
            }
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            var newSpeech = cap.TakeNew();
            if (Burst() && !straight && newSpeech.Count >= 2 && t > 0.15) Console.WriteLine($"BURST {w.Icao} {where} t={t:F1}: {string.Join(" | ", newSpeech)}");
            if (!straight && !dirContraReported && DirectionContradiction(newSpeech, pan) is { } dcL)
            { dirContraReported = true; found.Add(new Finding(w.Icao, "SPOKEN_DIRECTION_CONTRADICTS", where, $"t={t:F0}s {geo.Describe(lat, lon, exitAlong)}: {dcL}")); }
            foreach (var a in newSpeech)
            {
                if (trace) Console.WriteLine($"  {t,6:F1}s SPEAK {a}");
                if (a.Contains("Missed", StringComparison.OrdinalIgnoreCase) || a.Contains("Retargeting", StringComparison.OrdinalIgnoreCase)
                    || a.Contains("Unable planned exit", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sawMissed) { straightAlongAtMiss = geo.Along(lat, lon) - exitAlong; missText = a; }
                    sawMissed = true;
                    if (!straight)
                    {
                        // Was the pilot on the route the tone was following, and where does that route leave the runway?
                        string diag = "";
                        if (routeAtMiss is { } rm && rm.Segments.Count > 0)
                        {
                            var pts = new List<(double Lat, double Lon)> { (rm.Segments[0].FromNode.Latitude, rm.Segments[0].FromNode.Longitude) };
                            pts.AddRange(rm.Segments.Select(sg => (sg.ToNode.Latitude, sg.ToNode.Longitude)));
                            double dRoute = PathDist(pts, lat, lon);
                            var leave = pts.FirstOrDefault(pp => Math.Abs(geo.Lateral(pp.Lat, pp.Lon)) > 10);
                            string leaves = leave == default ? "never" : $"{geo.Along(leave.Lat, leave.Lon) - exitAlong:+0;-0} m";
                            diag = $" [on-route {dRoute:F0} m; route >10 m off centreline from {leaves}]";
                        }
                        found.Add(new Finding(w.Icao, "LANDING_FALSE_MISS", where, $"t={t:F0}s gs={gs:F0}kt {geo.Describe(lat, lon, exitAlong)}{diag}: \"{a}\""));
                    }
                }
                if (a.Contains("Off the runway at", StringComparison.OrdinalIgnoreCase)) arrivalText = a.Substring(a.IndexOf("Off the runway at", StringComparison.OrdinalIgnoreCase));
                if (!straight && a.Contains("U-turn", StringComparison.OrdinalIgnoreCase) && t - handoffT < 15 && geo.Along(lat, lon) - exitAlong < 60 && Math.Abs(geo.Lateral(lat, lon)) < geo.HalfWidthM)
                    found.Add(new Finding(w.Icao, ExitTurnsBack(exit) ? "UTURN_ON_FLAGGED_TURNBACK_EXIT" : "UTURN_SPOKEN_ON_RUNWAY", where, $"\"{a}\" at {gs:F0} kt, {geo.Describe(lat, lon, exitAlong)}"));
                if (!straight && t < 1 && a.EndsWith("Steering guidance active.", StringComparison.OrdinalIgnoreCase)
                    && !a.StartsWith($"Taxiway {Name(exit)}.", StringComparison.OrdinalIgnoreCase))
                    found.Add(new Finding(w.Icao, "TOUCHDOWN_NAMES_OTHER_TAXIWAY", where, $"\"{a}\" spoken at touchdown"));
                if (!straight && (a.Contains("Could not", StringComparison.OrdinalIgnoreCase) || a.Contains("unavailable", StringComparison.OrdinalIgnoreCase)))
                    found.Add(new Finding(w.Icao, "LANDING_SPOKEN_FAILURE", where, $"\"{a}\""));
            }
            if (st == TaxiGuidanceState.Arrived && !arrived)
            {
                arrived = true;
                onRunwayAtArrival = Math.Abs(geo.Lateral(lat, lon)) < geo.HalfWidthM;
                if (straight) break;
            }
            if (arrived && gs < 0.5) break;
            if (st == TaxiGuidanceState.HoldShort)
            {
                if (gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            }
            if (st == TaxiGuidanceState.Inactive && t > 2) break;

            // What the pilot does.
            bool rollout = st == TaxiGuidanceState.LandingRollout;
            double alongPast = geo.Along(lat, lon) - exitAlong;
            double targetKt;
            if (arrived || st == TaxiGuidanceState.HoldShort) targetKt = 0;
            else if (rollout && alongPast < 0)
                targetKt = Math.Max(exitSpeedKt, Kt(Math.Sqrt(Math.Pow(exitSpeedKt * 0.5144, 2) + 2 * 1.6 * Math.Max(0, -alongPast))));
            else targetKt = straight ? 20 : Math.Min(exitSpeedKt, pilot.TaxiTarget(pan, cap));
            bool followTone = !(straight && alongPast > -50);
            pilot.Step(ref hdg, ref gs, followTone ? pan : 0f, targetKt, 0.1,
                holdHeading: !followTone || (rollout && pan == 0) ? rwy.Heading : (double?)null);
            (lat, lon) = Geo.Move(lat, lon, hdg, gs * 0.5144 * 0.1);
            odo += gs * 0.5144 * 0.1;

            if (!rollout || alongPast > 0)
            {
                double off = w.OffPavementMetres(lat, lon);
                if (off > maxOffPavement) { maxOffPavement = off; offWhere = $"t={t:F0}s {geo.Describe(lat, lon, exitAlong)} state={st}"; }
            }
            if (trace && ((int)(t * 10)) % 5 == 0)
                Console.WriteLine($"  {t,6:F1}s {st,-15} {geo.Describe(lat, lon, exitAlong)} hdg={hdg:F1} gs={gs:F1} pan={pan:F2} off={w.OffPavementMetres(lat, lon):F0}");
            if (straight && (sawMissed || geo.Along(lat, lon) > exitAlong + 600)) break;
            // Off the END only while still in the runway strip: an End exit's turnoff legitimately
            // runs on past the runway's length 80-120 m to the side (KSLC 14 K1, EDDF 07C L1).
            if (geo.Along(lat, lon) > geo.LengthM + 50 && Math.Abs(geo.Lateral(lat, lon)) <= geo.HalfWidthM + 30) { if (straight) break; found.Add(new Finding(w.Icao, "LANDING_RAN_OFF_END", where, $"t={t:F0}s state={st}")); break; }
        }
        resultsWriter?.WriteLine($"{(straight ? "STRAIGHT" : "LANDING")}|{w.Icao}|{rwy.RunwayID}|{Name(exit)}|{exit.NodeId}|state={mgr.State}|arrived={arrived}|miss={(sawMissed ? $"{straightAlongAtMiss:F0}" : "-")}|missText={missText}|arrival={arrivalText}|off={maxOffPavement:F0}|end={geo.Describe(lat, lon, exitAlong)}");
        if (straight)
        {
            if (!sawMissed && !arrived)
                found.Add(new Finding(w.Icao, "STRAIGHT_MISS_NOT_CALLED", where, $"rolled {geo.Along(lat, lon) - exitAlong:F0} m past the junction on the centreline with no miss/retarget call; state={mgr.State}"));
            else if (sawMissed && straightAlongAtMiss > 200)
                found.Add(new Finding(w.Icao, "STRAIGHT_MISS_LATE", where, $"miss called {straightAlongAtMiss:F0} m past the junction"));
            else if (arrived)
                found.Add(new Finding(w.Icao, "STRAIGHT_ROLL_ARRIVED", where, $"rolling straight on the centreline was reported as having taken the exit ({geo.Describe(lat, lon, exitAlong)})"));
            return found;
        }
        if (!arrived)
            found.Add(new Finding(w.Icao, "LANDING_NOT_ARRIVED", where, $"state={mgr.State} after {t:F0}s at {geo.Describe(lat, lon, exitAlong)} [{string.Join(", ", stateLog.TakeLast(4))}]"));
        else
        {
            if (onRunwayAtArrival) found.Add(new Finding(w.Icao, "LANDING_ARRIVED_ON_RUNWAY", where, $"arrival at {geo.Describe(lat, lon, exitAlong)}"));
            if (arrivalText != null && exit.TaxiwayName.Length > 0 && !arrivalText.Contains(Name(exit), StringComparison.OrdinalIgnoreCase) && !sawMissed)
                found.Add(new Finding(w.Icao, "LANDING_WRONG_EXIT_NAMED", where, $"\"{arrivalText}\""));
        }
        if (maxOffPavement > 12)
            found.Add(new Finding(w.Icao, "LANDING_OFF_PAVEMENT", where, $"{maxOffPavement:F0} m off the pavement following the tone, {offWhere}"));
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "LANDING_EXCEPTION", where, ex.GetType().Name + ": " + ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

// ── one taxi ─────────────────────────────────────────────────────────────────
List<Finding> FlyTaxi(World w, TaxiPlan? plan, bool withClearance, bool trace)
{
    var found = new List<Finding>();
#if !OLDAPP
    lastFlightHoldNodes = null; lastFlightXc = null; lastFlightKeys = null;
#endif
    if (plan == null) return found;
    string where = plan.Describe + (withClearance ? " [clearance]" : "");
#if !OLDAPP
    if (plan.FixedSeq != null) where += plan.PickSuffix();
    if (plan.ClearFirstCrossing) where += " [first crossing cleared]";
    if (plan.KeyVariant != null) where += $" [{KeyFlight.Describe(plan.KeyVariant)}]";
#endif
    if (depTraceFilter != null)
    {
        if (!where.Contains(depTraceFilter, StringComparison.OrdinalIgnoreCase)) return found;
        trace = true; Console.WriteLine("=== " + where);
    }
#if !OLDAPP
    // --crossing-keys: what this flight presses, where, and what came of it.
    KeyFlight? keys = plan.KeyVariant == null ? null : new KeyFlight { Variant = plan.KeyVariant };
    lastFlightKeys = keys;
#endif
    var cap = new Cap(() => simNow);
    var mgr = new TaxiGuidanceManager(cap);
    try
    {
        List<string>? seq = null;
        double shortestM = 0;
#if !OLDAPP
        // A clearance given outright (the Turn pick flights, --trace ICAO:pick): LoadRoute edits the
        // list it is handed, so each load gets its own copy.
        if (plan.FixedSeq != null) seq = new List<string>(plan.FixedSeq);
#endif
        if (withClearance)
        {
            // What ATC would say: the taxiway names of the shortest route, in order.
            var probe = new TaxiGuidanceManager(new Cap(() => simNow));
            string? e0 = plan.Load(probe, provider, w, null);
            var r0 = probe.RouteForHarness;
            if (e0 != null || r0 == null) { probe.Dispose(); return found; }
            shortestM = r0.TotalDistanceMeters;
            seq = new List<string>();
            foreach (var s in r0.Segments)
                if (!string.IsNullOrEmpty(s.TaxiwayName) && (seq.Count == 0 || seq[^1] != s.TaxiwayName)) seq.Add(s.TaxiwayName);
            if (seq.Count == 0) { probe.Dispose(); return found; }
            where += " via " + string.Join(",", seq);
            if (plan.AddHoldPicks)
            {
                // What ATC adds: "hold short of runway X" after the taxiway the route meets X on.
                var picks = new Dictionary<int, string>();
                int si = -1; string lastName = ""; TaxiGraph.RunwayCenterline? onR = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, plan.StartLat, plan.StartLon);
                foreach (var sg in r0.Segments)
                {
                    if (!string.IsNullOrEmpty(sg.TaxiwayName) && sg.TaxiwayName != lastName) { si++; lastName = sg.TaxiwayName; }
                    var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                    if (ru != null && ru != onR)
                    {
                        bool isDest = plan.DestRunway != null && (ru.Name1 == plan.DestRunway || ru.Name2 == plan.DestRunway);
                        if ((!isDest || plan.PickDestination) && si >= 0 && !picks.ContainsKey(si))
                            picks[si] = plan.PickReciprocal ? ru.Name2 : ru.Name1;
                    }
                    onR = ru;
                }
                probe.Dispose();
                if (picks.Count == 0) return found;
                plan.HoldPicks = picks;
                // The same clearance as a controller would say it, through the app's own parser.
                if (plan.DestRunway != null) found.AddRange(SpokenClearance.Check(w, where, seq, picks, plan.DestRunway));
                where += " hold short " + string.Join(",", picks.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value}@{seq[kv.Key]}"));
            }
            else probe.Dispose();
        }
#if !OLDAPP
        var seqAsGiven = seq == null ? null : new List<string>(seq);   // LoadRoute edits the list it is handed
#endif
        string? err = plan.Load(mgr, provider, w, seq);
#if !OLDAPP
        // --clear-first-crossing: the first runway this route crosses is known only once it is loaded, so it is
        // loaded once, the first crossing not already passed is read from its runway events (route order: where
        // the route reaches the runway), and it is loaded again, on a fresh manager, with that crossing cleared.
        XcFlight? xc = null;
        if (plan.ClearFirstCrossing && err == null && mgr.RouteForHarness is { } firstLoad)
        {
            var first = FirstCrossing(firstLoad);
            if (first == null)
            {
                if (keys != null) keys.Skip = "no runway crossed";
                else counts["~xc no runway crossed"] = counts.TryGetValue("~xc no runway crossed", out int nc) ? nc + 1 : 1;
                if (trace) Console.WriteLine("=== no runway crossing on this route: nothing to clear");
                return found;
            }
            plan.ClearedCrossings = new[] { new ClearedCrossing(first.Designator) };
            try { mgr.StopGuidance(); mgr.Dispose(); } catch { }
            cap.TakeNew();
            mgr = new TaxiGuidanceManager(cap);
            err = plan.Load(mgr, provider, w, seqAsGiven == null ? null : new List<string>(seqAsGiven));
            xc = err == null && mgr.RouteForHarness is { } clearedLoad
                ? XcFlight.Setup(clearedLoad, first.Designator)
                : new XcFlight { Runway = first.Designator, Outcome = "LOAD_REFUSED", Flown = true };
            lastFlightXc = xc;
            // The Hold short key flights need a stop the clearing took off whole, counted down as cleared: not a start
            // hold (no approach to count down), not a shared stop (it still holds for its other runway).
            if (keys != null && xc.Outcome != "CLEARED")
            {
                keys.Skip = "first crossing " + xc.Outcome;
                if (trace) Console.WriteLine($"=== first crossing {xc.Outcome}: no key flight");
                return found;
            }
            if (keys != null) keys.SetStop(xc);
        }
        // Continue before the line needs the first crossing held by a stop of its own on a segment, for that runway
        // alone (a shared stop keeps its other runway, [HLD-11]), that is not the pilot's own hold, and not on the
        // route's last segment (CanClearEarly).
        if (keys is { Variant: KeyFlight.Early } && err == null && mgr.RouteForHarness is { } earlyLoad)
        {
            string? why = KeyFlight.SetEarlyStop(keys, earlyLoad);
            if (why != null)
            {
                keys.Skip = why;
                if (trace) Console.WriteLine($"=== {why}: no key flight");
                return found;
            }
        }
#endif
        var route = mgr.RouteForHarness;
        if (err != null || route == null)
        {
#if !OLDAPP
            // The same refusal is the plain flight's finding already: a key flight is not flown.
            if (keys != null) { keys.Skip = "route refused"; return found; }
#endif
            // The Taxi form's own refusals (announceSummary: true, as the form loads) — honest
            // "no route from here" answers, recorded so they can be read, not flown.
            found.Add(new Finding(w.Icao, withClearance ? "CLEARANCE_ROUTE_FAILED" : "TAXI_ROUTE_REFUSED", where, err ?? "no route"));
            return found;
        }
        if (plan.HoldPicks != null)
        {
            string all = mgr.LastRouteSummary + " " + StartWarning(mgr);
            if (all.Contains("could not be set", StringComparison.OrdinalIgnoreCase) || all.Contains("not on route", StringComparison.OrdinalIgnoreCase)
                || all.Contains("no safe place", StringComparison.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, "HOLDPICK_NOT_SET", where, (StartWarning(mgr) ?? mgr.LastRouteSummary ?? "").Trim()));
        }
        bool hpMissWarned = (StartWarning(mgr) ?? "").Contains("is not on a usable route", StringComparison.OrdinalIgnoreCase);
        if (plan.ExpectHoldNodeId is int xh && !route.Segments.Any(sg => sg.ToNode.NodeId == xh || sg.FromNode.NodeId == xh))
            found.Add(new Finding(w.Icao, hpMissWarned ? "HP_ROUTE_MISSES_POINT_WARNED" : "HP_ROUTE_MISSES_POINT", where, $"route does not pass the painted line's node n{xh}"));
        if (withClearance && route.TotalDistanceMeters > shortestM * 1.3 + 200)
            found.Add(new Finding(w.Icao, "CLEARANCE_ROUTE_DETOUR", where, $"cleared route {route.TotalDistanceMeters:F0} m vs shortest {shortestM:F0} m over the same taxiways"));
        string summary = mgr.LastRouteSummary + " " + StartWarning(mgr);
        lastRouteLine = RouteLine(route);
        double stubLenM = 0;   // the entry stub the lineup tone follows first (new app only)
        if (trace) Console.WriteLine($"ROUTE {route.Segments.Count} segs {route.TotalDistanceMeters:F0} m: {summary}");
        if (trace)
        {
            foreach (var ev in route.RunwayEvents) Console.WriteLine($"  EVENT {ev}");
            var s0 = route.Segments[0].FromNode;
            Console.WriteLine($"  START n{s0.NodeId} type={s0.Type} aircraft {plan.StartLat:F6},{plan.StartLon:F6} hdg {plan.StartHeading:F0}");
            foreach (var hn in w.Graph.Nodes.Values.Where(n => (n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort)
                         && TaxiGraph.CalculateDistanceMeters(plan.StartLat, plan.StartLon, n.Latitude, n.Longitude) < 150))
            {
                int onIdx = route.Segments.FindIndex(sg => sg.ToNode.NodeId == hn.NodeId);
                Console.WriteLine($"  HSNEAR lat {RwyLat(w, hn.Latitude, hn.Longitude)} start lat {RwyLat(w, plan.StartLat, plan.StartLon)} seg-ends {string.Join(" ", route.Segments.Take(6).Select(sg => "n" + sg.ToNode.NodeId + "=" + RwyLat(w, sg.ToNode.Latitude, sg.ToNode.Longitude)))}");
                Console.WriteLine($"  HSNEAR n{hn.NodeId} {hn.HoldShortName} {TaxiGraph.CalculateDistanceMeters(plan.StartLat, plan.StartLon, hn.Latitude, hn.Longitude):F0} m brg {TaxiPlanBearing(plan.StartLat, plan.StartLon, hn):F0} onRouteSeg={onIdx} comp={hn.ComponentId} adj=[{string.Join(",", (w.Graph.Adjacency.TryGetValue(hn.NodeId, out var aa) ? aa : new()).Select(x => x.TaxiwayName + ":" + x.PathType + "->n" + x.ToNodeId + "(" + x.DistanceMeters.ToString("F0") + ")"))}]");
            }
            for (int k = 0; k < route.Segments.Count; k++)
            {
                var sg = route.Segments[k];
                var ru = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, sg.ToNode.Latitude, sg.ToNode.Longitude);
                Console.WriteLine($"  seg{k,-3} {sg.TaxiwayName,-8} {sg.DistanceMeters,5:F0} m brg {sg.BearingDegrees,3:F0} to n{sg.ToNode.NodeId} {(sg.IsHoldShortPoint ? "HOLD " + sg.HoldShortRunway : "")} {(ru != null ? "ON RWY " + ru.Name1 + "/" + ru.Name2 : "")}");
            }
        }

#if !OLDAPP
        if (route.RunwayEntryStub is { Count: >= 2 } stubL)
        {
            double len = 0;
            for (int k = 1; k < stubL.Count; k++) stubLenM += TaxiGraph.CalculateDistanceMeters(stubL[k - 1].Lat, stubL[k - 1].Lon, stubL[k].Lat, stubL[k].Lon);
            for (int k = 1; k < stubL.Count; k++) len += TaxiGraph.CalculateDistanceMeters(stubL[k - 1].Lat, stubL[k - 1].Lon, stubL[k].Lat, stubL[k].Lon);
            if (Environment.GetEnvironmentVariable("VP_STUBSTATS") == "1") Console.WriteLine($"STUB {len:F0} |{route.Segments[^1].HoldShortRunway}| dest {plan.DestRunway} {w.Icao} {plan.Describe}");
            if (len > 600) found.Add(new Finding(w.Icao, "DEST_HOLD_FAR_FROM_ENTRY", where, $"the destination hold is {len:F0} m of taxiing before the runway entry"));
        }
        if (trace && route.RunwayEntryStub is { } stubT && plan.Rwy != null)
        {
            var lg = new LandingGeometry(plan.Rwy);
            Console.WriteLine($"  expected lineup along {plan.ExpectLineupAlongM:F0} m; entry stub:");
            foreach (var (sla, slo) in stubT) Console.WriteLine($"     along {lg.Along(sla, slo),6:F0} lateral {lg.Lateral(sla, slo),5:F0}");
        }
        if (trace && xc != null)
        {
            Console.WriteLine($"  CLEARED runway {xc.Runway}: {xc.Outcome}, stop node index {xc.StopNodeIndex} n{xc.StopNode?.NodeId}, line \"{xc.Label}\""
                + (mgr.LastClearedCrossingWarning.Length > 0 ? $"; warning \"{mgr.LastClearedCrossingWarning}\"" : ""));
            foreach (var ev in route.RunwayEvents)
                Console.WriteLine($"    event {ev.Kind} {ev.Designator} held={ev.Held} passed={ev.Passed} cleared={ev.Cleared} stop={ev.StopNodeIndex} reach={ev.ReachNodeIndex} at {ev.Location}");
        }
        // Each hold-short stop by its route node: the stop segment's end node (the start node for a start hold).
        var holdNodes = new List<int>();
        int HoldNodeNow()
        {
            if (mgr.RouteForHarness is not { Segments.Count: > 0 } r) return -1;
            int i = mgr.SegmentIndexForHarness;
            return i <= 0 ? r.Segments[0].FromNode.NodeId : i - 1 < r.Segments.Count ? r.Segments[i - 1].ToNode.NodeId : -1;
        }
#endif
        mgr.StartGuidance(settings);
        var cue = mgr.ConsumeStartHoldCue();
        if (trace && cue != null) Console.WriteLine($"  START-HOLD {cue}");

        double lat = plan.StartLat, lon = plan.StartLon, hdg = plan.StartHeading, gs = 0;
        var pilot = new PilotModel(hdg, 0);
        double t = 0, odo = 0, maxOff = 0, lastHoldOdo = double.NegativeInfinity, holdWait = 0, offSince = -1;
        string offWhere = "", lineupWhere = ""; int recalcs = 0; double maxOffLineup = 0;
        bool lineupPlaceChecked = false;
        bool linedUp = false, joinedPavement = false, falseOffRouteReported = false, holdOnRunwayReported = false, dirContraReported = false; double lineupStartOdo = -1;
        TaxiGraph.RunwayCenterline? onRunway = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
        TaxiGraph.RunwayCenterline? leftRunway = null; double leftRunwayOdo = double.NegativeInfinity;
        bool done = false; string doneState = "";
        double budget = route.TotalDistanceMeters / 3.0 + 240;
        var stateLog = new List<string>(); var prev = mgr.State;
        // Every stop for a hold short: where, what was said, and whether a runway followed it.
        var stops = new List<HoldStop>();
        string lastHoldSpeech = cue ?? "";
        if (prev == TaxiGuidanceState.HoldShort) stops.Add(new HoldStop(0, plan.StartLat, plan.StartLon, cue ?? "", 0));
#if !OLDAPP
        if (prev == TaxiGuidanceState.HoldShort) holdNodes.Add(HoldNodeNow());
#endif
        bool lineupChecked = false, reachedLineup = false;
        var paintCross = new List<(double odo, double lat, double lon)>();   // each painted runway-hold line crossed
        // The incursion guard's hold-line calls ("Crossing runway X." / "Passing the X hold line."): one about a
        // line already behind the aircraft, a "Crossing" with no runway in the next 200 m, the same line twice.
        bool holdCallBehindReported = false, crossingNoRunwayReported = false, holdCallRepeatReported = false;
        (double odo, string text)? pendingCrossing = null;
        (double odo, string text, string what)? pendingBehind = null;
        (string text, double odo, int stops)? lastHoldCall = null;
        // ...and the other way round: a runway entered on the way (not the destination) with no "Crossing" call
        // naming it from 150 m before the entry to 100 m after it.
        var crossingCalls = new List<(double odo, string text)>();
        var unannouncedCheck = new List<(double odo, TaxiGraph.RunwayCenterline rwy)>();
        bool crossedUnannouncedReported = false;
#if !OLDAPP
        // Turn pick flights: what was said about the pick and the off-chart leg, and (the miss flight)
        // the charted way the pilot takes at the junction instead of the picked turn.
        bool pickSaidMissed = false, pickSaidNotKept = false, legSaid = false, legReached = false, routeChangedSaid = false;
        double legMaxOff = 0, legLengthM = 0, legNearestM = double.MaxValue;
        string legOffWhere = ""; bool legOpensTheRoute = false; (double lat, double lon) legMid = default;
        if (route.TurnPicks.FirstOrDefault(p => p.IsOffChart) is { } legPick && legPick.FirstSegmentAfterDeparture < route.Segments.Count)
        {
            // The leg itself, from the route as loaded: its length, its midpoint (did the pilot, following
            // the tone, actually taxi along it?) and whether it is the route's very first segment (the
            // start utterance then says it, and the on-leg sentence waits out the start grace window).
            var legSeg = route.Segments[legPick.FirstSegmentAfterDeparture];
            legLengthM = legPick.OffChartLengthMeters; legOpensTheRoute = legPick.FirstSegmentAfterDeparture == 0;
            legMid = ((legSeg.FromNode.Latitude + legSeg.ToNode.Latitude) / 2, (legSeg.FromNode.Longitude + legSeg.ToNode.Longitude) / 2);
        }
        var divert = plan.DivertPath; int divertIdx = 0; double divertStartOdo = -1; bool diverting = false, diverted = false, excursionPastEnd = false;
        if (keys != null) keys.Flown = true;
#endif
        // When the hold-short countdown and the turn notices come (CountdownTiming, TurnTiming): measured
        // along the route, from the route's own segments, never from the app's countdown.
        var cdTiming = new CountdownTiming();
        var turnTiming = new TurnTiming();
        while (t < budget)
        {
            simNow = simNow.AddMilliseconds(100); t += 0.1;
            var st = mgr.State;
            if (st != TaxiGuidanceState.HoldShort) mgr.UpdatePosition(lat, lon, hdg, 0.0, gs);
            st = mgr.State;
            HoldStop? newStop = null;
            if (st != prev)
            {
                stateLog.Add($"{t:F0}s {prev}->{st}");
                if (st == TaxiGuidanceState.HoldShort) stops.Add(newStop = new HoldStop(odo, lat, lon, "", t));
#if !OLDAPP
                if (st == TaxiGuidanceState.HoldShort) holdNodes.Add(HoldNodeNow());
#endif
                if (st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.BacktrackDeparture) reachedLineup = true;
                if ((st == TaxiGuidanceState.LiningUp || st == TaxiGuidanceState.BacktrackDeparture) && plan.ExpectHoldNodeId is int hn && !lineupChecked)
                {
                    lineupChecked = true;
                    var last = stops.LastOrDefault();
                    if (w.Graph.Nodes.TryGetValue(hn, out var hnode))
                    {
                        double d = last == null ? double.NaN : TaxiGraph.CalculateDistanceMeters(last.Lat, last.Lon, hnode.Latitude, hnode.Longitude);
                        if ((last == null || d > 30) && !hpMissWarned)
                            found.Add(new Finding(w.Icao, "HP_HELD_AWAY_FROM_POINT", where, last == null ? "no hold stop before the lineup" : $"held {d:F0} m from the painted line {plan.ExpectHoldName}: \"{last.Text}\""));
                        else if (plan.ExpectHoldName != null && !last.Text.Contains(plan.ExpectHoldName, StringComparison.OrdinalIgnoreCase))
                            found.Add(new Finding(w.Icao, "HP_HOLD_NOT_NAMED", where, $"held at {plan.ExpectHoldName} but told \"{last.Text}\""));
                    }
                }
                prev = st;
            }
            float pan = mgr.SteeringToneForHarness.ObservedPan;
            var saidNow = cap.TakeNew();
            if (Burst() && saidNow.Count >= 2 && t > 0.15) Console.WriteLine($"BURST {w.Icao} {plan.Describe} t={t:F1}: {string.Join(" | ", saidNow)}");
            if (!dirContraReported && DirectionContradiction(saidNow, pan) is { } dcT)
            { dirContraReported = true; found.Add(new Finding(w.Icao, "SPOKEN_DIRECTION_CONTRADICTS", where, $"t={t:F0}s: {dcT}")); }
#if !OLDAPP
            if (xc != null)
            {
                int before = xc.Recalcs.Count;
                xc.Track(mgr.RouteForHarness, lat, lon);
                if (trace && xc.Recalcs.Count > before) Console.WriteLine($"  {t,6:F1}s CLEARED-CROSSING ON THE NEW ROUTE: {xc.Recalcs[^1]}");
            }
            if (keys?.Xc is { } kTrack && !ReferenceEquals(kTrack, xc)) kTrack.Track(mgr.RouteForHarness, lat, lon);
#endif
            // Measured at the position UpdatePosition was given this frame (the move comes below).
            cdTiming.Frame(w.Icao, where, found, st, mgr.RouteForHarness, mgr.SegmentIndexForHarness, lat, lon, gs, odo, saidNow, newStop != null, trace);
            turnTiming.Frame(w.Icao, where, found, saidNow, odo, newStop != null);
            foreach (var a in saidNow)
            {
                if (a.Contains("Hold short", StringComparison.OrdinalIgnoreCase) || a.Contains("Hold position", StringComparison.OrdinalIgnoreCase)) lastHoldSpeech = a;
                if (trace) Console.WriteLine($"  {t,6:F1}s SPEAK {a}");
                if (a.Contains("Route changed", StringComparison.OrdinalIgnoreCase) || a.Contains("Off route.", StringComparison.Ordinal)) recalcs++;
#if !OLDAPP
                if (a.Contains("Route changed", StringComparison.OrdinalIgnoreCase)) routeChangedSaid = true;
                if (a.Contains("was missed", StringComparison.OrdinalIgnoreCase)) pickSaidMissed = true;
                if (a.Contains("could not be kept", StringComparison.OrdinalIgnoreCase)) pickSaidNotKept = true;
                if (a.Contains("Off-chart leg", StringComparison.OrdinalIgnoreCase)) legSaid = true;
                // A cleared crossing's line is where the runway is met with no stop: it stands in for the stop the
                // runway-entry checks below look for (a line never said leaves the entry unheld, as it should). A
                // shared stop still holds for its other runway, and that stop is the one the entry uses.
                bool firstLine = xc != null && xc.Hear(a, lat, lon, mgr.RouteForHarness, mgr.SegmentIndexForHarness);
                if (firstLine && t < 0.5) xc!.LineAtGuidanceStart = true;   // passed as guidance started: nothing to count down
                // A key flight: the stop put back by the Hold short key is a stop again, and its hold is the entry's
                // stop, not the line after its Continue; the stop Continue cleared early has its line stand in for it.
                bool keyLine = keys?.Xc is { } kx && !ReferenceEquals(kx, xc) && kx.Hear(a, lat, lon, mgr.RouteForHarness, mgr.SegmentIndexForHarness);
                if ((firstLine && (xc!.Outcome is "CLEARED" or "START") && !(keys is { Variant: KeyFlight.Hold, Restored: true })) || keyLine)
                {
                    stops.Add(new HoldStop(odo, lat, lon, a, t));
                    lastHoldOdo = odo;
                }
#endif
                if (a.Contains("Lined up", StringComparison.OrdinalIgnoreCase)) linedUp = true;
                if (HoldLineCall(w.Graph, a, lat, lon) is { } hc)
                {
                    // About a line already passed: every node of that line in range is behind the pilot. A
                    // "Crossing" about one is still right while the runway it names is ahead (stopped beside the
                    // node by the hold capture, a route that starts held at it): judged once 150 m have gone by.
                    var near = hc.nodes[0];
                    double rel = Math.Abs(Geo.Norm(TaxiPlanBearing(lat, lon, near) - hdg));
                    bool allBehind = hc.nodes.All(n => Math.Abs(Geo.Norm(TaxiPlanBearing(lat, lon, n) - hdg)) > 90);
                    string behindWhat = $"t={t:F0}s \"{hc.call}\" with n{near.NodeId} {Geo.Dist(lat, lon, near.Latitude, near.Longitude):F0} m away, {rel:F0}° off the heading ({lat:F6},{lon:F6})";
                    if (allBehind && !hc.crossing && !holdCallBehindReported)
                    {
                        holdCallBehindReported = true;
                        found.Add(new Finding(w.Icao, "HOLD_LINE_CALLED_BEHIND", where, behindWhat));
                    }
                    else if (allBehind && hc.crossing && pendingBehind == null) pendingBehind = (odo, hc.call, behindWhat);
                    if (hc.crossing && pendingCrossing == null) pendingCrossing = (odo, hc.call);
                    if (hc.crossing) crossingCalls.Add((odo, hc.call));
                    // The same call again with no stop between: the pilot hears one line twice.
                    if (!holdCallRepeatReported && lastHoldCall is { } lc && lc.text == hc.call && lc.stops == stops.Count && odo - lc.odo < 150)
                    {
                        holdCallRepeatReported = true;
                        found.Add(new Finding(w.Icao, "HOLD_LINE_CALL_REPEATED", where,
                            $"t={t:F0}s \"{hc.call}\" again, {odo - lc.odo:F0} m after the first, no stop between"));
                    }
                    lastHoldCall = (hc.call, odo, stops.Count);
                }
                if (a.Contains("off route", StringComparison.OrdinalIgnoreCase) && a.Contains("Warning", StringComparison.OrdinalIgnoreCase) && route != null)
                {
                    var rpts = new List<(double Lat, double Lon)> { (route.Segments[0].FromNode.Latitude, route.Segments[0].FromNode.Longitude) };
                    rpts.AddRange(route.Segments.Select(sg => (sg.ToNode.Latitude, sg.ToNode.Longitude)));
                    double dr = PathDist(rpts, lat, lon);
                    if (dr < 15 && !falseOffRouteReported)
                    {
                        falseOffRouteReported = true;
                        found.Add(new Finding(w.Icao, "OFF_ROUTE_WARNING_ON_ROUTE", where, $"\"{a}\" with the aircraft {dr:F0} m from the route"));
                    }
                }
            }
            if (newStop != null) newStop.Text = lastHoldSpeech;
#if !OLDAPP
            // --crossing-keys: the key, pressed once while taxiing, in the frame of what triggers it, right after it was
            // said. Its answer is heard on the next frame like any other line (put back into the capture).
            if (keys is { Pressed: false } && st == TaxiGuidanceState.Taxiing && mgr.RouteForHarness is { } keyRoute
                && keys.Triggered(saidNow, keyRoute, mgr.SegmentIndexForHarness, lat, lon))
            {
                keys.Press(t, lat, lon, keyRoute, w.Graph);
                if (keys.Variant == KeyFlight.Early) mgr.ContinuePastHoldShort();
                else mgr.HoldShortOfNextClearedRunway();
                var answer = cap.TakeNew();
                keys.Answer = string.Join(" ", answer).Trim();
                keys.Restored = keys.Answer.Contains("Holding short of", StringComparison.Ordinal);
                foreach (var s in answer) cap.Announce(s);
                if (trace) Console.WriteLine($"  {t,6:F1}s KEY {keys.Variant}: \"{keys.Answer}\" ({keys.PressNote()})");
            }
#endif
            if (linedUp && !lineupPlaceChecked && plan.Rwy != null && plan.ExpectLineupAlongM is double xa)
            {
                lineupPlaceChecked = true;
                var lg = new LandingGeometry(plan.Rwy);
                double along = lg.Along(lat, lon);
                if (Math.Abs(along - xa) > 120)
                    found.Add(new Finding(w.Icao, "LINEUP_WRONG_PLACE", where, $"\"Lined up\" {along:F0} m from the runway start, expected {xa:F0} m ({plan.LineupNote})"));
            }
            if (st == TaxiGuidanceState.HoldShort)
            {
                if (!holdOnRunwayReported && TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon) is { } hr0)
                {
                    holdOnRunwayReported = true;
                    found.Add(new Finding(w.Icao, "HOLD_STOP_ON_RUNWAY_PAVEMENT", where, $"stopped for a hold short at t={t:F0}s standing on runway {hr0.Name1}/{hr0.Name2} ({lat:F6},{lon:F6})"));
                }
#if !OLDAPP
                // A key flight's hold at its stop, any frame of it on runway pavement (the key never puts a stop back there).
                if (keys != null && HoldNodeNow() == keys.StopNodeId && TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon) is { } kr)
                    keys.HeldOnRunway ??= $"{kr.Name1}/{kr.Name2} at t={t:F0}s ({lat:F6},{lon:F6})";
#endif
                lastHoldOdo = odo;
                if (gs < 0.3) { holdWait += 0.1; if (holdWait > 2) { mgr.ContinuePastHoldShort(); holdWait = 0; } }
            }
            if (st == TaxiGuidanceState.Arrived || st == TaxiGuidanceState.Inactive && t > 2) { done = true; doneState = st.ToString(); if (gs < 0.5) break; }
            if (linedUp) { done = true; doneState = "Lined up"; if (gs < 0.5) break; }
            if (st == TaxiGuidanceState.LiningUp)
            {
                if (lineupStartOdo < 0) lineupStartOdo = odo;
                if (odo - lineupStartOdo > 400 + stubLenM && !linedUp)
                {
                    found.Add(new Finding(w.Icao, "LINEUP_NEVER_ALIGNED", where, $"{odo - lineupStartOdo:F0} m of lineup guidance without \"Lined up\" ({lat:F6},{lon:F6}) hdg={hdg:F0}"));
                    done = true; doneState = "lineup failed"; break;
                }
            }

            double target = st switch
            {
                _ when linedUp => 0,
                TaxiGuidanceState.HoldShort => 0,
                TaxiGuidanceState.Arrived => 0,
                TaxiGuidanceState.LiningUp => linedUp ? 0 : 6,
                _ => pilot.TaxiTarget(pan, cap),
            };
            pilot.Precision = st == TaxiGuidanceState.LiningUp;
#if !OLDAPP
            // The miss flight: at the junction the pilot takes the charted way (as FlyWrongTurn drives a
            // branch), until guidance answers with a new route or 150 m have gone by; then the tone again.
            if (divert != null && !diverted && !diverting && st == TaxiGuidanceState.Taxiing
                && Geo.Dist(lat, lon, plan.DivertAt.lat, plan.DivertAt.lon) < 5)
            {
                diverting = true; divertStartOdo = odo;
                if (trace) Console.WriteLine($"  {t,6:F1}s DIVERT along {string.Join(" ", divert.Select(n => "n" + n.NodeId))}");
            }
            // An excursion also ends before it would take the aircraft within a runway's clear margin (30 m ahead):
            // a pilot off on an excursion never drives onto a runway, and the flight measures the route change.
            if (diverting && (routeChangedSaid || odo - divertStartOdo > (plan.DivertIsExcursion ? 300 : 150) || divertIdx >= divert!.Count || st != TaxiGuidanceState.Taxiing
                              || plan.DivertIsExcursion && Geo.Move(lat, lon, hdg, 30) is var (aLa, aLo) && !ClearOfRunwaysAt(w.Graph, aLa, aLo)))
            {
                diverting = false; diverted = true; pilot = new PilotModel(hdg, gs);
                if (trace) Console.WriteLine($"  {t,6:F1}s DIVERT ends after {odo - divertStartOdo:F0} m{(routeChangedSaid ? ", the route changed" : "")}");
            }
            if (diverting)
            {
                // An excursion aims at the first node of its path 25 m or more away (at 10 kt and 12 degrees a second
                // the turn radius is about 24 m, so a nearer node is circled, never reached) and holds its heading once
                // past the end, where the miss flight keeps steering at the last node: the excursion's job is to stay
                // off the route until guidance re-routes.
                if (plan.DivertIsExcursion)
                {
                    while (divertIdx < divert!.Count && Geo.Dist(lat, lon, divert[divertIdx].Latitude, divert[divertIdx].Longitude) < 25) divertIdx++;
                    if (divertIdx >= divert.Count) { excursionPastEnd = true; divertIdx = divert.Count - 1; }
                }
                else
                    while (divertIdx < divert!.Count - 1 && Geo.Dist(lat, lon, divert[divertIdx].Latitude, divert[divertIdx].Longitude) < 6) divertIdx++;
                double dErr = excursionPastEnd ? 0 : Geo.Norm(TaxiPlanBearing(lat, lon, divert[divertIdx]) - hdg);
                hdg = (hdg + Math.Clamp(dErr, -12 * 0.1, 12 * 0.1) + 360) % 360;
                gs += Math.Clamp(10 - gs, -0.4, 0.15);
            }
            else
#endif
            pilot.Step(ref hdg, ref gs, pan, target, 0.1, holdHeading: null);
            double stepM = gs * 0.5144 * 0.1;
            double pLat = lat, pLon = lon;
            (lat, lon) = Geo.Move(lat, lon, hdg, stepM);
            odo += stepM;
            if (stepM > 0 && w.CrossesPaintedHold(pLat, pLon, lat, lon)) paintCross.Add((odo, lat, lon));

            // Runway entered?
            var rUnder = TaxiGuidanceHarnessBridge.RunwayUnder(w.Graph, lat, lon);
            if (pendingBehind is { } pb)
            {
                if (rUnder != null && DepartureRig.NamesRunway(pb.text, rUnder)) pendingBehind = null;
                else if (odo - pb.odo > 150)
                {
                    if (!holdCallBehindReported) { holdCallBehindReported = true; found.Add(new Finding(w.Icao, "HOLD_LINE_CALLED_BEHIND", where, pb.what)); }
                    pendingBehind = null;
                }
            }
            if (rUnder != null) pendingCrossing = null;
            else if (pendingCrossing is { } pc0 && odo - pc0.odo > 200)
            {
                if (!crossingNoRunwayReported)
                {
                    crossingNoRunwayReported = true;
                    found.Add(new Finding(w.Icao, "CROSSING_CALLED_NO_RUNWAY", where, $"\"{pc0.text}\" and no runway in the next {odo - pc0.odo:F0} m"));
                }
                pendingCrossing = null;
            }
            if (rUnder == null && onRunway != null) { leftRunway = onRunway; leftRunwayOdo = odo; }
            bool flicker = rUnder != null && rUnder == leftRunway && odo - leftRunwayOdo < 150;
            // An ENTRY comes from off the pavement: rolling from one runway's pavement straight onto
            // another's (a crossing through a runway intersection, CYYC C over 35L/11) is one crossing.
            if (rUnder != null && onRunway == null && !flicker)
            {
                string rname = $"{rUnder.Name1}/{rUnder.Name2}";
                bool isDest = plan.DestRunway != null && (rUnder.Name1 == plan.DestRunway || rUnder.Name2 == plan.DestRunway);
                if (!isDest) unannouncedCheck.Add((odo, rUnder));
                bool warned = summary.Contains(rUnder.Name1) && summary.Contains("no hold short", StringComparison.OrdinalIgnoreCase)
                              || summary.Contains(rUnder.Name2) && summary.Contains("no hold short", StringComparison.OrdinalIgnoreCase);
                // The stop that should have been for THIS runway: the last one not yet followed by a runway.
                var hs = stops.LastOrDefault(x => !x.Used);
                if (hs != null)
                {
                    hs.Used = true;
                    if (!DepartureRig.NamesRunway(hs.Text, rUnder) && DepartureRig.MentionsRunway(hs.Text))
                        found.Add(new Finding(w.Icao, "HOLD_NAMED_OTHER_RUNWAY", where, $"\"{hs.Text}\" then entered runway {rname} {odo - hs.Odo:F0} m later"));
                    else if (odo - hs.Odo > 220 && !(isDest && reachedLineup))
                        found.Add(new Finding(w.Icao, "HOLD_FAR_BEFORE_RUNWAY", where, $"held {odo - hs.Odo:F0} m before entering runway {rname}: \"{hs.Text}\""));
                    // Audit against the painted lines (VP_AMDBDIR): the stop is on the runway side
                    // of EVERY painted line — one crossed well before the stop, none still ahead of
                    // it. 30 m covers the X-Plane/MSFS offset; a second, outer line (CAT II/III
                    // painted with the ordinary hold type) cannot trigger it, because the inner
                    // line is then still ahead of the stop.
                    const double PAINT_SLACK_M = 30;
                    // Only lines that can be THIS runway's holding point: off its pavement, within
                    // 250 m of its centreline, on the same side as the stop. A line crossed for an
                    // earlier runway, or one painted ACROSS this runway (EGKK 08L/26R is used as a
                    // taxiway, and X-Plane paints a line on its centreline), is not.
                    double stopSide = Geo.SignedLateralM(hs.Lat, hs.Lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                    var paintCrossOdo = paintCross.Where(x =>
                    {
                        double l = Geo.SignedLateralM(x.lat, x.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                        if (!(Math.Abs(l) > rUnder.HalfWidthMeters && Math.Abs(l) < 250 && Math.Sign(l) == Math.Sign(stopSide))) return false;
                        // …and nearer this runway than any other: close parallels (EGKK 08L/08R,
                        // ~200 m apart) put the line for vacating one inside the other's window.
                        double mine = Geo.LateralToSegmentM(x.lat, x.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2);
                        return w.Graph.RunwayCenterlines.All(o => ReferenceEquals(o, rUnder)
                            || Geo.LateralToSegmentM(x.lat, x.lon, o.Lat1, o.Lon1, o.Lat2, o.Lon2) >= mine);
                    }).Select(x => x.odo).ToList();
                    bool lineAheadOfStop = paintCrossOdo.Any(x => x > hs.Odo - PAINT_SLACK_M && x <= odo);
                    double lastBefore = paintCrossOdo.Where(x => x <= hs.Odo - PAINT_SLACK_M && x > hs.Odo - 300).DefaultIfEmpty(double.NaN).Max();
                    if (!lineAheadOfStop && !double.IsNaN(lastBefore))
                    {
                        // Did OUR navdata have a hold node at that painted line (the app chose not to
                        // stop there), or none at all (the two sceneries disagree)?
                        var pc = paintCross.First(x => x.odo == lastBefore);
                        double navHoldM = w.Graph.Nodes.Values
                            .Where(n => n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort)
                            .Select(n => Geo.Dist(pc.lat, pc.lon, n.Latitude, n.Longitude)).DefaultIfEmpty(9999).Min();
                        double stopLat = Math.Abs(Geo.SignedLateralM(hs.Lat, hs.Lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2));
                        double paintLat = Math.Abs(Geo.SignedLateralM(pc.lat, pc.lon, rUnder.Lat1, rUnder.Lon1, rUnder.Lat2, rUnder.Lon2));
                        found.Add(new Finding(w.Icao, navHoldM < 40 ? "HOLD_PAST_PAINT_NAVDATA_HOLD_UNUSED" : "HOLD_PAST_PAINT_NO_NAVDATA_HOLD", where,
                            $"stopped {hs.Odo - lastBefore:F0} m past the painted hold line for runway {rname}; stop {stopLat:F0} m from the centreline, paint {paintLat:F0} m, nearest navdata hold {navHoldM:F0} m from the paint ({hs.Lat:F6},{hs.Lon:F6}): \"{hs.Text}\""));
                    }
                    else if (lineAheadOfStop && paintCrossOdo.Where(x => x > hs.Odo + PAINT_SLACK_M && x <= odo).DefaultIfEmpty(double.NaN).Min() is double firstAfter
                             && !double.IsNaN(firstAfter) && firstAfter - hs.Odo > 120)
                        found.Add(new Finding(w.Icao, "HOLD_WELL_SHORT_OF_PAINT", where, $"stopped {firstAfter - hs.Odo:F0} m short of the first painted hold line for runway {rname}: \"{hs.Text}\""));
                }
                if (odo - lastHoldOdo > 250 && !plan.StartsOnRunway && !(isDest && st == TaxiGuidanceState.LiningUp))
                    found.Add(new Finding(w.Icao, warned ? "RUNWAY_ENTERED_UNHELD_WARNED" : (isDest ? "DEST_RUNWAY_ENTERED_UNHELD" : "RUNWAY_ENTERED_UNHELD"),
                        where, $"entered runway {rname} at t={t:F0}s, {odo - lastHoldOdo:F0} m after the last hold (state {st})"));
            }
            onRunway = rUnder;
            for (int ui = unannouncedCheck.Count - 1; ui >= 0; ui--)
            {
                var (eOdo, eRwy) = unannouncedCheck[ui];
                if (odo - eOdo < 100) continue;
                unannouncedCheck.RemoveAt(ui);
                if (!crossedUnannouncedReported && !crossingCalls.Any(c => c.odo >= eOdo - 150 && c.odo <= eOdo + 100 && DepartureRig.NamesRunway(c.text, eRwy)))
                {
                    crossedUnannouncedReported = true;
                    found.Add(new Finding(w.Icao, "RUNWAY_CROSSED_UNANNOUNCED", where, $"entered runway {eRwy.Name1}/{eRwy.Name2} at {eOdo:F0} m with no \"Crossing\" naming it"));
                }
            }

            double off = w.OffPavementMetres(lat, lon);
            if (off < 3) joinedPavement = true;
            if (!joinedPavement) off = 0;
            if (off > 12) { if (offSince < 0) offSince = t; } else offSince = -1;
            if (st == TaxiGuidanceState.LiningUp) { if (off > maxOffLineup) { maxOffLineup = off; lineupWhere = $"t={t:F0}s ({lat:F6},{lon:F6})"; } }
            else if (off > maxOff) { maxOff = off; offWhere = $"t={t:F0}s ({lat:F6},{lon:F6}) state={st} seg={mgr.SegmentIndexForHarness}"; }
#if !OLDAPP
            if (legLengthM > 0) legNearestM = Math.Min(legNearestM, Geo.Dist(lat, lon, legMid.lat, legMid.lon));
            if (legLengthM > 0 && mgr.RouteForHarness is { } rNow && mgr.SegmentIndexForHarness is int sNow
                && sNow >= 0 && sNow < rNow.Segments.Count && rNow.Segments[sNow].IsOffChartTurn)
            {
                legReached = true;
                if (off > legMaxOff) { legMaxOff = off; legOffWhere = $"t={t:F0}s ({lat:F6},{lon:F6})"; }
            }
#endif
            if (trace && ((int)(t * 10)) % 10 == 0)
                Console.WriteLine($"  {t,6:F1}s {st,-12} seg={mgr.SegmentIndexForHarness} ({lat:F6},{lon:F6}) hdg={hdg:F0} gs={gs:F1} pan={pan:F2} off={off:F0}"
                    + (plan.Rwy is { } pr ? $" rwy[{new LandingGeometry(pr).Describe(lat, lon, 0)}] halfW={new LandingGeometry(pr).HalfWidthM:F0}" : ""));
        }
        if (done)
            foreach (var hs in stops.Where(x => !x.Used && DepartureRig.MentionsRunway(x.Text)))
                found.Add(new Finding(w.Icao, "HOLD_WITHOUT_RUNWAY", where, $"stopped at t={hs.T:F0}s \"{hs.Text}\" and never entered a runway after it"));
        if (done && !crossedUnannouncedReported
            && unannouncedCheck.FirstOrDefault(e => !crossingCalls.Any(c => c.odo >= e.odo - 150 && DepartureRig.NamesRunway(c.text, e.rwy))) is { rwy: not null } eEnd)
            found.Add(new Finding(w.Icao, "RUNWAY_CROSSED_UNANNOUNCED", where, $"entered runway {eEnd.rwy.Name1}/{eEnd.rwy.Name2} at {eEnd.odo:F0} m with no \"Crossing\" naming it"));
        if (done && pendingBehind is { } pbEnd && !holdCallBehindReported)
            found.Add(new Finding(w.Icao, "HOLD_LINE_CALLED_BEHIND", where, pbEnd.what));
        if (done && pendingCrossing is { } pcEnd && !crossingNoRunwayReported)
            found.Add(new Finding(w.Icao, "CROSSING_CALLED_NO_RUNWAY", where, $"\"{pcEnd.text}\" and no runway before the flight ended {odo - pcEnd.odo:F0} m later"));
        if (!done) found.Add(new Finding(w.Icao, "TAXI_NOT_ARRIVED", where, $"after {t:F0}s ({odo:F0} m of {route.TotalDistanceMeters:F0} m) state={mgr.State} seg={mgr.SegmentIndexForHarness}/{route.Segments.Count} [{string.Join(", ", stateLog.TakeLast(3))}]"));
        if (maxOff > 12) found.Add(new Finding(w.Icao, "TAXI_OFF_PAVEMENT", where, $"{maxOff:F0} m off the pavement following the tone, {offWhere}"));
        if (maxOffLineup > 12) found.Add(new Finding(w.Icao, "LINEUP_LEAVES_MAPPED_PAVEMENT", where, $"{maxOffLineup:F0} m from any mapped taxi edge or runway while following the lineup tone, {lineupWhere}"));
#if !OLDAPP
        if (plan.DivertPath != null) recalcs = 0;   // a deliberate miss is ANSWERED with a route change
#endif
        if (recalcs > 0) found.Add(new Finding(w.Icao, "TAXI_RECALC_WHILE_FOLLOWING", where, $"{recalcs} route change(s) while following the tone"));
#if !OLDAPP
        if (plan.TurnPicks is { Count: > 0 } && plan.DivertPath == null)
        {
            if (pickSaidMissed || pickSaidNotKept)
                found.Add(new Finding(w.Icao, "TURN_PICK_LOST_WHILE_FOLLOWING", where, $"\"{(pickSaidMissed ? "was missed" : "could not be kept")}\" said while the pilot followed the tone"));
            if (legLengthM >= 40 && legReached && !legSaid && !legOpensTheRoute)
                found.Add(new Finding(w.Icao, "OFFCHART_LEG_NOT_ANNOUNCED", where, $"a {legLengthM:F0} m off-chart leg was taxied with no \"Off-chart leg\" sentence"));
            // 25 m: a pilot rounds the corner of a 20 to 30 m leg by 13 to 19 m and has still taxied it.
            // Not judged when the route itself begins well away from the aircraft (an apron start).
            if (legLengthM >= 20 && done && legNearestM > 25
                && Geo.Dist(plan.StartLat, plan.StartLon, route.Segments[0].FromNode.Latitude, route.Segments[0].FromNode.Longitude) < 50)
                found.Add(new Finding(w.Icao, "OFFCHART_LEG_NOT_TAXIED", where, $"arrived, but never came closer than {legNearestM:F0} m to the middle of the {legLengthM:F0} m off-chart leg"));
            if (legMaxOff > 12)
                found.Add(new Finding(w.Icao, "OFFCHART_LEG_OFF_PAVEMENT", where, $"{legMaxOff:F0} m beyond the nearest taxi edge on the off-chart leg, {legOffWhere}"));
        }
        else if (plan.DivertPath != null && plan.DivertIsExcursion)
        {
            // An excursion off the route before the picked turn: guidance answers it with a route that keeps the
            // pick (the kept tail), never with "was missed" or "could not be kept".
            if (!diverted && !diverting) found.Add(new Finding(w.Icao, "XC_EXCURSION_NOT_FLOWN", where, "the pilot never came to the junction the excursion leaves from"));
            else if (!routeChangedSaid) found.Add(new Finding(w.Icao, "XC_EXCURSION_NO_REROUTE", where, "left the route before the turn; guidance never changed the route"));
            else if (pickSaidMissed || pickSaidNotKept)
                found.Add(new Finding(w.Icao, "XC_EXCURSION_PICK_LOST", where, $"left the route before the turn; \"{(pickSaidMissed ? "was missed" : "could not be kept")}\" said"));
        }
        else if (plan.DivertPath != null)
            found.Add(!diverted && !diverting ? new Finding(w.Icao, "TURN_MISS_NOT_FLOWN", where, "the pilot never came to the junction to take the charted way")
                : !routeChangedSaid ? new Finding(w.Icao, "TURN_MISS_NO_REROUTE", where, "took the charted way; guidance never changed the route")
                : !pickSaidMissed && !pickSaidNotKept ? new Finding(w.Icao, "TURN_MISS_NOT_SAID", where, "took the charted way; the route changed with no word about the picked turn")
                : new Finding(w.Icao, pickSaidMissed ? "TURN_MISS_SAID_MISSED" : "TURN_MISS_SAID_NOT_KEPT", where, "took the charted way; the route change said so"));

        lastFlightHoldNodes = holdNodes;
        // A key flight is judged by what its key was pressed for, not by a cleared flight's expectations (the Hold short
        // key puts the stop back, so it is held there and its line is the watch's after the Continue).
        if (xc != null && keys == null) found.AddRange(XcFindings(w, where, xc, holdNodes, plan.XcBaseHoldNodes, done));
        if (keys != null) found.AddRange(KeyFindings(w, where, keys, holdNodes, done));
#endif
    }
    catch (Exception ex) { found.Add(new Finding(w.Icao, "TAXI_EXCEPTION", where, ex.GetType().Name + ": " + ex.Message)); }
    finally { try { mgr.StopGuidance(); mgr.Dispose(); } catch { } }
    return found;
}

#if !OLDAPP
/// <summary>
/// What a flight with its first crossing cleared did there (XC_ codes, docs/virtual-pilot.md): no stop at a fully
/// cleared crossing; its line said once, within 100 m of the stop, after a countdown that said "cleared to cross";
/// every other stop of the same flight with nothing cleared still made, in the same order; and every route change
/// before the stop either cleared it again at the same node or, where the new route stops for that runway
/// elsewhere, said so ("is a stop again"), never carrying the clearance to another crossing.
/// </summary>
static List<Finding> XcFindings(World w, string where, XcFlight xc, List<int> holdNodes, List<int>? baseHoldNodes, bool done)
{
    var fs = new List<Finding>();
    int stopId = xc.StopNode?.NodeId ?? -1;
    string at = $"runway {xc.Runway} at n{stopId} (\"{xc.Label}\", {xc.Outcome})";
    xc.HeldAt = holdNodes.Count(n => n == stopId);
    if (baseHoldNodes != null)
    {
        xc.BaseHeldAt = baseHoldNodes.Count(n => n == stopId);
        var baseOther = baseHoldNodes.Where(n => n != stopId).ToList();
        var nowOther = holdNodes.Where(n => n != stopId).ToList();
        xc.OtherStopsSame = baseOther.SequenceEqual(nowOther);
        if (xc.OtherStopsSame == false)
            fs.Add(new Finding(w.Icao, "XC_OTHER_STOPS_DIFFER", where,
                $"{at}: with nothing cleared the other stops were [{string.Join(" ", baseOther.Select(n => "n" + n))}], now [{string.Join(" ", nowOther.Select(n => "n" + n))}]"));
    }
    switch (xc.Outcome)
    {
        case "LOAD_REFUSED": return fs;   // the flight's own TAXI_ROUTE_REFUSED / CLEARANCE_ROUTE_FAILED says why
        case "NOT_APPLIED": fs.Add(new Finding(w.Icao, "XC_NOT_APPLIED", where, $"runway {xc.Runway}: the load cleared no crossing")); return fs;
        case "NO_STOP": return fs;
    }
    // Once a route change has left the crossing no longer cleared at its node (Moved), nothing more is due there: the
    // route change's own finding (XC_RECALC_*) says what became of it.
    bool fully = xc.Outcome is "CLEARED" or "START";
    if (fully && xc.HeldAt > 0 && !xc.Moved)
        fs.Add(new Finding(w.Icao, "XC_HELD_AT_CLEARED_CROSSING", where, $"{at}: entered HoldShort there {xc.HeldAt} time(s)"));
    if (xc.Line == 0 && xc.Reached && !xc.Moved)
        fs.Add(new Finding(w.Icao, "XC_LINE_NOT_SAID", where, $"{at}: came within 30 m of the stop and no \"Crossing runway {xc.Runway}\" within 100 m of it{(done ? "" : " (the flight did not arrive)")}"));
    else if (xc.Line == 0 && !xc.Moved)
        fs.Add(new Finding(w.Icao, "XC_STOP_NOT_REACHED", where, $"{at}: never within 30 m of the stop; route changes [{string.Join(",", xc.Recalcs)}]"));
    if (xc.Line > 1)
        fs.Add(new Finding(w.Icao, "XC_LINE_SAID_TWICE", where, $"{at}: {xc.Line} lines within 100 m of the stop"));
    if (xc.Outcome == "CLEARED" && xc.Line > 0 && xc.ApproachBeforeLine == 0 && !xc.LineAtGuidanceStart)
        fs.Add(new Finding(w.Icao, "XC_APPROACH_NOT_SAID", where,
            $"{at}: the line \"{xc.LineText}\" came with no \"… cleared to cross.\" before it (stop segment {xc.StopSegmentM:F0} m)"));
    if (xc.Early > 0)
        fs.Add(new Finding(w.Icao, "XC_CROSSING_SAID_BEFORE_ITS_SEGMENT", where,
            $"{at}: \"{xc.EarlyText}\" said before guidance reached the stop's segment (the incursion watch's on-route line), then the line \"{xc.LineText}\""));
    fs.AddRange(XcRecalcFindings(w, where, xc, at));
    return fs;
}

/// <summary>
/// What a --crossing-keys flight did at its first crossing (KEY_ codes, docs/virtual-pilot.md). EARLY: Continue pressed
/// right after the first countdown call cleared it ("Cleared across runway R. No stop."), no HoldShort there, and its
/// "Crossing …" line said once at the line. HOLD: the Hold short key pressed right after the first "… cleared to
/// cross." put the stop back ("Holding short of …"), HoldShort was entered there, and never with the aircraft on runway
/// pavement. PAST: the key pressed just past the line said "Past the hold line of …" and no HoldShort followed there.
/// A flight whose trigger never came says so (_NOT_PRESSED), with nothing else judged.
/// </summary>
static List<Finding> KeyFindings(World w, string where, KeyFlight k, List<int> holdNodes, bool done)
{
    var fs = new List<Finding>();
    k.HeldAt = holdNodes.Count(n => n == k.StopNodeId);
    string at = $"runway {k.Runway} at n{k.StopNodeId} (\"{k.Label}\")";
    string notArrived = done ? "" : " (the flight did not arrive)";
    void Flag(string code, string detail) => fs.Add(new Finding(w.Icao, code, where, $"{at}: {detail}"));
    switch (k.Variant)
    {
        case KeyFlight.Early:
            if (!k.Pressed) { Flag("KEY_EARLY_NOT_PRESSED", "no countdown call for the stop heard on its segment" + notArrived); break; }
            if (!k.Answer.Contains("Cleared across runway", StringComparison.Ordinal) || !k.Answer.Contains("No stop.", StringComparison.Ordinal))
                Flag("KEY_EARLY_NOT_CLEARED", $"Continue {k.PressNote()} answered \"{k.Answer}\"");
            if (k.HeldAt > 0) Flag("KEY_EARLY_HELD_AT_STOP", $"entered HoldShort there {k.HeldAt} time(s) after Continue {k.PressNote()}");
            if (k.Xc is { } x && !x.Moved)
            {
                if (x.Line == 0 && x.Reached) Flag("KEY_EARLY_LINE_NOT_SAID", $"came within 30 m of the stop and no \"Crossing runway {k.Runway}\" within 100 m of it{notArrived}");
                if (x.Line > 1) Flag("KEY_EARLY_LINE_SAID_TWICE", $"{x.Line} lines within 100 m of the stop");
            }
            break;
        case KeyFlight.Hold:
            if (!k.Pressed) { Flag("KEY_HOLD_NOT_PRESSED", "no \"… cleared to cross.\" heard for it" + notArrived); break; }
            if (!k.Restored) { Flag("KEY_HOLD_NOT_RESTORED", $"the key {k.PressNote()} answered \"{k.Answer}\""); break; }
            if (k.HeldAt == 0) Flag("KEY_HOLD_NOT_HELD_AT_STOP", $"the stop put back {k.PressNote()} was never held{notArrived}");
            if (k.HeldOnRunway is { } on) Flag("KEY_HOLD_ON_RUNWAY_PAVEMENT", $"held at the stop put back on runway {on}");
            break;
        case KeyFlight.Past:
            if (!k.Pressed) { Flag("KEY_PAST_NOT_PRESSED", $"never more than {RouteRunwayCrossings.StopPassedToleranceMetres:F0} m past the stop along the route" + notArrived); break; }
            if (!k.Answer.Contains("Past the hold line of", StringComparison.Ordinal))
                Flag("KEY_PAST_NOT_SAID", $"the key {k.PressNote()} answered \"{k.Answer}\"");
            if (k.HeldAt > 0) Flag("KEY_PAST_HELD_AT_STOP", $"entered HoldShort there {k.HeldAt} time(s) after the key {k.PressNote()}");
            break;
    }
    return fs;
}

/// <summary>Each kind of route change a flight with a cleared crossing saw before its stop, as an XC_ finding.</summary>
static IEnumerable<Finding> XcRecalcFindings(World w, string where, XcFlight xc, string at)
{
    foreach (var r in xc.Recalcs.Distinct())
    {
        string what = r.Replace("+tail", "");
        string code = what switch
        {
            "KEPT" => r.EndsWith("+tail") ? "XC_KEPT_TAIL_KEPT" : "XC_RECALC_KEPT",
            "BACK_AT_SAME_STOP" => "XC_RECALC_STOP_BACK",
            "CLEARED_ELSEWHERE" => "XC_RECALC_CLEARED_ELSEWHERE",
            "STOP_ELSEWHERE" => xc.StopAgainSaid ? "XC_RECALC_STOP_MOVED_SAID" : "XC_RECALC_STOP_MOVED_UNSAID",
            "ENTRY_STOP_ELSEWHERE" => "XC_RECALC_ENTRY_STOP",
            "CROSSED_WITHOUT_STOP" => "XC_RECALC_CROSSED_WITHOUT_STOP",
            _ => "XC_RECALC_NOT_CROSSED",
        };
        yield return new Finding(w.Icao, code, where, $"{at}: a route change before the stop: {r}");
    }
}
#endif

static string Name(LandingExit e) => e.TaxiwayName.Length > 0 ? e.TaxiwayName : $"node{e.NodeId}";
/// <summary>The direction a spoken steering line commands: -1 left, +1 right, 0 none/unclear.</summary>
static int SpokenDirection(string a)
{
    var m = System.Text.RegularExpressions.Regex.Match(a,
        @"\b(?:Curving|[Tt]urn|Sharp turn|Bear|Veer)\s+(left|right)\b|\b(Left|Right) turn ahead\b|\bto the (left|right)\b");
    if (!m.Success) return 0;
    string d = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).ToLowerInvariant();
    return d == "left" ? -1 : 1;
}
/// <summary>Two opposite steering directions spoken in one frame, or a "Curving" line against a hard opposite pan.</summary>
static string? DirectionContradiction(List<string> said, float pan)
{
    var dirs = said.SelectMany(u => System.Text.RegularExpressions.Regex.Split(u, @"(?<=\.)\s+"))
                   .Select(a => (a, d: SpokenDirection(a))).Where(x => x.d != 0).ToList();
    if (dirs.Any(x => x.d < 0) && dirs.Any(x => x.d > 0))
        return string.Join(" / ", dirs.Select(x => $"\"{x.a}\""));
    foreach (var (a, d) in dirs)
        if (a.StartsWith("Curving", StringComparison.OrdinalIgnoreCase) && Math.Abs(pan) >= 0.9f && Math.Sign(pan) == -d)
            return $"\"{a}\" with the tone hard {(pan < 0 ? "left" : "right")}";
    return null;
}
static string RwyLat(World w, double la, double lo)
{
    string best = ""; double bd = double.MaxValue;
    foreach (var c in w.Graph.RunwayCenterlines)
    {
        double dn = (la - c.Lat1) * 111132.0, de = (lo - c.Lon1) * 111132.0 * Math.Cos(la * Math.PI / 180);
        double h = c.HeadingDeg1 * Math.PI / 180;
        double along = dn * Math.Cos(h) + de * Math.Sin(h);
        double len = TaxiGraph.CalculateDistanceMeters(c.Lat1, c.Lon1, c.Lat2, c.Lon2);
        if (along < -300 || along > len + 300) continue;
        double xt = Math.Abs(-dn * Math.Sin(h) + de * Math.Cos(h));
        if (xt < bd) { bd = xt; best = c.Name1; }
    }
    return $"{best}:{bd:F0}";
}
static bool Burst() => Environment.GetEnvironmentVariable("VP_BURST") == "1";
// The app's "(sharp turn back)" flag; an app without it (MAINAPP) never flags one.
static bool ExitTurnsBack(LandingExit e)
{
#if MAINAPP
    return false;
#else
    return e.RequiresTurnBack;
#endif
}
static string? StartWarning(TaxiGuidanceManager m)
{
#if MAINAPP
    return null;
#else
    return m.LastRouteStartWarning;
#endif
}
// Shortest distance (m) from a point to a polyline — harness-local, so it does not depend on the app under test.
static double PathDist(IReadOnlyList<(double Lat, double Lon)> pts, double lat, double lon)
{
    double best = double.MaxValue;
    double kx = 111320.0 * Math.Cos(lat * Math.PI / 180), ky = 110540.0;
    for (int i = 0; i + 1 < pts.Count; i++)
    {
        double ax = (pts[i].Lon - lon) * kx, ay = (pts[i].Lat - lat) * ky;
        double bx = (pts[i + 1].Lon - lon) * kx, by = (pts[i + 1].Lat - lat) * ky;
        double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
        double t = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / l2, 0, 1);
        double px = ax + t * dx, py = ay + t * dy;
        best = Math.Min(best, Math.Sqrt(px * px + py * py));
    }
    if (pts.Count == 1) best = Math.Sqrt(Math.Pow((pts[0].Lon - lon) * kx, 2) + Math.Pow((pts[0].Lat - lat) * ky, 2));
    return best;
}
static double Kt(double ms) => ms / 0.5144;

static List<string> BusiestAirports(string db, int n)
{
    var list = new List<string>();
    using var conn = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
    conn.Open();
    using var c = new SqliteCommand(@"SELECT a.ident FROM airport a JOIN taxi_path t ON t.airport_id=a.airport_id
        GROUP BY a.airport_id ORDER BY COUNT(*) DESC LIMIT @n", conn);
    c.Parameters.AddWithValue("@n", n * 2);
    using var r = c.ExecuteReader();
    while (r.Read()) list.Add(r.GetString(0));
    return list.Distinct().Take(n).ToList();
}

// ── pieces ───────────────────────────────────────────────────────────────────


sealed record Finding(string Icao, string Code, string Where, string Detail)
{
    public override string ToString() => $"{Code,-28} {Icao,-5} {Where} — {Detail}";
}

sealed class Options
{
    public string Mode = "all"; public int Airports = 100; public List<string>? Icaos;
    public (int i, int n)? Shard; public int TaxiPerAirport = 6; public string Out = ""; public string? Trace;
    public string? Sim, Db;
#if !OLDAPP
    /// <summary>--clear-first-crossing (taxi, turnpicks): each flight is flown a second time with the first runway its route crosses cleared.</summary>
    public bool ClearFirstCrossing;
    /// <summary>--crossing-keys (taxi, turnpicks): each flight is flown three times more, pressing Continue or the Hold short key at its first crossing (FlyCrossingKeys).</summary>
    public bool CrossingKeys;
#endif
    public static Options? Parse(string[] a)
    {
        if (a.Length == 0) { Console.WriteLine("usage: VirtualPilot landings|taxi|all [options] (see source header)"); return null; }
        var o = new Options { Mode = a[0].ToLowerInvariant() };
        for (int i = 1; i < a.Length; i++)
        {
            switch (a[i])
            {
                case "--airports": o.Airports = int.Parse(a[++i]); break;
                case "--icao": o.Icaos = a[++i].Split(',').Select(x => x.Trim().ToUpperInvariant()).ToList(); break;
                case "--shard": { var p = a[++i].Split('/'); o.Shard = (int.Parse(p[0]), int.Parse(p[1])); break; }
                case "--taxi-per-airport": o.TaxiPerAirport = int.Parse(a[++i]); break;
                case "--out": o.Out = a[++i]; break;
                case "--trace": o.Trace = a[++i]; break;
                case "--sim": o.Sim = a[++i]; break;
                case "--db": o.Db = a[++i]; break;
#if !OLDAPP
                case "--clear-first-crossing": o.ClearFirstCrossing = true; break;
                case "--crossing-keys": o.CrossingKeys = true; break;
#endif
                default: Console.WriteLine($"unknown option {a[i]}"); return null;
            }
        }
        if (o.Out == "") o.Out = $"virtualpilot_{o.Mode}{(o.Shard is { } s ? $"_{s.i}of{s.n}" : "")}.txt";
        return o;
    }
}

/// <summary>
/// Which navdata database a run flies. A file given by path wins; else the named simulator's
/// database; else the MSFS 2020 one when it is installed (the rounds recorded in
/// docs/virtual-pilot.md flew it) and the MSFS 2024 one when it is not. The databases are found
/// through <see cref="DatabasePathResolver"/>, as the app finds them ([CORE-13]).
/// </summary>
static class NavdataChoice
{
    public static (string Path, string Sim)? Resolve(string? dbArg, string? simArg)
    {
        string? sim = null;
        if (!string.IsNullOrWhiteSpace(simArg))
        {
            sim = simArg.Trim().ToUpperInvariant() switch
            {
                "FS2020" or "MSFS2020" or "2020" => "FS2020",
                "FS2024" or "MSFS2024" or "2024" => "FS2024",
                _ => null,
            };
            if (sim == null) { Console.WriteLine($"unknown simulator '{simArg}': use fs2020 or fs2024"); return null; }
        }
        if (!string.IsNullOrWhiteSpace(dbArg))
        {
            string file = Path.GetFullPath(dbArg);
            if (!File.Exists(file)) { Console.WriteLine($"DB not found: {file}"); return null; }
            // The simulator name only labels the run; with no --sim the file's own name decides it.
            return (file, sim ?? (Path.GetFileName(file).Contains("2024", StringComparison.Ordinal) ? "FS2024" : "FS2020"));
        }
        sim ??= DatabasePathResolver.ExistsAnywhere("FS2020") || !DatabasePathResolver.ExistsAnywhere("FS2024") ? "FS2020" : "FS2024";
        string path = DatabasePathResolver.ResolveExistingDatabasePath(sim);
        if (!File.Exists(path))
        {
            Console.WriteLine($"DB not found: {path}" + (DatabasePathResolver.ExistsAnywhere(sim == "FS2020" ? "FS2024" : "FS2020")
                ? $" (the other simulator's database is installed: pass --sim {(sim == "FS2020" ? "fs2024" : "fs2020")})" : ""));
            return null;
        }
        return (path, sim);
    }
}

/// <summary>Captures speech instead of speaking it.</summary>
sealed class Cap : ScreenReaderAnnouncer
{
    private readonly List<string> _pending = new();
    private readonly Func<DateTime> _now;
    public DateTime LastSlowCall = DateTime.MinValue;
    public Cap(Func<DateTime> now) : base(IntPtr.Zero) { _now = now; }
    private void Say(string m)
    {
        _pending.Add(m);
        if (m.Contains("Slow", StringComparison.OrdinalIgnoreCase)) LastSlowCall = _now();
    }
    public override void Announce(string m) => Say(m);
    public override void AnnounceImmediate(string m) => Say(m);
    public override void AnnounceQueued(string m) => Say(m);
    public override void AnnounceWithQueue(string m) => Say(m);
    public List<string> TakeNew() { var l = new List<string>(_pending); _pending.Clear(); return l; }
    public bool RecentlyToldToSlow(DateTime now) => (now - LastSlowCall).TotalSeconds < 6;
}

/// <summary>
/// When the hold-short countdown comes, measured ALONG THE ROUTE by the harness itself: the
/// distance through every segment to the next stop (the first segment at or after the cursor that
/// ends at a hold short), against the countdown's outer tier (15 s at the speed then, 300 to 600 ft).
/// Flags a stop whose first countdown line came more than 30 m after the outer tier was crossed
/// (HOLD_COUNTDOWN_LATE) or never came (HOLD_COUNTDOWN_MISSING), and a "Passing the … hold line." or
/// "Crossing …" said about the line the aircraft then stopped at (HOLD_CALLED_PASSING_BEFORE_STOP).
/// Only a stop whose outer tier was crossed while taxiing is judged: a route that starts inside it
/// has no room for the outer line. With <c>legEnd</c> and <c>cleared</c> (the legends and crossthenhold
/// modes) the next stop can also be the end of a leg with no destination, judged when the leg ends, and a
/// stop the pilot was cleared across, judged once the guidance cursor is past it (it is never stopped at),
/// each by its own countdown words ("Hold short taxiway B in", "Hold at VIKAS in", "Route ends in",
/// "Runway 04L at C in"); a cleared line's own "Crossing runway …" is its call at the line, never a
/// HOLD_CALLED_PASSING_BEFORE_STOP.
/// </summary>
sealed class CountdownTiming
{
    private const double LateSlackM = 30;
    private int _node = -1;
    private StopKind _kind;
    private double _dueAt = double.NaN, _firstAt = double.NaN, _prevAlong = double.NaN;
    private string _firstText = "", _countdownText = "";
    private (double Odo, string Name)? _passLine;
    private bool _lateReported, _missingReported, _passReported;

    /// <summary>What the countdown counts down to: a hold short, a stop cleared across, the end of a leg.</summary>
    public enum StopKind { Hold, Cleared, LegEnd }

    private static readonly System.Text.RegularExpressions.Regex CountdownLine = new(
        @"\b(?:Hold short|End of route|Hold at|Route ends)\b[^.]* in \d|^Runway\b[^.]* in \d", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex PassLine = new(
        @"\b(?:Passing the (.+?) hold line|Crossing (runway [^.]+))\.", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The countdown's outer tier in metres: 15 s at <paramref name="gsKt"/>, 300 to 600 ft.</summary>
    public static double OuterTierM(double gsKt) => Math.Clamp(gsKt * 1.68781 * 15.0, 300.0, 600.0) / 3.28084;

    /// <summary>
    /// The next stop at or after segment <paramref name="idx"/> and the distance to it along the route: a hold short,
    /// with <paramref name="legEnd"/> the route's last segment (a leg with no destination; it wins on its segment), and
    /// with <paramref name="cleared"/> a stop every runway of which was cleared.
    /// </summary>
    public static (int NodeId, double Along, StopKind Kind)? NextStop(TaxiRoute? r, int idx, double lat, double lon,
                                                                      bool legEnd = false, bool cleared = false)
    {
        if (r == null || idx < 0 || idx >= r.Segments.Count) return null;
        double along = Geo.Dist(lat, lon, r.Segments[idx].ToNode.Latitude, r.Segments[idx].ToNode.Longitude);
        for (int k = idx; k < r.Segments.Count; k++)
        {
            if (k > idx) along += r.Segments[k].DistanceMeters;
            if (legEnd && k == r.Segments.Count - 1) return (r.Segments[k].ToNode.NodeId, along, StopKind.LegEnd);
            if (r.Segments[k].IsHoldShortPoint) return (r.Segments[k].ToNode.NodeId, along, StopKind.Hold);
#if !OLDAPP
            if (cleared && r.Segments[k].ClearedRunways.Count > 0) return (r.Segments[k].ToNode.NodeId, along, StopKind.Cleared);
#endif
        }
        return null;
    }

    public void Frame(string icao, string where, List<Finding> found, TaxiGuidanceState st, TaxiRoute? route, int idx,
                      double lat, double lon, double gs, double odo, List<string> said, bool stopped, bool trace,
                      bool legEnd = false, bool cleared = false)
    {
        var next = !stopped && st == TaxiGuidanceState.Taxiing ? NextStop(route, idx, lat, lon, legEnd, cleared) : null;
        // A cleared line is never stopped at: it is judged on the frame the cursor is past it, the frame its own
        // "Crossing runway …" is said, which is no call about a line ahead.
        bool clearedPassed = _node >= 0 && _kind == StopKind.Cleared && st == TaxiGuidanceState.Taxiing && next?.NodeId != _node;
        if (clearedPassed)
        {
            Judge(icao, where, found, odo);
            _node = -1;
        }
        // On the frame that stops, the cursor has already moved past the hold: judge the stop tracked so far.
        if (next is { } ns)
        {
            if (ns.NodeId != _node)
            {
                _node = ns.NodeId; _kind = ns.Kind; _dueAt = _firstAt = _prevAlong = double.NaN; _firstText = _countdownText = "";
            }
            double outer = OuterTierM(gs);
            if (double.IsNaN(_dueAt) && !double.IsNaN(_prevAlong) && _prevAlong >= outer && ns.Along < outer)
            {
                _dueAt = ns.Along;
                if (trace) Console.WriteLine($"  CD outer tier due {ns.Along:F0} m out along the route at {gs:F1} kt (stop n{ns.NodeId})");
            }
            foreach (var a in said)
                if (CountdownLine.IsMatch(a))
                {
                    _countdownText = CountdownText(a);
                    if (double.IsNaN(_firstAt))
                    {
                        _firstAt = ns.Along; _firstText = a;
                        if (trace) Console.WriteLine($"  CD first line {ns.Along:F0} m out along the route");
                    }
                }
            _prevAlong = ns.Along;
        }
        foreach (var a in said)
        {
            if (!clearedPassed && PassLine.Match(a) is { Success: true } m)
                _passLine = (odo, m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            if (stopped && CountdownLine.IsMatch(a))
            {
                _countdownText = CountdownText(a);
                if (double.IsNaN(_firstAt)) { _firstAt = 0; _firstText = a; }
            }
        }
        if (!stopped || _node < 0) return;
        Judge(icao, where, found, odo);
        _node = -1;
    }

    // The countdown's words the pass check reads: the frame's whole utterance for a hold short, as main measured it; for a
    // leg end or a cleared crossing the countdown's own sentence, since the frame's one utterance can carry a "Passing the
    // … hold line." about another line the route passes (EGLL "Hold short taxiway A in 60 metres. Slow down. Passing the
    // runway 27R at A9W hold line.", the leg ending 36 m on), which is no call about the stop.
    private string CountdownText(string utterance)
    {
        if (_kind == StopKind.Hold) return utterance;
        var m = CountdownLine.Match(utterance);
        if (!m.Success) return utterance;
        int end = utterance.IndexOf(". ", m.Index, StringComparison.Ordinal);
        return end < 0 ? utterance[m.Index..] : utterance[m.Index..(end + 1)];
    }

    // The stop tracked so far, as it is reached: held at, the leg ended, or a cleared line passed.
    private void Judge(string icao, string where, List<Finding> found, double odo)
    {
        string reached = _kind switch
        {
            StopKind.LegEnd => "the leg ended",
            StopKind.Cleared => "passed a cleared line",
            _ => "stopped at a hold",
        };
        if (!double.IsNaN(_dueAt))
        {
            if (double.IsNaN(_firstAt))
            {
                if (!_missingReported)
                {
                    _missingReported = true;
                    found.Add(new Finding(icao, "HOLD_COUNTDOWN_MISSING", where, $"{reached} with no countdown line; the outer tier was due {_dueAt:F0} m out"));
                }
            }
            else if (_dueAt - _firstAt > LateSlackM && !_lateReported)
            {
                _lateReported = true;
                found.Add(new Finding(icao, "HOLD_COUNTDOWN_LATE", where, $"{reached}: first countdown line {_firstAt:F0} m out along the route, the outer tier due {_dueAt:F0} m out: \"{_firstText}\""));
            }
        }
        if (_kind != StopKind.Cleared && _passLine is { } p && odo - p.Odo < 80
            && _countdownText.Contains(p.Name, StringComparison.OrdinalIgnoreCase) && !_passReported)
        {
            _passReported = true;
            found.Add(new Finding(icao, "HOLD_CALLED_PASSING_BEFORE_STOP", where, $"\"{p.Name}\" called passed or crossed {odo - p.Odo:F0} m before stopping at that line: \"{_countdownText}\""));
        }
        _passLine = null;
    }
}

/// <summary>
/// Whether a turn's "… now" had an advance notice before it: a "… now" with no "In N, left/right …"
/// heard since the previous "… now" (or the route start, or the last stop), though the aircraft had
/// taxied more than 150 m since then, room for one (TURN_NOW_WITHOUT_ADVANCE). An advance notice spoken
/// in the same frame as its "… now" is dropped by the app, so it was never heard and does not count.
/// An advance notice whose side (left, right or "continue") is not the side of the "… now" that follows
/// it is TURN_ADVANCE_OTHER_WAY ("In 120 metres, continue onto taxiway L." then "Sharp left now").
/// </summary>
sealed class TurnTiming
{
    private const double RoomM = 150;
    private static readonly System.Text.RegularExpressions.Regex Now = new(@"^(?:Turn )?(?:Sharp |Slight )?(left|right) now\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex Advance = new(@"^In [^,]+, (?:sharp |slight )?(left|right|continue)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    // "Taxiway M." on joining a taxiway: a "continue onto taxiway M" notice has no "… now" of its own, and is done.
    private static readonly System.Text.RegularExpressions.Regex Joined = new(@"^Taxiway \S+\.$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private double _since;
    private bool _advance, _reported, _otherWayReported;
    private string? _advanceSide, _advanceText;

    public void Frame(string icao, string where, List<Finding> found, List<string> said, double odo, bool stopped)
    {
        foreach (var a in said)
            foreach (var s in System.Text.RegularExpressions.Regex.Split(a, @"(?<=[.!?])\s+"))
            {
                if (Advance.Match(s) is { Success: true } am)
                {
                    _advanceSide = am.Groups[1].Value.ToLowerInvariant(); _advanceText = s;
                    if (_advanceSide != "continue") _advance = true;
                }
                else if (Joined.IsMatch(s) && _advanceSide == "continue") _advanceSide = null;
                else if (Now.Match(s) is { Success: true } nm)
                {
                    if (!_advance && odo - _since > RoomM && !_reported)
                    {
                        _reported = true;
                        found.Add(new Finding(icao, "TURN_NOW_WITHOUT_ADVANCE", where, $"\"{s}\" with no advance notice after {odo - _since:F0} m of taxiing"));
                    }
                    if (_advanceSide != null && _advanceSide != nm.Groups[1].Value.ToLowerInvariant() && !_otherWayReported)
                    {
                        _otherWayReported = true;
                        found.Add(new Finding(icao, "TURN_ADVANCE_OTHER_WAY", where, $"\"{_advanceText}\" then \"{s}\""));
                    }
                    _advance = false; _since = odo; _advanceSide = null;
                }
            }
        if (stopped) { _advance = false; _since = odo; _advanceSide = null; }
    }
}

/// <summary>
/// A pilot who steers by the tone: yaw rate proportional to the pan (with reaction lag and
/// a turn-rate limit that shrinks with speed), holds heading when the tone is silent, and
/// taxis at 15 kt, slowing for large pans and when told to slow.
/// </summary>
sealed class PilotModel
{
    private readonly Queue<float> _lag = new();
    private double _yawRate;
    private readonly DateTime _t0 = DateTime.MinValue;
    public PilotModel(double hdg, double gs) { for (int i = 0; i < 3; i++) _lag.Enqueue(0f); }

    public double TaxiTarget(float pan, Cap cap)
    {
        double target = 15;
        if (Math.Abs(pan) > 0.6f) target = 9;
        if (cap.RecentlyToldToSlow(MSFSBlindAssist.Utils.SimClock.UtcNow)) target = Math.Min(target, 10);
        return target;
    }

    /// <summary>Set for a precision tone (lineup: silent 0.5°, full pan 15°): the pilot reads the pan as an error estimate and corrects proportionally instead of turning hard on any pan.</summary>
    public bool Precision;

    public void Step(ref double hdg, ref double gs, float pan, double targetKt, double dt, double? holdHeading)
    {
        _lag.Enqueue(pan);
        float heard = _lag.Dequeue();                        // 0.3 s reaction
        double v = Math.Max(1.0, gs * 0.5144);
        double maxRate = Math.Min(15.0, 0.15 * 9.81 / v * 57.2958);
        double want;
        if (heard != 0f && Precision)
        {
            double p = Math.Clamp((Math.Abs(heard) - 0.25) / 0.75, 0, 1);
            double errEst = 1.0 + 14.0 * p * p;                      // inverse of the 1°/15° sqrt pan curve
            want = Math.Sign(heard) * Math.Min(maxRate, errEst / 1.5);
        }
        else if (heard != 0f) want = Math.Sign(heard) * maxRate * (0.5 + 0.5 * Math.Abs(heard));
        else if (holdHeading is { } hh) want = Math.Clamp(Geo.Norm(hh - hdg) * 1.0, -2, 2);
        else want = 0;
        double accel = 12.0 * dt;                             // yaw-rate change per step
        _yawRate += Math.Clamp(want - _yawRate, -accel, accel);
        if (gs < 0.2) _yawRate = 0;
        hdg = (hdg + _yawRate * dt + 360) % 360;
        double dv = targetKt - gs;
        gs += Math.Clamp(dv, -4.0 * dt, 1.5 * dt);
        if (gs < 0) gs = 0;
    }
}

static class Geo
{
    public static double Dist(double la1, double lo1, double la2, double lo2)
    {
        double x = (lo2 - lo1) * Math.Cos((la1 + la2) * Math.PI / 360);
        return Math.Sqrt(x * x + (la2 - la1) * (la2 - la1)) * 111320.0;
    }
    /// <summary>Signed perpendicular metres from the line (1 → 2); + = right of 1→2.</summary>
    public static double SignedLateralM(double la, double lo, double la1, double lo1, double la2, double lo2)
    {
        double c = Math.Cos(la1 * Math.PI / 180);
        double x = (lo - lo1) * c, y = la - la1, dx = (lo2 - lo1) * c, dy = la2 - la1;
        double L = Math.Sqrt(dx * dx + dy * dy);
        return L > 0 ? (x * dy - y * dx) / L * 111320.0 : 0;
    }
    /// <summary>Metres from a point to the segment (1 → 2), flat-earth; the extended line is not used.</summary>
    public static double LateralToSegmentM(double la, double lo, double la1, double lo1, double la2, double lo2)
    {
        double c = Math.Cos(la1 * Math.PI / 180);
        double x = (lo - lo1) * c, y = la - la1, dx = (lo2 - lo1) * c, dy = la2 - la1;
        double L2 = dx * dx + dy * dy;
        double t = L2 > 0 ? Math.Clamp((x * dx + y * dy) / L2, -0.2, 1.2) : 0;
        return Math.Sqrt((x - t * dx) * (x - t * dx) + (y - t * dy) * (y - t * dy)) * 111320.0;
    }
    public const double MPD = 111132.0;
    public static (double, double) Move(double lat, double lon, double hdg, double m)
    {
        double r = hdg * Math.PI / 180;
        return (lat + m * Math.Cos(r) / MPD, lon + m * Math.Sin(r) / (MPD * Math.Cos(lat * Math.PI / 180)));
    }
    public static double Norm(double d) { d %= 360; if (d > 180) d -= 360; if (d < -180) d += 360; return d; }
}

/// <summary>Runway frame: along from the pavement start, lateral + = right.</summary>
sealed class LandingGeometry
{
    private readonly double _lat0, _lon0, _h, _cl;
    public readonly double LengthM, HalfWidthM;
    public LandingGeometry(Runway r)
    {
        _lat0 = r.StartLat; _lon0 = r.StartLon; _h = r.Heading * Math.PI / 180; _cl = Math.Cos(_lat0 * Math.PI / 180);
        LengthM = r.Length * 0.3048; HalfWidthM = (r.Width > 0 ? r.Width : 150) * 0.3048 / 2;
    }
    public (double, double) At(double along, double lat)
    {
        double n = along * Math.Cos(_h) - lat * Math.Sin(_h), e = along * Math.Sin(_h) + lat * Math.Cos(_h);
        return (_lat0 + n / Geo.MPD, _lon0 + e / (Geo.MPD * _cl));
    }
    public double Along(double lat, double lon)
    { double n = (lat - _lat0) * Geo.MPD, e = (lon - _lon0) * Geo.MPD * _cl; return n * Math.Cos(_h) + e * Math.Sin(_h); }
    public double Lateral(double lat, double lon)
    { double n = (lat - _lat0) * Geo.MPD, e = (lon - _lon0) * Geo.MPD * _cl; return -n * Math.Sin(_h) + e * Math.Cos(_h); }
    public string Describe(double lat, double lon, double exitAlong)
        => $"{Along(lat, lon) - exitAlong:+0;-0} m past junction, {Lateral(lat, lon):+0;-0} m {(Lateral(lat, lon) >= 0 ? "right" : "left")}";
    public static double RoomPast(Runway r, LandingExit e)
    { var g = new LandingGeometry(r); return g.LengthM - g.Along(e.Latitude, e.Longitude); }
    public static bool Feasible(Runway r, LandingExit e, out double exitAlong)
    {
        var g = new LandingGeometry(r);
#if MAINAPP
        exitAlong = g.Along(e.Latitude, e.Longitude);
#else
        exitAlong = g.Along(e.Latitude, e.Longitude) + e.TurnPointOffsetFeet * 0.3048;
#endif
        return exitAlong > 700 && exitAlong < g.LengthM + 100 && e.ExitAngleDegrees <= 90;
    }
}

/// <summary>An airport's graph plus a spatial index of taxi edges for the pavement check.</summary>
sealed class World
{
    public string Icao = "";
    public TaxiGraph Graph = null!;
    public List<Runway> Runways = new();
    public List<ParkingSpot> Parking = new();
    public List<StartPosition> Starts = new();
    private readonly Dictionary<(int, int), List<(double ax, double ay, double bx, double by, double hw)>> _grid = new();
    private double _lat0, _cl;
    private const double Cell = 60.0;

    // Painted RUNWAY hold lines (X-Plane Gateway, via the free AMDB Bridge cache) — an
    // independent audit of where a hold stop should be. VP_AMDBDIR unset = feature off.
    // Positions are X-Plane's, ~20 m off MSFS: only ever used with a 30 m allowance.
    private List<(double la1, double lo1, double la2, double lo2)>? _paintedHolds;
    public List<(double la1, double lo1, double la2, double lo2)> PaintedHolds
    {
        get
        {
            if (_paintedHolds != null) return _paintedHolds;
            _paintedHolds = new();
            string? dir = Environment.GetEnvironmentVariable("VP_AMDBDIR");
            string f = dir == null ? "" : Path.Combine(dir, Icao, "taxiwayholdingposition.geojson");
            if (dir == null || !File.Exists(f)) return _paintedHolds;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(f));
            foreach (var ft in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var props = ft.GetProperty("properties");
                if (!props.TryGetProperty("catstop", out var cs) || cs.ValueKind != System.Text.Json.JsonValueKind.Number || cs.GetInt32() != 1) continue;
                var g = ft.GetProperty("geometry");
                if (g.GetProperty("type").GetString() != "LineString") continue;
                var pts = g.GetProperty("coordinates").EnumerateArray().Select(c => (lat: c[1].GetDouble(), lon: c[0].GetDouble())).ToList();
                for (int i = 1; i < pts.Count; i++) _paintedHolds.Add((pts[i - 1].lat, pts[i - 1].lon, pts[i].lat, pts[i].lon));
            }
            return _paintedHolds;
        }
    }

    /// <summary>Did the step (a → b) cross a painted runway hold line?</summary>
    public bool CrossesPaintedHold(double la, double lo, double lb, double lob)
    {
        foreach (var (p1, q1, p2, q2) in PaintedHolds)
        {
            if (Math.Abs(p1 - la) > 0.002 || Math.Abs(q1 - lo) > 0.003) continue;
            double d1 = Cross(p1, q1, p2, q2, la, lo), d2 = Cross(p1, q1, p2, q2, lb, lob);
            double d3 = Cross(la, lo, lb, lob, p1, q1), d4 = Cross(la, lo, lb, lob, p2, q2);
            if (d1 * d2 < 0 && d3 * d4 < 0) return true;
        }
        return false;
        static double Cross(double ay, double ax, double by, double bx, double cy, double cx) =>
            (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    }

    public static World? Load(IAirportDataProvider p, string icao)
    {
        var paths = p.GetTaxiPaths(icao);
        if (paths.Count == 0) return null;
        var w = new World { Icao = icao, Runways = p.GetRunways(icao), Parking = p.GetParkingSpots(icao), Starts = p.GetRunwayStarts(icao) };
        w.Graph = TaxiGraph.Build(paths, w.Parking, w.Starts, w.Runways);
        w.Index();
        return w;
    }

    public TaxiNode? NodeNear(double lat, double lon, double maxM)
    {
        var n = Graph.FindNearestNode(lat, lon);
        return n != null && TaxiGraph.CalculateDistanceMeters(lat, lon, n.Latitude, n.Longitude) <= maxM ? n : null;
    }

    private (double x, double y) XY(double lat, double lon) => ((lon - 0) * Geo.MPD * _cl, (lat - _lat0) * Geo.MPD);

    private void Index()
    {
        var any = Graph.Nodes.Values.FirstOrDefault();
        _lat0 = any?.Latitude ?? 0; _cl = Math.Cos(_lat0 * Math.PI / 180);
        foreach (var (from, edges) in Graph.Adjacency)
        {
            if (!Graph.Nodes.TryGetValue(from, out var a)) continue;
            foreach (var e in edges)
            {
                if (e.FromNodeId > e.ToNodeId && Graph.Adjacency.ContainsKey(e.ToNodeId)) { }
                if (!Graph.Nodes.TryGetValue(e.ToNodeId, out var b)) continue;
                var (ax, ay) = XY(a.Latitude, a.Longitude); var (bx, by) = XY(b.Latitude, b.Longitude);
                double hw = e.WidthFeet > 0 ? e.WidthFeet * 0.3048 / 2 : 10;
                int x0 = (int)Math.Floor((Math.Min(ax, bx) - hw) / Cell), x1 = (int)Math.Floor((Math.Max(ax, bx) + hw) / Cell);
                int y0 = (int)Math.Floor((Math.Min(ay, by) - hw) / Cell), y1 = (int)Math.Floor((Math.Max(ay, by) + hw) / Cell);
                if ((x1 - x0) * (y1 - y0) > 400) continue;
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                {
                    if (!_grid.TryGetValue((x, y), out var l)) _grid[(x, y)] = l = new();
                    l.Add((ax, ay, bx, by, hw));
                }
            }
        }
    }

    /// <summary>How far beyond the nearest taxi edge's half-width (or 0 on a runway) the point is; 99 when nothing is near.</summary>
    public double OffPavementMetres(double lat, double lon)
    {
        if (TaxiGuidanceHarnessBridge.RunwayUnder(Graph, lat, lon) != null) return 0;
        var (px, py) = XY(lat, lon);
        int cx = (int)Math.Floor(px / Cell), cy = (int)Math.Floor(py / Cell);
        double best = 99;
        for (int x = cx - 1; x <= cx + 1; x++) for (int y = cy - 1; y <= cy + 1; y++)
        {
            if (!_grid.TryGetValue((x, y), out var l)) continue;
            foreach (var (ax, ay, bx, by, hw) in l)
            {
                double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-6 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
                double d = Math.Sqrt(Math.Pow(ax + t * dx - px, 2) + Math.Pow(ay + t * dy - py, 2)) - hw;
                if (d < best) best = d;
            }
        }
        return Math.Max(0, best);
    }
}

static class TaxiGuidanceHarnessBridge
{
    public static TaxiGraph.RunwayCenterline? RunwayUnder(TaxiGraph g, double lat, double lon)
        => RouteRunwayCrossings.RunwayUnder(g.RunwayCenterlines, lat, lon);
}

/// <summary>A taxi to fly: where it starts and how the form would load it.</summary>
sealed class TaxiPlan
{
    public string Describe = "";
    public double StartLat, StartLon, StartHeading;
    public bool StartsOnRunway;
    public string? DestRunway;
    public Runway? Rwy;
    public string Kind = "";
    // Expectations for the flight: the painted hold line the pilot picked, where "Lined up" should come.
    public int? ExpectHoldNodeId; public string? ExpectHoldName;
    public double? ExpectLineupAlongM; public string LineupNote = "";
    // ATC "hold short of runway X" picks (sequence index -> designator), built from the clearance.
    public bool AddHoldPicks, PickDestination, PickReciprocal;
    public Dictionary<int, string>? HoldPicks;
    public Func<TaxiGuidanceManager, IAirportDataProvider, World, List<string>?, string?> LoadFn = null!;
    public string? Load(TaxiGuidanceManager m, IAirportDataProvider p, World w, List<string>? seq) => LoadFn(m, p, w, seq);
#if !OLDAPP
    // The picks as the app's LoadRoute takes them: (row, runway) pairs, each with no place.
    public List<KeyValuePair<int, RunwayHoldPick>>? HoldPickList()
        => HoldPicks?.Select(p => new KeyValuePair<int, RunwayHoldPick>(p.Key, new RunwayHoldPick(p.Value))).ToList();
    // Turn picks (row -> side) as the planner's Turn lists hand them to LoadRoute; a clearance given
    // outright instead of derived from the shortest route; and, for the miss flight, the charted way
    // the pilot takes at DivertAt instead of the picked turn.
    public Dictionary<int, TurnSide>? TurnPicks;
    public List<string>? FixedSeq;
    public List<TaxiNode>? DivertPath; public (double lat, double lon) DivertAt;
    public string PickSuffix() => " via " + string.Join(",", FixedSeq ?? new())
        + (TurnPicks is { Count: > 0 } tp && FixedSeq != null
            ? " turn " + string.Join(",", tp.OrderBy(kv => kv.Key).Select(kv => $"{kv.Value} onto {(kv.Key < FixedSeq.Count ? FixedSeq[kv.Key] : "?")}")) : "")
        + (DivertPath != null ? (DivertIsExcursion ? " [leaves the route before the turn]" : " [takes the charted way]") : "");
    // Cleared crossings as the planner hands them to LoadRoute (clearedCrossings:). With ClearFirstCrossing
    // (--clear-first-crossing) FlyTaxi loads once, reads the first runway the route crosses and reloads with that
    // crossing cleared; XcBaseHoldNodes are the stops the same flight made with nothing cleared, to compare with.
    // DivertIsExcursion: the divert is an excursion off the route BEFORE a picked turn, which a kept-tail recalculation
    // answers, not the charted way taken at the turn (the miss flight).
    public IReadOnlyList<ClearedCrossing>? ClearedCrossings;
    public bool ClearFirstCrossing, DivertIsExcursion;
    public List<int>? XcBaseHoldNodes;
    // --crossing-keys: which key the flight presses at its first crossing (KeyFlight.Early / Hold / Past), null for none.
    public string? KeyVariant;
#endif

    /// <summary>A departure from a named (OSM) holding point ("HP") or a runway intersection ("IX"), as the form loads it.</summary>
    public static TaxiPlan? Departure(World w, ParkingSpot gate, Runway rwy, string kind, TaxiGraph.RunwayIntersection e, bool partway, string osmKind)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude);
        // Form semantics: an intersection always lines up AT the intersection; a holding point
        // only when Partway, else at the full-length lineup point.
        bool atEntry = kind == "IX" || partway;
        double thLat = atEntry ? e.Latitude : lineupLat, thLon = atEntry ? e.Longitude : lineupLon;
        var g = new LandingGeometry(rwy);
        var tp = new TaxiPlan
        {
            Kind = kind,
            Describe = $"{kind} {e.TaxiwayName}{(kind == "HP" ? (partway ? " partway" : " full") + (osmKind.Length > 0 ? " " + osmKind : "") : "")} {gate} -> Runway {rwy.RunwayID}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
            ExpectHoldNodeId = kind == "HP" && e.HoldNodeId != 0 ? e.HoldNodeId : null,
            ExpectHoldName = kind == "HP" ? e.TaxiwayName : null,
            // What the pilot was told: an intersection's listed distance from the threshold.
            ExpectLineupAlongM = kind == "IX" ? e.AlongMetersFromThreshold : g.Along(thLat, thLon), LineupNote = atEntry ? $"at {e.TaxiwayName}" : "full length",
        };
        int dest = e.NodeId; int? holdNode = kind == "HP" ? e.HoldNodeId : null; string? holdName = kind == "HP" ? e.TaxiwayName : null;
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
#if !OLDAPP
            // The app takes the stops as (row, runway pick) pairs; a frozen build still takes the dictionary.
            seq, userRunwayHoldShorts: tp.HoldPickList(), destinationHeading: rwy.HeadingMag, destinationThresholdLat: thLat, destinationThresholdLon: thLon,
#else
            seq, userRunwayHoldShorts: tp.HoldPicks, destinationHeading: rwy.HeadingMag, destinationThresholdLat: thLat, destinationThresholdLon: thLon,
#endif
            destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true,
#if MAINAPP
            holdingPointHoldNodeId: holdNode);
#else
            holdingPointHoldNodeId: holdNode, holdingPointName: holdName, intersectionDeparture: kind == "IX");
#endif
        return tp;
    }

    public static TaxiPlan? Backtrack(World w, ParkingSpot gate, Runway rwy)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var entry = w.Graph.FindBacktrackEntryNode(rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, hw, gate.Latitude, gate.Longitude);
        if (entry == null) return null;
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude);
        var tp = new TaxiPlan
        {
            Kind = "BT", Describe = $"BT {gate} -> Runway {rwy.RunwayID} via n{entry.NodeId}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
            ExpectLineupAlongM = new LandingGeometry(rwy).Along(lineupLat, lineupLon), LineupNote = "full length after backtrack",
        };
        int dest = entry.NodeId;
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
            seq, destinationHeading: rwy.HeadingMag, destinationThresholdLat: lineupLat, destinationThresholdLon: lineupLon,
            destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true,
            fullLengthBacktrack: true);
        return tp;
    }

    public static TaxiPlan? GateToGate(World w, ParkingSpot a, ParkingSpot b)
    {
        if (a == b) return null;
        var bn = w.NodeNear(b.Latitude, b.Longitude, 100);
        var first = w.Graph.FindNearestNode(a.Latitude, a.Longitude);
        if (bn == null || first == null) return null;
        if (TaxiGraph.CalculateDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) < 600) return null;
        double hdg = Bearing(a.Latitude, a.Longitude, first.Latitude, first.Longitude);
        int dest = bn.NodeId;
        return new TaxiPlan
        {
            Kind = "G2G", Describe = $"G2G {a} -> {b}", StartLat = a.Latitude, StartLon = a.Longitude, StartHeading = hdg,
            LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, a.Latitude, a.Longitude, hdg, dest, b.ToString(), seq,
                prebuiltGraph: wo.Graph, announceSummary: true),
        };
    }

    /// <param name="heading">The aircraft's own heading; without one it faces its nearest taxi node.</param>
    public static TaxiPlan? GateToRunway(World w, ParkingSpot gate, Runway rwy, double? heading = null)
    {
        var (lineupLat, lineupLon) = DepartureRig.Lineup(w, rwy);   // TaxiAssistForm.PopulateDestinations
        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var entry = w.Graph.FindRunwayLineupEntryNode(lineupLat, lineupLon, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            halfWidthM, TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M, gate.Latitude, gate.Longitude);
        if (entry == null) return null;
        var first = w.Graph.FindNearestNode(gate.Latitude, gate.Longitude);
        double hdg = heading ?? (first == null ? 0 : Bearing(gate.Latitude, gate.Longitude, first.Latitude, first.Longitude));
        int dest = entry.NodeId;
        var tp = new TaxiPlan
        {
            Describe = $"{gate} -> Runway {rwy.RunwayID}",
            StartLat = gate.Latitude, StartLon = gate.Longitude, StartHeading = hdg, DestRunway = rwy.RunwayID, Rwy = rwy,
        };
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, gate.Latitude, gate.Longitude, hdg, dest, $"Runway {rwy.RunwayID}",
#if !OLDAPP
                seq, userRunwayHoldShorts: tp.HoldPickList(), destinationHeading: rwy.HeadingMag, destinationThresholdLat: lineupLat, destinationThresholdLon: lineupLon,
#else
                seq, userRunwayHoldShorts: tp.HoldPicks, destinationHeading: rwy.HeadingMag, destinationThresholdLat: lineupLat, destinationThresholdLon: lineupLon,
#endif
#if OLDAPP
                destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true);
#else
                destinationHeadingTrue: rwy.Heading, isRunwayDestination: true, prebuiltGraph: wo.Graph, announceSummary: true,
                userTurnPicks: tp.TurnPicks, clearedCrossings: tp.ClearedCrossings);
#endif
        return tp;
    }

    /// <summary>Stopped on the centreline midway across the widest gap between two exits, facing the rollout direction.</summary>
    public static TaxiPlan? RunwayStopToGate(World w, Runway rwy, List<LandingExit> exits, ParkingSpot gate)
    {
        var ordered = exits.OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        LandingExit? from = null, to = null;
        double widest = 300.0 / 0.3048;
        for (int i = 0; i + 1 < ordered.Count; i++)
        {
            double gap = ordered[i + 1].DistanceFromThresholdFeet - ordered[i].DistanceFromThresholdFeet;
            if (gap >= widest) { widest = gap; from = ordered[i]; to = ordered[i + 1]; }
        }
        var gateNode = w.NodeNear(gate.Latitude, gate.Longitude, 100);
        if (from == null || to == null || gateNode == null) return null;
        double lat = (from.Latitude + to.Latitude) / 2.0, lon = (from.Longitude + to.Longitude) / 2.0;
        double hdg = Bearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        int dest = gateNode.NodeId;
        return new TaxiPlan
        {
            Describe = $"Rwy {rwy.RunwayID} stop {from.TaxiwayName}..{to.TaxiwayName} -> {gate}",
            StartLat = lat, StartLon = lon, StartHeading = hdg,
            LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, lat, lon, hdg, dest, gate.ToString(),
                seq, prebuiltGraph: wo.Graph, announceSummary: true),
        };
    }

    /// <summary>A route loaded at a given position and heading, with the clearance given (or none).</summary>
    public static TaxiPlan FromPosition(World w, double lat, double lon, double hdg, ParkingSpot gate, List<string>? clearance)
    {
        int dest = w.NodeNear(gate.Latitude, gate.Longitude, 100)!.NodeId;
        return new TaxiPlan
        {
            Describe = $"From {lat:F6},{lon:F6} hdg {hdg:F0} -> {gate}" + (clearance == null ? "" : " via " + string.Join(",", clearance)),
            StartLat = lat, StartLon = lon, StartHeading = hdg,
            LoadFn = (m, p, wo, _) => m.LoadRoute(p, wo.Icao, lat, lon, hdg, dest, gate.ToString(),
                clearance == null ? null : new List<string>(clearance), prebuiltGraph: wo.Graph, announceSummary: true),
        };
    }

    public static TaxiPlan? ExitToGate(World w, Runway rwy, List<LandingExit> exits, LandingExit exit, ParkingSpot gate)
    {
        int startNode = LandingExitDestination.Resolve(w.Graph, exit, exits, rwy, rwy.Heading, out _, out _, out _);
        if (startNode <= 0 || !w.Graph.Nodes.TryGetValue(startNode, out var sn)) return null;
        var gateNode = w.NodeNear(gate.Latitude, gate.Longitude, 100);
        if (gateNode == null) return null;
        // Face away from the runway: the heading from the exit junction to where the vacate ends.
        double hdg = Bearing(exit.Latitude, exit.Longitude, sn.Latitude, sn.Longitude);
        int dest = gateNode.NodeId;
        var tp = new TaxiPlan
        {
            Describe = $"Rwy {rwy.RunwayID} exit {exit.TaxiwayName} -> {gate}",
            StartLat = sn.Latitude, StartLon = sn.Longitude, StartHeading = hdg,
        };
        tp.LoadFn = (m, p, wo, seq) => m.LoadRoute(p, wo.Icao, sn.Latitude, sn.Longitude, hdg, dest, gate.ToString(),
#if OLDAPP
                seq, prebuiltGraph: wo.Graph, announceSummary: true);
#else
                seq, prebuiltGraph: wo.Graph, announceSummary: true, userTurnPicks: tp.TurnPicks, clearedCrossings: tp.ClearedCrossings);
#endif
        return tp;
    }

    public static double Bearing(double la1, double lo1, double la2, double lo2)
        => (Math.Atan2((lo2 - lo1) * Math.Cos(la1 * Math.PI / 180), la2 - la1) * 180 / Math.PI + 360) % 360;
}


sealed class HoldStop
{
    public double Odo, Lat, Lon, T; public string Text; public bool Used;
    public HoldStop(double odo, double lat, double lon, string text, double t) { Odo = odo; Lat = lat; Lon = lon; Text = text; T = t; }
}

/// <summary>What the Taxi form computes for a departure, mirrored for the harness.</summary>
static class DepartureRig
{
    public static (double lat, double lon) Lineup(World w, Runway rwy)
    {
        var rows = w.Starts.Where(s => string.Equals(s.RunwayName?.Trim(), rwy.RunwayID, StringComparison.OrdinalIgnoreCase)).ToList();
#if OLDAPP
        var st = rows.Count > 0 ? TaxiGraph.PickFullLengthStart(rows, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon) : null;
        return st != null ? TaxiGraph.SnapStartToRunwayCenterline(st.Latitude, st.Longitude, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon)
                          : (rwy.StartLat, rwy.StartLon);
#else
        return w.Graph.DepartureLineupPoint(rows, rwy);   // TaxiAssistForm.PopulateDestinations
#endif
    }

    /// <summary>TaxiAssistForm.ResolveHoldingPointsForRunway, with the gate standing in for the aircraft.</summary>
    public static List<(TaxiGraph.RunwayIntersection e, string kind, bool partway)> ResolveHoldingPoints(
        World w, Runway rwy, List<(string Name, double Lat, double Lon, string Kind)> named, ParkingSpot at)
    {
        var res = new List<(TaxiGraph.RunwayIntersection, string, bool)>();
        if (named.Count == 0) return res;
        var kindByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, _, kind) in named)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string key = name.Trim();
            if (!kindByName.TryGetValue(key, out var ex) || string.IsNullOrEmpty(ex)) kindByName[key] = kind?.Trim() ?? "";
        }
        var pts = named.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Select(p => (Name: p.Name.Trim(), p.Lat, p.Lon)).ToList();
        double hw = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
        var (lLat, lLon) = Lineup(w, rwy);
        double fLat = rwy.EndLat, fLon = rwy.EndLon;
        string? recip = TaxiGraph.ReciprocalRunwayName(rwy.RunwayID);
        var far = recip == null ? null : w.Runways.FirstOrDefault(r => string.Equals(r.RunwayID, recip, StringComparison.OrdinalIgnoreCase));
        if (far != null) (fLat, fLon) = Lineup(w, far);
        var (thrLat, thrLon, farLat, farLon, lineupAlong) = TaxiGraph.ChooseHoldingPointExtent(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, lLat, lLon, fLat, fLon);
        foreach (var e in w.Graph.ResolveHoldingPointEntries(pts, thrLat, thrLon, farLat, farLon, hw, at.Latitude, at.Longitude,
                     behindThresholdEligible: name =>
                         !string.Equals(name.Replace(" ", "").Replace("-", ""), "NOENTRY", StringComparison.OrdinalIgnoreCase) &&
                         kindByName.TryGetValue(name, out var k) &&
                         (string.Equals(k, "runway", StringComparison.OrdinalIgnoreCase) || string.Equals(k, "ils", StringComparison.OrdinalIgnoreCase)),
                     runwayName: rwy.RunwayID))
        {
            kindByName.TryGetValue(e.TaxiwayName, out var k);
            res.Add((e, k ?? "", e.AlongMetersFromThreshold > lineupAlong + 50.0));
        }
        return res;
    }

    static readonly System.Text.RegularExpressions.Regex RwyRx =
        new(@"[Rr]unways?\s+0*(\d{1,2})\s?([LCR])?\b", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static bool MentionsRunway(string text) => RwyRx.IsMatch(text);
    static string Norm(string d) => d.Trim().ToUpperInvariant().TrimStart('0');
    /// <summary>Does the spoken text name this runway (either end)?</summary>
    public static bool NamesRunway(string text, TaxiGraph.RunwayCenterline r)
    {
        foreach (System.Text.RegularExpressions.Match m in RwyRx.Matches(text))
        {
            string d = Norm(m.Groups[1].Value + m.Groups[2].Value);
            if (d == Norm(r.Name1) || d == Norm(r.Name2)) return true;
        }
        return false;
    }
}


/// <summary>
/// Voices a clearance the way ATC gives it and runs it through the SayIntentions import's own
/// parser (MainForm.ParseClearanceTaxiPlan + ParseDestinationRunway), checking it recovers the
/// taxiways, every hold short and the runway. Two phrasings: written designators ("via A, B4"),
/// and spoken NATO ("via Alpha, Bravo Four") for the names a controller spells.
/// </summary>
static class SpokenClearance
{
    static readonly string[] Letters = { "Alpha","Bravo","Charlie","Delta","Echo","Foxtrot","Golf","Hotel","India","Juliett","Kilo","Lima","Mike",
        "November","Oscar","Papa","Quebec","Romeo","Sierra","Tango","Uniform","Victor","Whiskey","X-ray","Yankee","Zulu" };
    static readonly string[] Digits = { "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Niner" };
    static readonly System.Text.RegularExpressions.Regex Spellable = new(@"^[A-Z]{1,2}[0-9]{0,3}[A-Z]?$");

    static string Speak(string tw)
    {
        string t = tw.Trim().ToUpperInvariant();
        if (!Spellable.IsMatch(t)) return tw;
        return string.Join(" ", t.Select(c => char.IsDigit(c) ? Digits[c - '0'] : Letters[c - 'A']));
    }

    public static List<Finding> Check(World w, string where, List<string> seq, Dictionary<int, string> picks, string destRunway)
    {
        var found = new List<Finding>();
        var known = w.Graph.GetAllTaxiwayNames();
        foreach (bool spoken in new[] { false, true })
        {
            var sb = new System.Text.StringBuilder($"Speedbird one two three, runway {destRunway}, taxi via ");
            for (int k = 0; k < seq.Count; k++)
            {
                if (k > 0) sb.Append(", ");
                sb.Append(spoken ? Speak(seq[k]) : seq[k].ToUpperInvariant());   // SayIntentions writes designators upper case
                if (picks.TryGetValue(k, out var r)) sb.Append($", hold short of runway {r}");
            }
            sb.Append('.');
            string text = sb.ToString();
            string tag = spoken ? "SPOKEN" : "WRITTEN";
            if (Environment.GetEnvironmentVariable("VP_SHOWCLR") == "1") Console.WriteLine("CLR " + text);
            var (tws, holds, unknown) = MSFSBlindAssist.MainForm.ParseClearanceTaxiPlan(text, known);
            var want = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.CollapseConsecutive(seq);
            var got = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.CollapseConsecutive(tws);
            if (!want.SequenceEqual(got, StringComparer.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_TAXIWAYS_WRONG", where, $"\"{text}\" parsed as {string.Join(",", tws)}{(unknown.Count > 0 ? " unknown " + string.Join(",", unknown) : "")}"));
            var wantHolds = picks.OrderBy(kv => kv.Key).Select(kv => $"{seq[kv.Key]}>{RouteRunwayCrossings.NormalizeDesignator(kv.Value)}").ToList();
            var gotHolds = holds.Select(h => $"{h.AfterTaxiway}>{RouteRunwayCrossings.NormalizeDesignator(h.Runway)}").ToList();
            if (!wantHolds.SequenceEqual(gotHolds, StringComparer.OrdinalIgnoreCase))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_HOLDS_WRONG", where, $"\"{text}\" hold shorts {string.Join(",", gotHolds)} want {string.Join(",", wantHolds)}"));
            string? dest = MSFSBlindAssist.Services.SayIntentions.SayIntentionsClearanceParser.ParseDestinationRunway(text);
            if (dest == null || RouteRunwayCrossings.NormalizeDesignator(dest) != RouteRunwayCrossings.NormalizeDesignator(destRunway))
                found.Add(new Finding(w.Icao, $"CLR_{tag}_RUNWAY_WRONG", where, $"\"{text}\" destination {dest ?? "none"}"));
        }
        return found;
    }
}

#if !OLDAPP
/// <summary>A `legends` leg: where it starts and what the form is given ("via A" or "via Z, A" with a Turn on A).</summary>
sealed class LegPlan
{
    public string Icao = "", Kind = "", A = "", B = "";
    public string? Z;
    public int M = -1, StartNode, Dest;
    public double Lat, Lon, Hdg, AlongM;
    public List<string> Seq = new();
    public TurnSide? Pick;
    // crossthenhold: the runway the way along A crosses before the B junction, cleared at load (clearedCrossings),
    // how far before the junction its far pavement edge is, and whether the route stopped for it with nothing cleared.
    public string? ClearRunway;
    public TaxiGraph.RunwayCenterline? CrossRunway;
    public double EdgeToJunctionM = double.NaN;
    public bool RunwayHeldAtLoad;
    public string Describe() => Kind switch
    {
        "turn" => $"via {Z},{A} turn {Pick} onto {A} hold short of {B} from n{StartNode} ({AlongM:F0} m along {Z} before n{M})",
        "cross" => $"via {A} cross runway {ClearRunway} hold short of {B} from n{StartNode} ({AlongM:F0} m along {A} before the junction, the runway's far edge {EdgeToJunctionM:F0} m before it)",
        _ => $"via {A} hold short of {B} from n{StartNode} ({AlongM:F0} m along {A} before the junction)",
    };
}

/// <summary>One `legends` leg as flown: a CSV row. Metres are along the route to the B junction ahead of the stop.</summary>
sealed class LegendRow
{
    public string Icao = "", A = "", B = "", Pick = "none", Outcome = "", Kind = "", Z = "", Note = "";
    public bool Ended;
    public double Seconds = double.NaN, EndM = double.NaN, StandM = double.NaN, StandStraightM = double.NaN,
                  StartM = double.NaN, SetbackM = double.NaN, RouteM = double.NaN, LastSegmentM = double.NaN,
                  EndHeadingOffDeg = double.NaN;
    /// <summary>The junction measured to (the app's), the harness's own pick of it (a cross-check), and the form's.</summary>
    public int Countdown, Recalcs, Junction = -1, HarnessJunction = -1, FormDest = -1;
    /// <summary>The countdown lines heard, in order: O "in N", S "… Slow down.", X "… Stop.".</summary>
    public string Tiers = "";
    /// <summary>The taxiway of the route's last segment as loaded (not A when the stop moved back before the turn onto A).</summary>
    public string LastTaxiway = "";

    public const string Header = "icao,a,b,pick,outcome,ended,seconds_to_end,end_m_before_junction,standstill_m_before_junction,"
        + "kind,z,standstill_straight_m_to_junction,start_m_to_junction,stop_m_before_junction_at_load,route_m,last_segment_m,"
        + "last_taxiway,end_heading_off_last_segment_deg,countdown_lines,countdown_tiers,route_changes,junction_node,"
        + "harness_junction_node,form_dest_node,note";

    public string Csv() => string.Join(",", new[]
    {
        Icao, A, B, Pick, Outcome, Ended ? "1" : "0", F(Seconds), F(EndM), F(StandM),
        Kind, Z, F(StandStraightM), F(StartM), F(SetbackM), F(RouteM), F(LastSegmentM), LastTaxiway, F(EndHeadingOffDeg),
        I(Countdown), Tiers, I(Recalcs), I(Junction), I(HarnessJunction), I(FormDest), Note,
    }.Select(Quote));

    // crossthenhold: the runway crossed and cleared, and what the pilot heard and did there; where the stop for B lies
    // against that runway (metres along the route past its pavement's far edge; before it, NaN and StopBeforeRunway).
    public string Runway = "", XcOutcome = "";
    public double EdgeToJunctionM = double.NaN, StopPastEdgeM = double.NaN;
    public int HeldAtRunway, CrossingLines, CrossingEarly, ClearedApproach, EndedOnRunway;
    public bool RunwayHeldAtLoad, StopBeforeRunway, StopClearOfMargin, Approach25Clear;

    public const string CrossHeader = "icao,a,b,runway,runway_edge_m_before_junction,runway_held_with_nothing_cleared,cleared,"
        + "held_at_runway,crossing_lines,crossing_said_before_its_segment,cleared_to_cross_calls,ended,seconds_to_end,"
        + "end_m_before_junction,standstill_m_before_junction,stop_m_before_junction_at_load,stop_m_past_runway_edge,"
        + "stop_before_runway,stop_clear_of_every_margin,stop_approach_25m_clear,leg_ended_on_runway,countdown_tiers,"
        + "route_changes,start_m_to_junction,junction_node,note";

    public string CsvCross() => string.Join(",", new[]
    {
        Icao, A, B, Runway, F(EdgeToJunctionM), B01(RunwayHeldAtLoad), XcOutcome,
        I(HeldAtRunway), I(CrossingLines), I(CrossingEarly), I(ClearedApproach), B01(Ended), F(Seconds),
        F(EndM), F(StandM), F(SetbackM), F(StopPastEdgeM),
        B01(StopBeforeRunway), B01(StopClearOfMargin), B01(Approach25Clear), I(EndedOnRunway), Tiers,
        I(Recalcs), F(StartM), I(Junction), Note,
    }.Select(Quote));

    private static string B01(bool b) => b ? "1" : "0";
    private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F1", CultureInfo.InvariantCulture);
    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Quote(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

#if !OLDAPP
/// <summary>
/// One flight with a runway crossing cleared (LoadRoute's clearedCrossings): where the crossing was cleared, as
/// the route was loaded, and what the pilot heard and did there. The stop is the cleared event's
/// (<see cref="TaxiRouteRunwayEvent.StopNodeIndex"/>): CLEARED, no longer a hold short; START, the route's start
/// hold; SHARED, a stop that still holds for another runway ([HLD-11]); NO_STOP, a crossing with no stop to clear;
/// NOT_APPLIED, nothing on the route was cleared.
/// </summary>
sealed class XcFlight
{
    public string Runway = "", Outcome = "", Label = "", LineText = "";
    public TaxiNode? StopNode;
    public int StopNodeIndex = -1;
    /// <summary>The stop segment's length (NaN for a start hold): shorter than the 25 m capture radius, it can be passed with no countdown.</summary>
    public double StopSegmentM = double.NaN;
    public bool Flown, Reached, StopAgainSaid, LineAtGuidanceStart;
    /// <summary>With a Turn pick that changed the route: whether the stop lies at or after its departure (in the kept tail). Null without one.</summary>
    public bool? StopAfterPick;
    /// <summary>
    /// Lines naming the runway: "Crossing runway …" said once the segment cursor is past the stop's segment, within
    /// 100 m of it (the line); said while the cursor is still before it (Early: the incursion watch's own line for
    /// an on-route hold node within 40 m that is not yet the countdown's); further away (Elsewhere); the countdown's
    /// "… cleared to cross." (Approach, and before the line); HoldShort entered at the stop.
    /// </summary>
    public int Line, Early, CrossingElsewhere, Approach, ApproachBeforeLine, HeldAt;
    public string EarlyText = "";
    public double LineAtM = double.NaN;
    /// <summary>
    /// Each route change before the aircraft reached the stop: KEPT (cleared again at the same node), BACK_AT_SAME_STOP,
    /// CLEARED_ELSEWHERE (another crossing of the runway cleared), STOP_ELSEWHERE (a crossing of it held at another
    /// node), ENTRY_STOP_ELSEWHERE (the route now enters it, held), CROSSED_WITHOUT_STOP, NOT_CROSSED; "+tail" when
    /// the new route kept a Turn pick's tail.
    /// </summary>
    public readonly List<string> Recalcs = new();
    private object? _routeSeen;

    /// <summary>
    /// A route change before the stop left this crossing no longer cleared at its node (anything but KEPT): from then on
    /// no line is due there, and what is heard about the runway belongs to the route's new stop for it.
    /// </summary>
    public bool Moved => Recalcs.Count > 0 && !Recalcs[^1].StartsWith("KEPT", StringComparison.Ordinal);

    // "Crossing runway 04L at C." and a scenery hold name's "Crossing H1, Runway 34C."; one naming no runway
    // ("Crossing taxiway E9.") is left out by NamesRunway.
    private static readonly System.Text.RegularExpressions.Regex CrossingLine = new(@"\bCrossing ([^.]+)\.");

    /// <summary>The crossing as loaded: the cleared event's stop node, the label its line is said with, and what kind of stop it was.</summary>
    public static XcFlight Setup(TaxiRoute route, string runway)
    {
        var xc = new XcFlight { Runway = runway, Flown = true, _routeSeen = route };
        var ev = route.RunwayEvents.FirstOrDefault(e => e.Kind == RunwayEventKind.Crossing && e.Cleared);
        if (ev == null) { xc.Outcome = "NOT_APPLIED"; return xc; }
        int si = ev.StopNodeIndex;
        xc.StopNodeIndex = si;
        if (si < 0 || si > route.Segments.Count) { xc.Outcome = "NO_STOP"; return xc; }
        if (si == 0)
        {
            xc.StopNode = route.Segments[0].FromNode;
            xc.Label = ClearedCrossingHolds.LineLabel(route.PlacedStartHoldLabel, route.StartHoldClearedRunways);
            xc.Outcome = route.StartHoldRunway != null ? "SHARED" : "START";
        }
        else
        {
            var seg = route.Segments[si - 1];
            xc.StopNode = seg.ToNode;
            xc.StopSegmentM = seg.DistanceMeters;
            // A Turn pick that changed the route and departs at or before the stop: the stop is in the tail a
            // recalculation before the pick keeps (TurnPickCarryOver.TailFrom); otherwise it is rebuilt.
            if (route.TurnPicks.Any(p => p.ChangedRoute))
                xc.StopAfterPick = route.TurnPicks.Any(p => p.ChangedRoute && si - 1 >= p.FirstSegmentAfterDeparture);
            xc.Label = ClearedCrossingHolds.LineLabel(seg.PlacedHoldLabel, seg.ClearedRunways);
            xc.Outcome = seg.IsHoldShortPoint ? "SHARED" : "CLEARED";
        }
        return xc;
    }

    /// <summary>
    /// A crossing held by its own stop on segment <c>stopNodeIndex - 1</c>, nothing cleared at load: the --crossing-keys
    /// Continue flight, which clears it early, counts its lines with it (<see cref="Hear"/>, <see cref="Track"/>).
    /// </summary>
    public static XcFlight ForHeldStop(TaxiRoute route, string runway, int stopNodeIndex)
    {
        var seg = route.Segments[stopNodeIndex - 1];
        return new XcFlight
        {
            Runway = runway, Flown = true, _routeSeen = route, Outcome = "HELD", StopNodeIndex = stopNodeIndex,
            StopNode = seg.ToNode, StopSegmentM = seg.DistanceMeters, Label = seg.HoldShortRunway ?? "",
        };
    }

    /// <summary>Whether <paramref name="text"/> names <paramref name="runway"/>, either end.</summary>
    public static bool NamesRunway(string text, string runway)
    {
        string r = RouteRunwayCrossings.NormalizeDesignator(runway), rr = RouteRunwayCrossings.Reciprocal(runway);
        return RouteRunwayCrossings.ExtractRunwayDesignators(text)
            .Any(d => d.Equals(r, StringComparison.OrdinalIgnoreCase) || d.Equals(rr, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An utterance heard with the aircraft at (lat, lon), guidance on <paramref name="segmentIndex"/> of
    /// <paramref name="route"/>. True for the first line said at the stop.
    /// </summary>
    public bool Hear(string a, double lat, double lon, TaxiRoute? route, int segmentIndex)
    {
        if (StopNode == null) return false;
        if (a.Contains("is a stop again", StringComparison.OrdinalIgnoreCase) && NamesRunway(a, Runway)) StopAgainSaid = true;
        if (Moved) return false;
        double d = Geo.Dist(lat, lon, StopNode.Latitude, StopNode.Longitude);
        bool first = false;
        if (a.Contains("cleared to cross", StringComparison.OrdinalIgnoreCase) && NamesRunway(a, Runway))
        {
            Approach++;
            if (Line == 0) ApproachBeforeLine++;
        }
        // The stop's segment on the route flown now (a route change renumbers it); none for a start hold, whose line
        // is told from the watch's by its words: the watch names the hold node ("runway 16L at A10A"), the line the
        // label the start hold was placed with ("runway 16L").
        int stopSeg = route?.Segments.FindIndex(s => s.ToNode.NodeId == StopNode.NodeId) ?? -1;
        foreach (System.Text.RegularExpressions.Match m in CrossingLine.Matches(a))
        {
            if (!NamesRunway(m.Groups[1].Value, Runway)) continue;
            if (d > 100) { CrossingElsewhere++; continue; }
            if (Line == 0 && (stopSeg >= 0 ? segmentIndex <= stopSeg
                                           : Outcome == "START" && !m.Groups[1].Value.Equals(Label, StringComparison.OrdinalIgnoreCase)))
            {
                if (++Early == 1) EarlyText = a.Trim();
                continue;
            }
            if (++Line == 1) { first = true; LineAtM = d; LineText = a.Trim(); }
        }
        return first;
    }

    /// <summary>Every frame: whether the aircraft has come to the stop, and what each route change before it did with the crossing.</summary>
    public void Track(TaxiRoute? route, double lat, double lon)
    {
        if (StopNode == null) return;
        if (Geo.Dist(lat, lon, StopNode.Latitude, StopNode.Longitude) < 30) Reached = true;
        if (route == null || ReferenceEquals(route, _routeSeen)) return;
        _routeSeen = route;
        if (Line > 0 || Reached) return;
        int k = route.Segments.FindIndex(s => s.ToNode.NodeId == StopNode.NodeId);
        bool Named(TaxiRouteRunwayEvent e) => NamesRunway("runway " + e.Designator, Runway);
        bool Mine(TaxiRouteRunwayEvent e) => e.Kind == RunwayEventKind.Crossing && Named(e);
        string what = k >= 0 && route.Segments[k].ClearedRunways.Count > 0 ? "KEPT"
            : k >= 0 && route.Segments[k].IsHoldShortPoint ? "BACK_AT_SAME_STOP"
            : route.RunwayEvents.Any(e => Mine(e) && e.Cleared) ? "CLEARED_ELSEWHERE"
            : route.RunwayEvents.Any(e => Mine(e) && e.Held && !e.Passed) ? "STOP_ELSEWHERE"
            : route.RunwayEvents.Any(e => e.Kind != RunwayEventKind.Crossing && Named(e) && e.Held && !e.Passed) ? "ENTRY_STOP_ELSEWHERE"
            : route.RunwayEvents.Any(e => Mine(e) && !e.Passed) ? "CROSSED_WITHOUT_STOP"
            : "NOT_CROSSED";
        Recalcs.Add(what + (route.TurnPicks.Count > 0 ? "+tail" : ""));
    }

    /// <summary>Against the same flight with nothing cleared: its stops at this node (-1 not known), and whether every other stop is the same.</summary>
    public int BaseHeldAt = -1;
    public bool? OtherStopsSame;

    /// <summary>The XC| results line's fields after the kind, airport and key.</summary>
    public string ResultFields()
        => string.Join("|", Outcome, Runway, StopNode == null ? "-" : "n" + StopNode.NodeId, $"held={HeldAt}", $"line={Line}",
            $"approach={ApproachBeforeLine}", $"early={Early}", $"elsewhere={CrossingElsewhere}", $"reached={(Reached ? 1 : 0)}",
            $"line_at_start={(LineAtGuidanceStart ? 1 : 0)}", $"stop_after_pick={(StopAfterPick is bool sp ? (sp ? "1" : "0") : "-")}", $"stop_seg_m={(double.IsNaN(StopSegmentM) ? "-" : StopSegmentM.ToString("F0", CultureInfo.InvariantCulture))}",
            $"base_held={BaseHeldAt}", $"other_stops={(OtherStopsSame is bool b ? (b ? "same" : "differ") : "-")}",
            $"recalcs={string.Join(",", Recalcs)}", $"stop_again_said={(StopAgainSaid ? 1 : 0)}", Label, LineText);
}

/// <summary>
/// One --crossing-keys flight: the key it presses at its first crossing (<see cref="Variant"/>), what triggers the
/// press, where the aircraft was, what the key answered, and what followed at the stop. EARLY: nothing cleared,
/// Continue (Input Y, ContinuePastHoldShort) right after the first countdown call for the stop, heard while guidance is
/// on the stop's segment. HOLD: the crossing cleared at load, the Hold short key (Input Alt+Y,
/// HoldShortOfNextClearedRunway) right after the first "… cleared to cross." naming the runway. PAST: cleared, the
/// Hold short key at the first frame more than RouteRunwayCrossings.StopPassedToleranceMetres (10 m) beyond the stop
/// node along the route: just past the line as the key itself judges it (within that, off the runway's pavement, the
/// aircraft is still at the line and the key puts the stop back, by design).
/// </summary>
sealed class KeyFlight
{
    public const string Early = "EARLY", Hold = "HOLD", Past = "PAST";

    public string Variant = "", Runway = "", Label = "", Answer = "", Skip = "";
    public int StopNodeId = -1;
    public bool Flown, Pressed, Restored;
    public double PressT = double.NaN, ToStopM = double.NaN, PastM = double.NaN;
    public bool PressOnRunway;
    /// <summary>The runway and moment of the first frame held at the stop on runway pavement; null when never.</summary>
    public string? HeldOnRunway;
    public int HeldAt;
    /// <summary>The stop's lines: the load's cleared crossing (HOLD, PAST), or the held stop's own (EARLY, <see cref="XcFlight.ForHeldStop"/>).</summary>
    public XcFlight? Xc;

    // "Hold short runway 22R in 120 metres." / "… in 400 feet. Slow down.": the countdown's own words for a stop.
    private static readonly System.Text.RegularExpressions.Regex CountdownCall = new(@"\bHold short (.+?) in \d");

    /// <summary>How the flight's description names the key it presses.</summary>
    public static string Describe(string variant) => variant switch
    {
        Early => "Continue after the first countdown call",
        Hold => "Hold short key after the first cleared-to-cross call",
        _ => "Hold short key just past the line",
    };

    /// <summary>The stop of a crossing cleared at load (HOLD, PAST).</summary>
    public void SetStop(XcFlight xc)
    {
        Runway = xc.Runway; StopNodeId = xc.StopNode?.NodeId ?? -1; Label = xc.Label; Xc = xc;
    }

    /// <summary>
    /// EARLY's stop: the route's first crossing not already passed, in route order, held by a stop of its own on a
    /// segment, for that runway alone, that is not the pilot's own hold and not on the route's last segment. Null when
    /// set; else why the flight is not flown.
    /// </summary>
    public static string? SetEarlyStop(KeyFlight k, TaxiRoute route)
    {
        var ev = route.RunwayEvents.Where(e => e.Kind == RunwayEventKind.Crossing && !e.Passed)
                      .OrderBy(e => e.ReachNodeIndex < 0 ? int.MaxValue : e.ReachNodeIndex).FirstOrDefault();
        if (ev == null) return "no runway crossed";
        if (!ev.Held || ev.StopNodeIndex <= 0 || ev.StopNodeIndex > route.Segments.Count) return "first crossing has no stop on a segment";
        int seg = ev.StopNodeIndex - 1;
        var s = route.Segments[seg];
        if (seg == route.Segments.Count - 1) return "first crossing's stop on the last segment";
        if (!s.IsHoldShortPoint) return "first crossing's stop is no hold short";
        if (RouteRunwayCrossings.ExtractRunwayDesignators(s.HoldShortRunway).Count != 1) return "first crossing's stop is shared";
        if (!ClearedCrossingHolds.CanClearLabel(s.HoldShortRunway, ev.Designator)) return "first crossing's stop is the pilot's own";
        k.Runway = ev.Designator; k.StopNodeId = s.ToNode.NodeId; k.Label = s.HoldShortRunway ?? "";
        k.Xc = XcFlight.ForHeldStop(route, ev.Designator, ev.StopNodeIndex);
        return null;
    }

    /// <summary>Whether this frame triggers the press: what was just said, guidance's segment, and where the aircraft is.</summary>
    public bool Triggered(List<string> saidNow, TaxiRoute route, int segmentIndex, double lat, double lon)
    {
        int stopSeg = route.Segments.FindIndex(s => s.ToNode.NodeId == StopNodeId);
        if (stopSeg < 0) return false;
        switch (Variant)
        {
            case Early:
                return segmentIndex == stopSeg && saidNow.Any(a => CountdownCall.Matches(a).Any(m => XcFlight.NamesRunway(m.Groups[1].Value, Runway)));
            case Hold:
                return saidNow.Any(a => a.Contains("cleared to cross", StringComparison.OrdinalIgnoreCase) && XcFlight.NamesRunway(a, Runway));
            default:
                return ClearedCrossingHolds.MetresPastStop(route, stopSeg + 1, lat, lon) is double past
                       && past > RouteRunwayCrossings.StopPassedToleranceMetres;
        }
    }

    /// <summary>Where the aircraft is as the key is pressed: straight to the stop node, along the route past it, on a runway's pavement.</summary>
    public void Press(double t, double lat, double lon, TaxiRoute route, TaxiGraph graph)
    {
        Pressed = true; PressT = t;
        int stopSeg = route.Segments.FindIndex(s => s.ToNode.NodeId == StopNodeId);
        if (stopSeg >= 0)
        {
            var node = route.Segments[stopSeg].ToNode;
            ToStopM = Geo.Dist(lat, lon, node.Latitude, node.Longitude);
            PastM = ClearedCrossingHolds.MetresPastStop(route, stopSeg + 1, lat, lon) ?? double.NaN;
        }
        PressOnRunway = TaxiGuidanceHarnessBridge.RunwayUnder(graph, lat, lon) != null;
    }

    public string PressNote() => string.Create(CultureInfo.InvariantCulture,
        $"pressed at t={PressT:F1}s, {ToStopM:F0} m from the stop node, {PastM:F1} m past it along the route{(PressOnRunway ? ", on runway pavement" : "")}");

    /// <summary>The flight's outcome for the counts: "ok", or the first KEY_ code it was given.</summary>
    public string Outcome(List<Finding> fs)
        => fs.Select(f => f.Code).Where(c => c.StartsWith("KEY_", StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal).FirstOrDefault() ?? "ok";

    /// <summary>The KEYS| results line's fields after the kind, airport and key.</summary>
    public string ResultFields() => string.Join("|", Variant, Runway, "n" + StopNodeId, $"pressed={(Pressed ? 1 : 0)}",
        string.Create(CultureInfo.InvariantCulture, $"t={PressT:F1}"), string.Create(CultureInfo.InvariantCulture, $"to_stop_m={ToStopM:F0}"),
        string.Create(CultureInfo.InvariantCulture, $"past_m={PastM:F1}"), $"on_runway={(PressOnRunway ? 1 : 0)}", $"restored={(Restored ? 1 : 0)}",
        $"held={HeldAt}", $"held_on_runway={(HeldOnRunway != null ? 1 : 0)}", $"line={Xc?.Line ?? 0}", $"early={Xc?.Early ?? 0}",
        $"reached={(Xc?.Reached == true ? 1 : 0)}", Label, Answer);
}
#endif

/// <summary>Simulated AI aircraft for the traffic mode.</summary>
sealed class SimTraffic
{
    public uint Id; public double Lat, Lon, Heading, Gs, AltFt; public bool OnGround = true;
    public string Callsign = "", Airline = "", Type = "", Role = "", Spoken = "";
    public bool OnRoute, Straight, Dormant; public double RouteD, MovingSince = -1; public double PastEndAt = -1;
    public static (string cs, string airline, string type)[] Pool() => new[]
    {
        ("BAW123", "British Airways", "A320"), ("DLH4AB", "Lufthansa", "A359"), ("EZY12KP", "easyJet", "A20N"),
        ("UAL901", "United", "B789"), ("N123AB", "", "C172"), ("AFR66", "Air France", "B77W"), ("RYR5TX", "Ryanair", "B738"),
    };
}

/// <summary>The simulator side of GroundTrafficMonitor: answers a traffic request synchronously.</summary>
sealed class FakeTrafficSource : IGroundTrafficSimSource
{
    public List<SimTraffic> Traffic = new();
    public MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition? Position;
    public bool IsConnected => true;
    public bool? LastKnownOnGround { get; set; } = true;
    public MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition? LastKnownPosition => Position;
    public void RequestAircraftPosition() { }
    public void RequestAircraftPositionAsync(Action<MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition> cb) { if (Position is { } p) cb(p); }
    // The monitor learns the id it waits for from RequestGroundTrafficData's return value, so the
    // answer is delivered AFTER the tick (CompleteSweep), exactly as SimConnect's arrives later.
    private uint _nextId = (uint)MSFSBlindAssist.SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
    private uint _pendingId;
    public uint RequestGroundTrafficData(uint radiusMeters)
    {
        uint first = (uint)MSFSBlindAssist.SimConnect.SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
        uint id = _nextId;
        _nextId = first + (_nextId - first + 1) % MSFSBlindAssist.SimConnect.SimConnectManager.GroundTrafficRequestIdCount;
        _pendingId = id;
        return id;
    }
    public void CompleteSweep()
    {
        uint id = _pendingId;
        if (id == 0) return;
        _pendingId = 0;
        foreach (var a in Traffic.Where(z => !z.Dormant))
            AiTrafficReceived?.Invoke(this, new MSFSBlindAssist.SimConnect.AiTrafficDataEventArgs
            {
                ObjectId = a.Id, Callsign = a.Callsign, AircraftType = a.Type, Airline = a.Airline,
                Latitude = a.Lat, Longitude = a.Lon, AltitudeFt = a.AltFt, HeadingMagnetic = a.Heading,
                GroundSpeedKnots = a.Gs, OnGround = a.OnGround,
            });
        GroundTrafficSweepCompleted?.Invoke(this, new MSFSBlindAssist.SimConnect.GroundTrafficSweepEventArgs(id));
    }
    public event EventHandler<MSFSBlindAssist.SimConnect.AiTrafficDataEventArgs>? AiTrafficReceived;
    public event EventHandler<MSFSBlindAssist.SimConnect.GroundTrafficSweepEventArgs>? GroundTrafficSweepCompleted;
}

/// <summary>Everything the traffic monitor said, with whether it interrupted.</summary>
sealed class TrafficCap : ScreenReaderAnnouncer
{
    public sealed record Said(double T, string Text, bool Immediate, bool Holding);
    public readonly List<Said> All = new();
    public List<string> ThisTick = new();
    public int ImmediateThisTick;
    private double _t; private bool _holding;
    public TrafficCap() : base(IntPtr.Zero) { }
    public void Begin(double t, TaxiGuidanceState st)
    { _t = t; _holding = st == TaxiGuidanceState.HoldShort; ThisTick = new(); ImmediateThisTick = 0; }
    private void Say(string m, bool imm) { All.Add(new Said(_t, m, imm, _holding)); ThisTick.Add((imm ? "!" : "") + m); if (imm) ImmediateThisTick++; }
    public override void Announce(string m) => Say(m, false);
    public override void AnnounceImmediate(string m) => Say(m, true);
    public override void AnnounceQueued(string m) => Say(m, false);
    public override void AnnounceWithQueue(string m) => Say(m, false);
}

/// <summary>A route as a polyline with cumulative distance, for placing and moving traffic.</summary>
sealed class RouteLine
{
    private readonly List<(double la, double lo, double d)> _p = new();
    public double Total => _p[^1].d;
    public RouteLine(TaxiRoute r)
    {
        double acc = 0;
        _p.Add((r.Segments[0].FromNode.Latitude, r.Segments[0].FromNode.Longitude, 0));
        foreach (var s in r.Segments) { acc += s.DistanceMeters; _p.Add((s.ToNode.Latitude, s.ToNode.Longitude, acc)); }
    }
    public (double la, double lo, double brg) At(double d)
    {
        d = Math.Clamp(d, 0, Total);
        for (int i = 1; i < _p.Count; i++)
            if (_p[i].d >= d && _p[i].d > _p[i - 1].d)
            {
                double f = (d - _p[i - 1].d) / (_p[i].d - _p[i - 1].d);
                double brg = (Math.Atan2((_p[i].lo - _p[i - 1].lo) * Math.Cos(_p[i].la * Math.PI / 180), _p[i].la - _p[i - 1].la) * 180 / Math.PI + 360) % 360;
                return (_p[i - 1].la + f * (_p[i].la - _p[i - 1].la), _p[i - 1].lo + f * (_p[i].lo - _p[i - 1].lo), brg);
            }
        return (_p[^1].la, _p[^1].lo, 0);
    }
    public double DistanceTo(double la, double lo)
    {
        double best = double.MaxValue;
        double kx = 111320.0 * Math.Cos(la * Math.PI / 180), ky = 110540.0;
        for (int i = 0; i + 1 < _p.Count; i++)
        {
            double ax = (_p[i].lo - lo) * kx, ay = (_p[i].la - la) * ky;
            double bx = (_p[i + 1].lo - lo) * kx, by = (_p[i + 1].la - la) * ky;
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-9 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / l2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2)));
        }
        return best;
    }
}
#endif

