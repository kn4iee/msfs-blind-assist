using System.ComponentModel;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Taxi guidance form. Allows blind users to select a destination and taxiway route,
/// then activates real-time steering guidance.
///
/// Design:
/// - Airport ICAO input, auto-filled with the airport the aircraft is at (CurrentAirport.Resolve, via MainForm.OpenTaxiForm)
/// - Destination type selection (Runway; Gate, stand or parking; Progressive Taxi, which has none; De-icing area; Place)
/// - Destination combo (runways or gates sorted by distance)
/// - The clearance as a list of steps, in ATC's words and order: a step kind (Via, Left onto, Right onto, Cross,
///   Hold short of), a name box whose list follows the kind (ClearanceNameLists), an optional "At" for a runway
///   crossing, Add Step, the Clearance list (each step read as a sentence), Remove Step and Clear Steps
/// - Screen reader optimized tab order
/// </summary>
public class TaxiAssistForm : Form
{
    // Shared with DockingGuidanceManager/MainForm.AircraftSwitch/SimConnectManager.Dispatch —
    // all writers of docking-aircraft.log now serialize through this one channel.
    private static readonly LogChannel _dockingAircraftLog = Log.Channel("docking-aircraft");
    private static readonly LogChannel _taxiRouterLog = Log.Channel("taxi_router");

    private readonly IAirportDataProvider _dataProvider;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiGuidanceManager _guidanceManager;
    private readonly Services.GateDataSource? _gateSource;
    // When non-null, OnCalculateClicked fires GSX gate auto-select for gate destinations
    // (if the setting is on and GSX is available). The selector is constructed in MainForm
    // and is only non-null when GSX is installed. Unlike the retired menu-walking selector,
    // there is no separate "CouatlStarted" gate to check here: GsxRemoteGateSelector
    // feature-checks the 'gate' capability itself, on every call, before ever sending
    // gate.select -- so a non-null selector against a GSX that isn't running returns
    // Unavailable, and one against a connected build older than 4.0.8 returns
    // GateSelectUnsupported; either way it falls through to manual routing with no wasted
    // send. Only the second is spoken (once) -- see SelectGsxGateAsync.
    private readonly Services.Gsx.Remote.GsxRemoteGateSelector? _gsxGateSelector;
    // Once-per-instance latch for the "gate selection needs GSX 4.0.8" message — see
    // SelectGsxGateAsync. In practice that means once per APP session, not once per time
    // the dialog is opened: MainForm caches this form (GetOrCreateTaxiAssistForm) and
    // OnFormClosing cancels a user close and Hide()s instead, so the instance — and this
    // latch with it — survives closing and reopening the dialog. That is the right amount
    // of speech either way: the fact does not change until the pilot updates GSX, which
    // needs a sim restart anyway, and this path runs on EVERY gate-destination Calculate.
    // Instance state rather than static so it cannot outlive the form it describes.
    private bool _gsxUnsupportedAnnounced;
    // Optional. When non-null, OnCalculateClicked refreshes aircraft position
    // from `LastKnownPosition` (or via RequestAircraftPositionAsync) right
    // before computing the route, so the route starts from where the aircraft
    // ACTUALLY is — not from a stale snapshot taken when the form opened.
    // Critical for the open-form-then-push-back workflow: without this, the
    // route is computed from the gate, the post-pushback aircraft is already
    // off that route, and off-route detection recalcs immediately.
    private readonly MSFSBlindAssist.SimConnect.SimConnectManager? _simConnectManager;
    private readonly TcasService? _tcasService;
    // ~55 m — tight enough to distinguish adjacent gates but large enough to catch an
    // aircraft that has stopped just short of the spot centre. Class-scoped because BOTH
    // the destination-list occupancy filter and the Calculate-time warning measure with
    // it; two copies could drift and make the list and the warning disagree.
    private const double GATE_OCCUPIED_NM = 0.030;
    // Refreshed at the top of LoadAirportData from _simConnectManager?.AircraftWingSpan
    // so a mid-session aircraft swap (multi-aircraft architecture) is honored on the
    // next form open. Constructor parameter is preserved as a fallback for callers
    // that don't pass a SimConnectManager.
    private double _aircraftWingspan;

    // Form controls
    private Label lblAirport = null!;
    private TextBox txtAirport = null!;
    private Label lblDestType = null!;
    private ComboBox cmbDestType = null!;
    private Label lblDestination = null!;
    private CheckBox chkFitFilter = null!;
    // Gate destinations only: leaves stands with an aircraft parked on them out of the
    // list. See the occupancy filter in PopulateDestinations and SpotIsOccupied.
    private CheckBox chkHideOccupied = null!;
    private Label lblGateSearch = null!;
    private TextBox txtGateSearch = null!;
    private ComboBox cmbDestination = null!;
    // The Runway entry list (runway destinations only, stage 4 D9): "Full length", then every
    // taxiway intersection and every PAINTED holding point (OSM aeroway=holding_position refs,
    // LSZH "A2", covering entries whose stub is UNNAMED in navdata) down the runway, built by
    // RunwayEntryList.Build (PopulateRunwayEntries). An intersection lines up partway down at
    // that taxiway; a holding point SELECTS the entry node (hold-short placement stays
    // navdata's), a partway one moving the lineup target too. _runwayEntries holds the items
    // in the combo's order (the combo shows their labels); Calculate reads SelectedRunwayEntry.
    private Label lblRunwayEntry = null!;
    private ComboBox cmbRunwayEntry = null!;
    private IReadOnlyList<RunwayEntryItem> _runwayEntries = Array.Empty<RunwayEntryItem>();
    // Last runway whose default (Full length) holding point was announced, so
    // re-entering the same selection doesn't repeat the call-out. Cleared on airport
    // load. See AnnounceDefaultHoldingPoint.
    private string _lastHoldingPointAnnounceKey = "";
    // Set while the destination combo is being rebuilt or driven programmatically (the
    // list rebuild re-selects index 0, and the SayIntentions import selects the imported
    // destination). Both raise SelectedIndexChanged without the pilot choosing anything,
    // and the import speaks its own single summary that a holding-point call-out must
    // not talk over.
    private bool _suppressHoldingPointAnnounce;
    // Set while an EXTERNAL destination (the SayIntentions assigned gate) is being
    // resolved and kept set once one has been seated: the occupied-stands filter is a
    // browsing aid, but an assigned gate with AI parked on it is still the assigned
    // gate, and hiding it makes the name, alias AND position steps all miss — the
    // whole chain then falls through to the ARRIVAL RUNWAY, the failure those
    // fallbacks exist to stop. Latched (not restored on success) so a later list
    // rebuild cannot drop the stand the pilot is being taxied to; cleared when the
    // pilot toggles the checkbox themselves and on every airport load. The occupancy
    // WARNING still reaches them at Calculate (CheckGateOccupancy).
    private bool _suppressOccupiedFilter;
    // Same shape, same lifetime, for the wingspan "show fitting only" filter — and for
    // a stronger reason. The occupancy filter reads a live traffic feed; this one reads
    // SCENERY-AUTHORED size data (a navdata parking radius, or a GSX max wing span), and
    // that data is frequently wrong. So a stand the controller NAMED, at an airport the
    // aircraft is really going to, outranks a number saying it will not fit: the pilot
    // was told to go there. Left applied, the whole gate half of a SayIntentions import
    // failed at once — PopulateDestinations drops a non-fitting spot BEFORE its label
    // reaches the combo and the two maps behind it, so the name, this scenery's aliases
    // and the published coordinate all had nothing to match against, and a known arrival
    // announced "this scenery has no stand under that label" for a stand it does have.
    // Latched on a successful seat (a later rebuild — the Calculate-path and show-path
    // gate-source refreshes both repopulate — must not drop the stand being taxied to);
    // cleared when the pilot toggles the checkbox themselves and on every airport load.
    private bool _suppressFitFilter;
    // CAT III / low-visibility hold (runway destinations only). When ticked, a
    // runway-destination route holds at the CAT III / ILS hold-short (further
    // back, protects the ILS critical area — e.g. EGKK A3/C3/M3) instead of the
    // default full-length line (A1/M1). Passed to LoadRoute as preferIlsHold.
    private CheckBox chkCatIiiHold = null!;
    // Runway combo items carry this affordance when the taxi network only reaches
    // the runway partway down (TaxiGraph.RunwayNeedsBacktrack — LICR 15/33 class),
    // so a blind pilot hears "backtrack required" WHILE ARROWING the destination
    // list, as part of the item's own label — never as a separate announcement
    // (the screen reader already speaks the item; a second voice would step on
    // it). The suffixed label is the key for every _destination*Map, and every
    // consumer that PARSES or SPEAKS the runway name strips it first via
    // StripBacktrackSuffix. The SayIntentions import needs no change:
    // MatchDestinationLabel's CleanRunway extracts the designator by regex from
    // anywhere in the label.
    internal const string BacktrackRequiredSuffix = " (backtrack required)";

    internal static string StripBacktrackSuffix(string label)
        => label.EndsWith(BacktrackRequiredSuffix, StringComparison.OrdinalIgnoreCase)
            ? label[..^BacktrackRequiredSuffix.Length].TrimEnd()
            : label;
    // Full-length backtrack departure (runway destinations only). When ticked, and
    // the airport requires it, guidance holds short of an intermediate runway
    // entrance, then backtracks to the departure threshold and lines up full length
    // (e.g. iniBuilds EGNM 32 via D1, EGGW). Passed to LoadRoute as
    // fullLengthBacktrack; the entrance node is found via FindBacktrackEntryNode.
    private CheckBox chkFullLengthBacktrack = null!;
    // The step block: the clearance as ATC said it, one step at a time (spec 8.2). The kind
    // list picks what the name box lists (ClearanceNameLists); the "At" box shows only for a
    // runway crossing; Add Step puts the step at the end of the Clearance list, which reads
    // each step as a sentence, or, after Enter on a step there, replaces that step in place
    // (_changingStepIndex). _steps is the list itself, in the order entered, and the one
    // thing Calculate reads (ClearancePlanner.Build).
    private Label lblStepKind = null!;
    private ComboBox cmbStepKind = null!;
    private Label lblStepName = null!;
    private ComboBox cmbStepName = null!;
    private Label lblStepAt = null!;
    private ComboBox cmbStepAt = null!;
    private Button btnAddStep = null!;
    private Label lblClearance = null!;
    private ListBox lstClearance = null!;
    private Button btnRemoveStep = null!;
    private Button btnClearSteps = null!;
    private readonly List<ClearanceStep> _steps = new();
    // What the list is trimmed from while a route is live (spec 8.2): the steps the last successful Calculate used, as
    // it used them (guidance's progress counts Via steps and numbers crossings by THIS list, so a trim always starts from
    // it, never from a list already trimmed), and whether that Calculate had a destination. Null when the list holds no
    // leg being flown: none calculated yet, or emptied since (Clear Steps, the fill, an airport change, a leg's end).
    private List<ClearanceStep>? _calculatedSteps;
    private bool _calculatedWithDestination;
    // For each step in _steps, its index in _calculatedSteps; -1 for a step added since. And the indexes of the
    // calculated steps the pilot removed since (Remove Step, a made crossing dropped by an append): a trim never brings
    // one back.
    private readonly List<int> _stepOrigins = new();
    private readonly HashSet<int> _removedOrigins = new();
    // The step being changed in place (Enter on it in the Clearance list, BeginStepChange), -1 when none: the step boxes
    // hold it, Add Step replaces it (ReplaceStepFromInputs), Escape keeps it (CancelStepChange). Anything else that changes
    // the list closes the change first, so a replacement never lands on a step that has moved. UI thread only.
    private int _changingStepIndex = -1;
    // The "At" box's item for the place of the runway crossing being changed when no offered item names it (a crossing
    // made at a holding point now behind the aircraft): ClearanceStepEntry.OwnPlaceLabel, rebuilt with the At list; a pick
    // of it keeps the step's place (ClearanceStepEntry.BuildForChange). Null when there is none.
    private string? _changingOwnAtLabel;
    // Add Step's own text, name and description, put back when a change closes (EndStepChange): kept here so the button's
    // text is written as a literal once (ClearanceStepEntryTests reads the access keys from the Text literals).
    private string _addStepText = "";
    private string _addStepName = "";
    private string _addStepDescription = "";
    private const string ReplaceStepDescription =
        "Replace the step being changed with the one in the boxes. Enter in the step kind, name or At box does the same. Escape keeps the step as it was.";
    // Bumped by every successful Calculate. A guidance event queues its action with the number it saw, and the action
    // is skipped when a Calculate has built a list since: a late leg end never empties the list of the leg just set.
    private int _calculateGeneration;
    // How many times the window has been shown (PrepareForOpening: the Airport box the first time only).
    private int _timesShown;
    // Where focus lands as the window opens (PrepareForOpening), focused by FocusForOpening if activation has not.
    private Control? _openingFocusControl;
    // Whether the "At" box is shown (a Cross or Hold short of step with a runway chosen, ClearanceStepEntry.ShowsAtBox).
    // A field, not cmbStepAt.Visible, which also reads false whenever the form itself is hidden.
    private bool _stepAtShown;
    // Set while the kind is set by code (an access key, Add Step), so the kind list's own
    // change handler does not refill the name box a second time.
    private bool _settingStepKind;
    // Set while the name box is refilled, so the "At" box is rebuilt once, after the fill.
    private bool _fillingStepName;
    // The step sentence waiting for the screen reader to finish naming the box focus just
    // moved to (see SpeakStepAdded), and the one-shot timer that speaks it.
    private string? _pendingStepSpeech;
    private System.Windows.Forms.Timer? _stepSpeechTimer;
    // Once a second while the window is shown (started and stopped in OnVisibleChanged), created once: the step list
    // follows guidance while this is the active window (OnFollowTimerTick, FollowGuidance).
    private System.Windows.Forms.Timer? _followTimer;
    // Published NAMED holding points (VIKAS, N2E…) for the loaded airport:
    // online-sourced designators resolved onto navdata graph nodes by
    // NamedHoldingPointResolver. Empty when augmentation is off, the airport has
    // none, or the online fetch hasn't landed yet (the hold-short lists retry the
    // resolve on demand so a late background fetch still surfaces).
    private List<NamedHoldingPoint> _namedHoldingPoints = new();
    // True once ResolveNamedHoldingPoints has actually SEEN online holding-point data
    // for the loaded airport — whether or not any of it resolved. Gates the retry in
    // RefreshStepNameList, RefreshStepAtList and EnsureHoldingPointsListed: while the async
    // fetch hasn't landed the raw list is empty and retrying costs nothing (the resolve
    // early-returns before scanning), but once raw data has arrived the O(points × nodes)
    // scan must not repeat on every list refill.
    private bool _namedHoldingPointsResolved;
    // "At" HOLDING POINTS offered for a runway crossing or hold short ("cross runway 23R at P1",
    // "hold short of runway 27L at A2"), keyed by their label in the "At" box. Rebuilt with that
    // list (RefreshStepAtList) and read when the step is added, so a label only ever resolves to
    // a point that had a genuine crossing when it was offered.
    private readonly Dictionary<string, NamedHoldingPoint> _crossAtHoldPoints = new();
    private const string CrossAtHoldPointSuffix = HoldShortTargetList.HoldingPointSuffix;
    private Button btnSayIntentions = null!;
    private Button btnCalculate = null!;
    private Button btnStop = null!;
    private Label lblStatus = null!;
    private Label lblRouteSummary = null!;
    private TextBox txtRouteSummary = null!;

    // The panel that holds the step block (it held the taxiway rows before the step list).
    private Panel pnlSteps = null!;

    // Cached runway designators for the current airport (e.g. ["09L","09R","27L","27R"]).
    // Populated on airport load; the runways the Cross and Hold short of name lists offer.
    private List<string> _airportRunwayIds = new();

    // State
    private TaxiGraph? _graph;
    private string _currentIcao = "";
    /// <summary>
    /// The gate-list source token (<see cref="Services.GateDataSource.GetGateListVersion"/>) that
    /// was live when <see cref="_graph"/> was built — the graph's own staleness key, alongside
    /// <see cref="_currentIcao"/>.
    ///
    /// <para>
    /// Stand names are baked into the graph's nodes at build time, so a graph built before GSX
    /// published this airport carries navdata's concourse letters permanently. The dropdown cache
    /// beside it already rebuilt on this token; the graph did not, and the same-ICAO early return
    /// in <c>LoadAirportDataCoreAsync</c> meant it never could. Pre-planning the arrival during
    /// descent therefore produced a form whose combo offered "B 25" while the graph it handed to
    /// guidance as <c>prebuiltGraph</c> called the same stand "A 25" — and
    /// <c>TaxiGuidanceManager.DescribeCurrentLocation</c> prefers that installed graph, so
    /// Where-Am-I said "A 25" on arrival at the stand the pilot had just been routed to as B 25.
    /// </para>
    /// </summary>
    private string _graphSourceToken = "";
    // The airport's online-data version (TaxiGuidanceManager.HoldingPointDataVersion) read just
    // before the taxi paths _graph was built from: Where Am I takes this form's holding points only
    // while it is current, since a graph built before the online data landed lacks its names.
    private long _graphHoldingPointDataVersion;
    private double _aircraftLat, _aircraftLon, _aircraftHeading;
    // Per-ICAO memo of GetRunways for the intersection picker, which re-lists on
    // every checkbox toggle / runway change. GetRunways opens a fresh SQLite
    // connection per call, so caching the (session-stable) runway set for the
    // loaded airport avoids a UI-thread DB round-trip on each interaction.
    private List<Runway>? _cachedRunways;
    private string _cachedRunwaysIcao = "";

    // Destination nodes for routing
    private Dictionary<string, int> _destinationNodeMap = new();
    private Dictionary<string, double> _destinationHeadingMap = new();
    private Dictionary<string, double> _destinationHeadingTrueMap = new();
    private Dictionary<string, (double lat, double lon)> _destinationThresholdMap = new();
    // Maps "Runway X" → Runway for every open runway end of the loaded airport, for every destination
    // type (a Cross step can come with any destination): the "At" box's crossing places, the name lists'
    // "runways your last taxiway crosses first", and the far-side / near-side node a leg with no
    // destination ends at. Filled on airport load (LoadAirportDataCoreAsync).
    private Dictionary<string, Runway> _crossRunwayMap = new();
    // Gate mode: maps the display label (same key as _destinationNodeMap) → ParkingSpot.
    // Populated in the gate branch of PopulateDestinations so OnCalculateClicked can pass the
    // actual ParkingSpot to GsxRemoteGateSelector without re-querying the data provider.
    private Dictionary<string, ParkingSpot> _destinationSpotMap = new();

    /// <summary>Non-building read of the surroundings catalog (MainForm wires
    /// <c>SurroundingsCatalogCache.TryGetCached</c>). Never builds: it is called synchronously from
    /// <see cref="PopulateDestinations"/> on the UI thread.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, Navigation.Surroundings.AirportFeatureCatalog?>? SurroundingsCatalogCached { get; set; }

    /// <summary>The catalog for an ICAO, built off the UI thread if need be
    /// (<c>SurroundingsCatalogCache.GetAsync</c>, single-flight).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<string, Task<Navigation.Surroundings.AirportFeatureCatalog?>>? SurroundingsCatalogAsync { get; set; }

    // The catalog the Place list was last built from, for _placeCatalogIcao. Kept so the list can
    // always be rebuilt at once (a filter toggle, a gate-source move) while a fresher one loads.
    private Navigation.Surroundings.AirportFeatureCatalog? _placeCatalog;
    private string _placeCatalogIcao = "";

    // The one Place load in flight: its id (a newer load or an airport change makes an older
    // settle stale) and whether it is a background refresh nobody asked for.
    private int _placesLoadId;
    private bool _placesLoading;
    private bool _placesLoadBackground;
    // True while a settling load rebuilds the list, so that rebuild never starts another load.
    private bool _placesSettling;

    // True while a programmatic type restore must stay silent (RestoreDestinationState).
    private bool _suppressPlaceAnnounce;

    // A background refresh that arrived while the destination list was open, run once it closes.
    private string? _placesRefreshAfterDropDown;

    // A Place label to put back once the loading list is filled (a destination restore, or a
    // gate-source move, that landed while the list was still empty).
    private string? _placeToReselect;

    // Gate-branch cache (Fix: per-keystroke gate-list rebuild). PopulateDestinations
    // runs on every txtGateSearch keystroke, every chkFitFilter toggle, and on each
    // dest-type change. The expensive work in the GATE branch — GateDataSource.GetGates
    // (directory enumeration / uncached navdata DB query at .py-only airports like EDDF),
    // plus a _graph.FindNearestNode + distance check per spot — depends ONLY on the
    // airport (ICAO + graph), not on the search text or fit filter. We resolve it ONCE
    // per airport into _cachedGateSpots (spot + its routing node id), and each
    // PopulateDestinations pass merely applies the search + wingspan filters and rebuilds
    // the combo/map entries in memory. Mirrors how GateTeleportForm loads once + filters.
    //
    // The wingspan fit-filter is deliberately NOT baked into the cache: _aircraftWingspan
    // can change between passes (mid-session aircraft swap), so it must re-apply per pass
    // against the full cached list.
    //
    // The cache is keyed on the ICAO AND on GateDataSource.GetGateListVersion's token — the
    // SOURCE the gate list came from, not just the airport. Without the token, a list bound
    // from the .ini/navdata fallback BEFORE GSX had published the airport (the arrival
    // pre-planned during descent, or the spawn before the first handlerData frame) was served
    // for the rest of the session: this form is hide-on-close, LoadAirportDataCoreAsync
    // early-returns for a same-ICAO reload, and nothing else ever invalidated it. Those spots
    // carry GsxIdentifier == null (only the Remote API reader sets it), so every gate
    // destination Calculate -> SelectGsxGateAsync -> BadArgs -> "GSX could not prepare this
    // stand." until a DIFFERENT airport was loaded. The token is O(1) to compute (a property
    // read, no file, no DB), which is what keeps the per-keystroke path cheap — the invariant
    // this cache exists to protect. See RefreshDestinationsIfGateSourceChanged for the two
    // extra moments it is checked outside PopulateDestinations.
    private List<(ParkingSpot spot, int nodeId)>? _cachedGateSpots;
    private string _cachedGateSpotsIcao = "";

    // TAXI distance from the aircraft to every graph node, for ordering the stand list.
    // Cached because PopulateDestinations re-runs on every gate-search keystroke and every
    // filter toggle, while a Dijkstra over a big airport's graph (EGLL: 6,085 nodes,
    // 12,792 edges) is ~100 ms of UI thread. Keyed on the ANCHOR NODE, not on a position:
    // the anchor only changes when the aircraft's nearest node changes, which is the only
    // thing that can reorder the list, and testing it is a single FindNearestNode.
    private Dictionary<int, double>? _gateTaxiDistances;
    private int _gateTaxiDistancesAnchor = -1;
    private string _gateTaxiDistancesIcao = "";

    // Rank offset for a stand the taxi network cannot reach from here. FINITE on purpose:
    // double.MaxValue + x is a floating-point no-op, which ties every unreachable stand
    // and lets the sort order them arbitrarily (the EVRA lesson, TaxiRouter). Larger than
    // any real taxi distance, so a reachable stand always sorts first, and the straight-line
    // distance added on top still orders the unreachable ones sensibly among themselves.
    private const double UNREACHABLE_STAND_RANK_M = 1_000_000.0;
    private string _cachedGateSpotsSourceToken = "";

    // Navdata stands the SELECTABLE list does not carry (GSX drops fuel and headingless stands).
    // A Place may still end there — without a GSX identity, so gate.select is never sent for it.
    // Keyed on the same (ICAO, gate-list token) pair as _cachedGateSpots and reset with it: the
    // set is defined by SUBTRACTING the selectable list, so it moves whenever that list does.
    private List<Navigation.Surroundings.StandCandidate>? _cachedNavdataOnlyStands;

    // Docking guidance manager: receives the selected gate so proximity audio
    // and lateral tone can guide the pilot to the stop position. Set in
    // OnCalculateClicked for gate destinations; cleared on runway destinations.
    private readonly Services.DockingGuidanceManager? _dockingManager;

    // Resolves the GSX .py per-aircraft stop offset for a navdata/.py gate so the
    // docking stop moves to where GSX's VDGS would stop THIS airframe. Lazy + cached.
    private readonly Services.Gsx.GsxStopOffsetResolver _stopOffsetResolver = new();

    /// <summary>Runs the SayIntentions taxi-route import — the same operation the
    /// Ctrl+Shift+Y hotkey performs. Supplied by the caller rather than reached through a
    /// MainForm reference, so this form keeps knowing nothing about MainForm (the pattern
    /// TaxiGuidancePanel already uses for its taxiway-name refresh). Null when the caller
    /// has no import to offer, and the button then stays present but disabled — a control
    /// that appears and disappears is worse to navigate than one consistently there.</summary>
    private readonly Func<Task>? _importFromSayIntentions;

    public TaxiAssistForm(
        IAirportDataProvider dataProvider,
        ScreenReaderAnnouncer announcer,
        TaxiGuidanceManager guidanceManager,
        MSFSBlindAssist.SimConnect.SimConnectManager? simConnectManager = null,
        TcasService? tcasService = null,
        double aircraftWingspan = 0,
        Services.GateDataSource? gateSource = null,
        Services.Gsx.Remote.GsxRemoteGateSelector? gsxGateSelector = null,
        Services.DockingGuidanceManager? dockingManager = null,
        Func<Task>? importFromSayIntentions = null)
    {
        _dataProvider = dataProvider;
        _announcer = announcer;
        _guidanceManager = guidanceManager;
        _simConnectManager = simConnectManager;
        _tcasService = tcasService;
        _aircraftWingspan = aircraftWingspan;
        _gateSource = gateSource;
        _gsxGateSelector = gsxGateSelector;
        _dockingManager = dockingManager;
        _importFromSayIntentions = importFromSayIntentions;
        InitializeFormControls();

        // The step list follows guidance (spec 8.2, 8.8): emptied when its leg ends, a crossing the Hold short key puts
        // back shown as its hold short. Form-scoped: unsubscribed in Dispose.
        _guidanceManager.StateChanged += OnGuidanceStateChanged;
        _guidanceManager.ClearedCrossingsChanged += OnClearedCrossingsChanged;
    }

    /// <summary>
    /// Sets the aircraft position for initial taxiway sorting and graph building.
    /// Call before Show().
    /// </summary>
    public void SetAircraftPosition(double lat, double lon, double heading, string nearestIcao)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;

        if (!string.IsNullOrEmpty(nearestIcao))
        {
            txtAirport.Text = nearestIcao.ToUpperInvariant();
            _ = LoadAirportDataSafeAsync(nearestIcao);
        }
    }

    /// <summary>
    /// Refreshes aircraft position while the form is open. MainForm calls this on every
    /// position update so that when the user presses Calculate — especially during a
    /// mid-taxi route amendment — the route starts from the CURRENT position, not from
    /// wherever the aircraft was when the form opened.
    /// </summary>
    public void UpdateAircraftPosition(double lat, double lon, double heading)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;
    }

    /// <summary>
    /// The aircraft heading in the TRUE frame, for consumers that compare against
    /// graph/segment bearings (which are all true): LoadRoute's
    /// FindNearestNodeInDirection start-node cone, and the direction-aware
    /// taxiway sort. `_aircraftHeading` itself stays MAGNETIC (the frame its
    /// writers deliver and the HeadingExitSideSign contract pins); passing it to
    /// a true-bearing comparison biased the pick by the full local variation
    /// (20-30° at high-magvar airports), and disagreed with the recalc/rollout
    /// LoadRoute call sites, which already pass true. Falls back to the raw
    /// magnetic value when no variation sample exists yet — the pre-fix behavior.
    /// </summary>
    // _aircraftHeading is MAGNETIC; this is the TRUE heading every LoadRoute call is given. Never
    // also pass aircraftHeadingMagVar to LoadRoute: the route-start turn cue would be rotated by the
    // variation twice and could contradict the tone near a U-turn.
    private double AircraftHeadingTrue()
        => _simConnectManager?.LastKnownPosition is { } p
            ? _aircraftHeading + p.MagneticVariation
            : _aircraftHeading;

    private void InitializeFormControls()
    {
        this.Text = "Taxi Guidance";
        this.Size = new System.Drawing.Size(420, 480);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.KeyPreview = true;
        this.KeyDown += OnFormKeyDown;

        int y = 15;
        int labelX = 15;
        int controlX = 15;
        int controlWidth = 370;

        // Access keys (each letter on one control only: Windows Forms does not cycle a shared
        // letter, it takes the first match after the focused control, so a shared letter can
        // press the wrong button; ClearanceStepEntryTests pins one control per letter):
        //   Alt+A  Airport (ICAO)                  Alt+T  Destination type
        //   Alt+G  Gate search                     Alt+E  Destination (D&estination)
        //   Alt+F  Show fitting only               Alt+O  Hide occupied stands
        //   Alt+I  Runway entry list               Alt+3  CAT III / low-visibility hold
        //   Alt+B  Full-length departure (backtrack)
        //   Alt+K  Step kind list                  Alt+W  Name box (its label follows the kind)
        //   Alt+D  Add Step                        Alt+U  Clearance list
        //   Alt+M  Remove Step                     Alt+Z  Clear Steps
        //   Alt+V, Alt+L, Alt+R, Alt+X, Alt+H  pick the step kind (Via, Left onto, Right onto,
        //          Cross, Hold short of) and put focus in the name box, from anywhere in the
        //          window (ProcessMnemonic): no control may take these five letters
        //   Alt+Y  Fill from SayIntentions         Alt+C  Calculate Route
        //   Alt+S  Stop Guidance                   Alt+N  Last route summary ("(&N)": no letter
        //          of its own words is free, and it once shared S, which stopped guidance)
        //   Enter on a step in the Clearance list loads it into the step boxes to change it in
        //          place: Add Step reads "Replace step N" (still Alt+D, ClearanceStepEntry's
        //          ReplaceButtonText) and Escape keeps the step; with no change open, Escape
        //          closes the window (OnFormKeyDown); in a dropped-down combo list Escape
        //          only closes the list, before either of those
        //
        // Tab order (top to bottom, explicit TabIndex below; a label with an access key sits
        // immediately before its control, or the key lands on whatever follows its number):
        //   Airport, Destination type, Gate search, Destination, the runway options (Runway
        //   entry, CAT III, backtrack), the stand
        //   filters, then the step block (Step kind, name box, At, Add Step, Clearance, Remove
        //   Step, Clear Steps), Fill from SayIntentions, Calculate, Stop, Last route summary.

        // Airport ICAO
        lblAirport = new Label
        {
            Text = "&Airport (ICAO):",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Airport ICAO Label"
        };
        y += 20;
        txtAirport = new TextBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            CharacterCasing = CharacterCasing.Upper,
            AccessibleName = "Airport ICAO",
            AccessibleDescription = "Enter the airport's ICAO code or identifier"
        };
        txtAirport.Leave += (s, e) => _ = LoadAirportDataSafeAsync(txtAirport.Text.Trim());
        y += 30;

        // Destination type
        lblDestType = new Label
        {
            Text = "Destination &type:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Destination type Label"
        };
        y += 20;
        cmbDestType = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Destination type",
            AccessibleDescription = "Select whether to taxi to a runway, a gate, stand or parking position, a progressive taxi with no destination (the steps say where the leg ends), a de-icing area, or a place (FBO, hangar, fuel, terminal, or cargo)"
        };
        // Tested by index throughout (0 runway, 1 stand, 2 no destination, 3 de-icing, 4 place):
        // only the text may change.
        cmbDestType.Items.AddRange(new object[] { "Runway", "Gate, stand or parking", "Progressive Taxi (no destination)", "De-icing area", "Place" });
        cmbDestType.SelectedIndex = 0;
        cmbDestType.SelectedIndexChanged += OnDestTypeChanged;
        y += 30;

        // Destination
        lblDestination = new Label
        {
            Text = "D&estination:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Destination Label"
        };
        chkFitFilter = new CheckBox
        {
            Text = "Show &fitting only",
            Location = new System.Drawing.Point(200, y),
            AutoSize = true,
            Visible = false,
            Checked = _aircraftWingspan > 0,
            Enabled = _aircraftWingspan > 0,
            AccessibleName = "Show only fitting parking spots",
            AccessibleDescription = "When checked, only shows parking spots large enough for your aircraft"
        };
        chkFitFilter.CheckedChanged += (s, e) =>
        {
            // The pilot asserting the filter outranks an import's latched suppression
            // (same rule as chkHideOccupied below).
            _suppressFitFilter = false;
            // Both stand-backed lists honour this filter, so both rebuild under it.
            if (cmbDestType.SelectedIndex is 1 or 4) PopulateDestinations();
        };
        y += 20;

        // Hide stands that currently have an aircraft parked on them. Default ON — an
        // occupied stand is not a destination — but escapable, because it depends on a
        // live traffic feed: with AI traffic off, or a stand whose scenery position sits
        // far from where the AI parks, the filter is the wrong answer and the pilot needs
        // the full list back. Enabled only when there IS a traffic feed.
        chkHideOccupied = new CheckBox
        {
            // Own row (the form is 420 px wide — beside "Show fitting only" it clips).
            Location = new System.Drawing.Point(200, y),
            Text = "Hide &occupied stands",
            AutoSize = true,
            Visible = false,
            Checked = _tcasService != null,
            Enabled = _tcasService != null,
            AccessibleName = "Hide occupied stands",
            AccessibleDescription = "When checked, parking spots with an aircraft already on them are left out of the destination list"
        };
        chkHideOccupied.CheckedChanged += (s, e) =>
        {
            // The pilot asserting the filter outranks an import's latched suppression.
            _suppressOccupiedFilter = false;
            // Both stand-backed lists honour this filter, so both rebuild under it.
            if (cmbDestType.SelectedIndex is 1 or 4) PopulateDestinations();
        };
        y += 20;

        // Gate search box (type-to-filter on name+number+suffix). Hidden
        // until Gate/Parking destination type is selected.
        lblGateSearch = new Label
        {
            Text = "&Gate search:",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            Visible = false,
            AccessibleName = "Gate search label"
        };
        y += 20;
        txtGateSearch = new TextBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            Visible = false,
            AccessibleName = "Gate search",
            AccessibleDescription = "Type a gate letter or number to filter the destination list"
        };
        txtGateSearch.TextChanged += (s, e) =>
        {
            if (cmbDestType.SelectedIndex == 1) PopulateDestinations();
        };
        txtGateSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (cmbDestination.Items.Count > 0)
                {
                    cmbDestination.SelectedIndex = 0;
                    cmbDestination.Focus();
                }
                else
                {
                    _announcer.AnnounceImmediate("No matching gates.");
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        y += 30;

        cmbDestination = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Destination",
            AccessibleDescription = "Select the destination runway or gate"
        };
        // A runway picked (by the pilot or a list rebuild) puts the Runway entry list back at
        // Full length, listing that runway's entries (D12), before the call-out below.
        cmbDestination.SelectedIndexChanged += (s, e) =>
        {
            if (cmbDestType.SelectedIndex == 0)
                PopulateRunwayEntries(keepSelection: false);
            // Tell the pilot which painted holding point a plain full-length departure
            // from this runway uses, so they can tell it apart from the one ATC named.
            AnnounceDefaultHoldingPoint();
        };
        // A background Place refresh that arrived while this list was open is resumed here rather
        // than rebuilding the list under the pilot's reading cursor.
        cmbDestination.DropDownClosed += OnDestinationDropDownClosed;
        y += 30;

        // The Runway entry list (D9). Runway destinations only — hidden for gate /
        // progressive / deice. "Full length" first, then the taxiway intersections and
        // painted holding points down the runway, each with the runway remaining ahead; Calculate
        // enters the runway at the one chosen (an intersection or a partway holding point lines up
        // there instead of at the full-length threshold). A permanent slot (like the gate-search
        // row above) so the layout doesn't jump; the controls just hide/show.
        lblRunwayEntry = new Label
        {
            Text = "Runway entry (&I):",
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            // Visible for the default (Runway) destination type. OnDestTypeChanged
            // is wired AFTER cmbDestType.SelectedIndex = 0, so it never fires during
            // construction — the list must therefore be born in the state that
            // matches the default selection, or the whole feature is invisible on
            // first open until the user toggles the destination type away and back.
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Runway entry label"
        };
        y += 20;
        cmbRunwayEntry = new ComboBox
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Runway entry",
            AccessibleDescription = "Where to enter the runway: full length, or a taxiway intersection or painted holding point further down, each with the runway remaining ahead"
        };
        // Refilled, keeping the choice, when focus arrives or the list opens (D12): the aircraft's
        // side of the runway and the online holding points can change after it was filled, and
        // arrowing a DropDownList never raises DropDown.
        cmbRunwayEntry.Enter += (s, e) => PopulateRunwayEntries(keepSelection: true);
        cmbRunwayEntry.DropDown += (s, e) => PopulateRunwayEntries(keepSelection: true);
        // Never empty: Full length until an airport and a runway give it more.
        PopulateRunwayEntries(keepSelection: false);
        y += 30;

        // CAT III / low-visibility hold. Runway destinations only (same visibility
        // rule as the Runway entry list — see the note there about being born in the
        // default-selection state because OnDestTypeChanged is wired after the
        // initial SelectedIndex = 0). Default OFF = hold at the full-length line
        // (closest to the runway, a normal clearance); ticked = hold further back
        // at the CAT III / ILS hold to protect the ILS critical area in low vis.
        chkCatIiiHold = new CheckBox
        {
            // Alt+3: Alt+L is "Left onto" (ProcessMnemonic).
            Text = "CAT &3 / low-visibility hold (LVP)",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "CAT three, low visibility hold",
            AccessibleDescription = "When checked, hold at the CAT three / ILS hold-short further back from the runway for low-visibility procedures, instead of the full-length hold closest to the runway"
        };
        // Toggling changes WHICH painted line the route holds at, so re-name it.
        chkCatIiiHold.CheckedChanged += (s, e) => AnnounceDefaultHoldingPoint();
        y += 30;

        // Full-length backtrack departure. Runway destinations only (same
        // visibility rule as the Runway entry list / chkCatIiiHold — born visible in the
        // default runway selection because OnDestTypeChanged is wired after the
        // initial SelectedIndex = 0). Default OFF. When ticked, guidance takes the
        // pilot onto the runway at an intermediate entrance, backtracks to the
        // departure threshold, turns around, and lines up full length — for
        // airports with no full-length parallel taxiway (EGNM, EGGW). Offered for
        // EVERY runway (opt-in); if the airport has no such entrance the route
        // falls back to a normal full-length departure with a spoken note.
        chkFullLengthBacktrack = new CheckBox
        {
            Text = "Full-length departure (requires &backtrack)",
            Location = new System.Drawing.Point(controlX, y),
            AutoSize = true,
            Visible = cmbDestType.SelectedIndex == 0,
            AccessibleName = "Full length departure requires backtrack",
            AccessibleDescription = "When checked, and the runway has no full-length taxiway, guidance takes you onto the runway partway down, backtracks to the departure threshold, turns you around, and lines you up full length before takeoff assist"
        };
        y += 30;

        // The step block (spec 8.2): the clearance as ATC said it, in the order said. Every
        // control sits in pnlSteps, which LayoutStepBlock arranges (the "At" line comes and
        // goes); tab order and access keys are set with the rest below.
        lblStepKind = new Label
        {
            Text = "Step &kind:",
            AutoSize = true,
            AccessibleName = "Step kind label"
        };
        cmbStepKind = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Step kind",
            AccessibleDescription = "Alt+V via, Alt+L left onto, Alt+R right onto, Alt+X cross, Alt+H hold short of. Each selects the kind and moves to the name box. Use left onto or right onto only when ATC names the turn; as the first step, left and right are from the way you are facing when you press Calculate, so at a stand calculate after pushback."
        };
        cmbStepKind.Items.AddRange(ClearanceStepEntry.KindItems.Cast<object>().ToArray());
        cmbStepKind.SelectedIndex = (int)ClearanceStepKind.Via;
        cmbStepKind.SelectedIndexChanged += (s, e) => { if (!_settingStepKind) RefreshStepNameList(); };
        cmbStepKind.KeyDown += OnStepBoxKeyDown;

        // The name box: its label, accessible name and list follow the kind (RefreshStepNameList).
        lblStepName = new Label
        {
            Text = ClearanceStepEntry.NameBoxLabel(ClearanceStepKind.Via),
            AutoSize = true,
            AccessibleName = "Name box label"
        };
        cmbStepName = new ComboBox
        {
            Width = controlWidth,
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = ClearanceStepEntry.NameBoxAccessibleName(ClearanceStepKind.Via),
            AccessibleDescription = ClearanceStepEntry.NameBoxDescription(ClearanceStepKind.Via)
        };
        cmbStepName.SelectedIndexChanged += (s, e) => { if (!_fillingStepName) RefreshStepAtList(); };
        cmbStepName.KeyDown += OnStepBoxKeyDown;
        // The holding points come from an online fetch that can land after the list was filled,
        // and arrowing a DropDownList never raises DropDown: brought up to date when focus
        // arrives, but only when they have just been found (never refilled on every focus).
        cmbStepName.Enter += (s, e) => EnsureHoldingPointsListed();

        // "At", for a runway crossed or held short of only: where ATC said (RefreshStepAtList, which
        // sets the description for the kind). No access key: Tab reaches it from the name box.
        lblStepAt = new Label
        {
            Text = "At (optional):",
            AutoSize = true,
            Visible = false,
            AccessibleName = "At label"
        };
        cmbStepAt = new ComboBox
        {
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "At taxiway or holding point, optional",
            AccessibleDescription = ClearanceStepEntry.AtBoxDescription(ClearanceStepKind.Cross)
        };
        cmbStepAt.KeyDown += OnStepBoxKeyDown;
        // Its holding points come from the same online fetch as the name box's, which can land after the list was
        // filled. Refreshed (keeping the pick) just before the list opens, and when focus arrives if the points have
        // just been found: arrowing a DropDownList never raises DropDown, and a refill on every focus would lose the
        // pilot's place.
        cmbStepAt.DropDown += (s, e) => RefreshStepAtList(keepSelection: true);
        cmbStepAt.Enter += (s, e) =>
        {
            if (_graph == null || _namedHoldingPointsResolved) return;
            ResolveNamedHoldingPoints();
            if (_namedHoldingPointsResolved) RefreshStepAtList(keepSelection: true);
        };

        btnAddStep = new Button
        {
            Text = "A&dd Step",
            Width = 140,
            Height = 28,
            AccessibleName = "Add Step",
            AccessibleDescription = "Add the step to the end of the clearance. Enter in the step kind, name or At box does the same."
        };
        // While a step is being changed the button reads "Replace step N" (BeginStepChange); these put it back.
        _addStepText = btnAddStep.Text;
        _addStepName = btnAddStep.AccessibleName ?? "";
        _addStepDescription = btnAddStep.AccessibleDescription ?? "";
        btnAddStep.Click += (s, e) => AddStepFromInputs();

        lblClearance = new Label
        {
            Text = "Clearance (&U):",
            AutoSize = true,
            AccessibleName = "Clearance label"
        };
        lstClearance = new ListBox
        {
            Width = controlWidth,
            Height = 84,
            IntegralHeight = false,
            AccessibleName = "Clearance",
            AccessibleDescription = "The steps of the clearance, in the order ATC said them. Enter changes the selected step in place; Delete removes it."
        };
        lstClearance.KeyDown += OnClearanceListKeyDown;

        btnRemoveStep = new Button
        {
            Text = "Re&move Step",
            Width = 140,
            Height = 28,
            AccessibleName = "Remove Step",
            AccessibleDescription = "Remove the step selected in the clearance list, or the last step when none is selected."
        };
        btnRemoveStep.Click += (s, e) => RemoveStep();

        btnClearSteps = new Button
        {
            Text = "Clear Steps (&Z)",
            Width = 140,
            Height = 28,
            AccessibleName = "Clear Steps",
            AccessibleDescription = "Remove every step from the clearance."
        };
        btnClearSteps.Click += (s, e) => OnClearStepsClicked();

        // The panel that holds the step block, where the taxiway rows used to be. Its height
        // follows the "At" line (UpdateLayout, LayoutStepBlock).
        pnlSteps = new Panel
        {
            Location = new System.Drawing.Point(controlX, y),
            Width = controlWidth,
            Height = 0,
            AutoSize = false
        };
        // y will be adjusted dynamically

        // SayIntentions import. Sits immediately above Calculate because it is the step
        // BEFORE calculating: it fills this form in from the clearance ATC just gave, and
        // the pilot then reviews it and presses Calculate.
        //
        // Disabled rather than hidden when no import callback was supplied (see
        // _importFromSayIntentions).
        btnSayIntentions = new Button
        {
            Text = "Fill from Sa&yIntentions",
            Location = new System.Drawing.Point(controlX, y),
            Width = 180,
            Height = 30,
            Enabled = _importFromSayIntentions != null,
            AccessibleName = "Fill from SayIntentions",
            AccessibleDescription = _importFromSayIntentions != null
                ? "Fill this form from the latest SayIntentions taxi clearance. Same as Ctrl+Shift+Y."
                : "Fill this form from the latest SayIntentions taxi clearance. Unavailable in this window."
        };
        btnSayIntentions.Click += OnSayIntentionsClicked;
        y += 35;

        // Calculate button
        btnCalculate = new Button
        {
            Text = "&Calculate Route",
            Location = new System.Drawing.Point(controlX, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Calculate Route",
            AccessibleDescription = "Calculate the taxi route and start guidance"
        };
        btnCalculate.Click += OnCalculateClicked;

        btnStop = new Button
        {
            Text = "&Stop Guidance",
            Location = new System.Drawing.Point(controlX + 190, y),
            Width = 180,
            Height = 30,
            AccessibleName = "Stop Guidance",
            AccessibleDescription = "Stop the active taxi guidance"
        };
        btnStop.Click += OnStopClicked;
        y += 40;

        // Status label
        lblStatus = new Label
        {
            Text = "",
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 20,
            AccessibleName = "Status"
        };
        y += 25;

        // Route summary read-only TextBox. Shows the same text the announcer
        // speaks when Calculate succeeds (e.g. "Taxi to runway 28L via A,
        // B, K. Total distance 1.2 miles."). Useful for two reasons:
        //   1. Screen readers often interrupt the spoken summary with their
        //      own UI announcement (especially after the form closes), so
        //      the text-form is the only reliable record.
        //   2. The shortest-path calculate path is the same way — the user
        //      can verify what the router actually produced when they
        //      didn't pick taxiways.
        // Multi-line + ReadOnly = TabStop on but not editable; the user can
        // arrow-read with the screen reader. Populated by OnCalculateClicked
        // from TaxiGuidanceManager.LastRouteSummary.
        lblRouteSummary = new Label
        {
            // Alt+N, never S: "&Stop Guidance" holds S, and a shared key takes the first match after
            // the focused control, so reaching for the summary stopped guidance.
            Text = "Last route summary (&N):",
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 20,
            AccessibleName = "Last route summary label"
        };
        y += 22;
        txtRouteSummary = new TextBox
        {
            Location = new System.Drawing.Point(labelX, y),
            Width = controlWidth,
            Height = 70,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            TabStop = true,
            AccessibleName = "Last route summary",
            AccessibleDescription = "Read-only display of the most recent route summary, including the shortest-path result. Use arrow keys to navigate the text with your screen reader."
        };

        // Add controls. Tab order and access keys come from the explicit TabIndex numbers
        // set below, not from the order added.
        this.Controls.Add(lblAirport);
        this.Controls.Add(txtAirport);
        this.Controls.Add(lblDestType);
        this.Controls.Add(cmbDestType);
        this.Controls.Add(lblDestination);
        this.Controls.Add(chkFitFilter);
        this.Controls.Add(chkHideOccupied);
        this.Controls.Add(lblGateSearch);
        this.Controls.Add(txtGateSearch);
        this.Controls.Add(cmbDestination);
        this.Controls.Add(lblRunwayEntry);
        this.Controls.Add(cmbRunwayEntry);
        this.Controls.Add(chkCatIiiHold);
        this.Controls.Add(chkFullLengthBacktrack);
        pnlSteps.Controls.Add(lblStepKind);
        pnlSteps.Controls.Add(cmbStepKind);
        pnlSteps.Controls.Add(lblStepName);
        pnlSteps.Controls.Add(cmbStepName);
        pnlSteps.Controls.Add(lblStepAt);
        pnlSteps.Controls.Add(cmbStepAt);
        pnlSteps.Controls.Add(btnAddStep);
        pnlSteps.Controls.Add(lblClearance);
        pnlSteps.Controls.Add(lstClearance);
        pnlSteps.Controls.Add(btnRemoveStep);
        pnlSteps.Controls.Add(btnClearSteps);
        this.Controls.Add(pnlSteps);
        this.Controls.Add(btnSayIntentions);
        this.Controls.Add(btnCalculate);
        this.Controls.Add(btnStop);
        this.Controls.Add(lblStatus);
        this.Controls.Add(lblRouteSummary);
        this.Controls.Add(txtRouteSummary);

        // Tab order, top to bottom. EVERY label is numbered here, immediately before its
        // control: a label's access key moves focus to the control after it in this order, and
        // a label left to Controls.Add is numbered automatically ("highest + 1" at the time),
        // which can land its key on some other control. The step block's panel takes its
        // place in the order; the numbers inside it continue the same sequence, which keeps
        // their order within the panel. A hidden control (the "At" box, the gate search) is
        // simply skipped.
        int tabIdx = 0;
        lblAirport.TabIndex = tabIdx++;
        txtAirport.TabIndex = tabIdx++;
        lblDestType.TabIndex = tabIdx++;
        cmbDestType.TabIndex = tabIdx++;
        lblGateSearch.TabIndex = tabIdx++;
        txtGateSearch.TabIndex = tabIdx++;
        lblDestination.TabIndex = tabIdx++;
        cmbDestination.TabIndex = tabIdx++;
        lblRunwayEntry.TabIndex = tabIdx++;
        cmbRunwayEntry.TabIndex = tabIdx++;
        chkCatIiiHold.TabIndex = tabIdx++;
        chkFullLengthBacktrack.TabIndex = tabIdx++;
        chkFitFilter.TabIndex = tabIdx++;
        chkHideOccupied.TabIndex = tabIdx++;
        pnlSteps.TabStop = true;
        pnlSteps.TabIndex = tabIdx++;
        lblStepKind.TabIndex = tabIdx++;
        cmbStepKind.TabIndex = tabIdx++;
        lblStepName.TabIndex = tabIdx++;
        cmbStepName.TabIndex = tabIdx++;
        lblStepAt.TabIndex = tabIdx++;
        cmbStepAt.TabIndex = tabIdx++;
        btnAddStep.TabIndex = tabIdx++;
        lblClearance.TabIndex = tabIdx++;
        lstClearance.TabIndex = tabIdx++;
        btnRemoveStep.TabIndex = tabIdx++;
        btnClearSteps.TabIndex = tabIdx++;
        btnSayIntentions.TabIndex = tabIdx++;
        btnCalculate.TabIndex = tabIdx++;
        btnStop.TabIndex = tabIdx++;
        lblStatus.TabIndex = tabIdx++;
        lblRouteSummary.TabIndex = tabIdx++;
        txtRouteSummary.TabIndex = tabIdx++;

        // Load handler for focus
        this.Load += (s, e) =>
        {
            this.BringToFront();
            this.Activate();
            txtAirport.Focus();
        };

        // The name box's label and list for the starting kind (empty until an airport loads).
        RefreshStepNameList();

        // Fit the form to its content NOW. UpdateLayout otherwise only runs when the
        // destination type or the "At" line changes, so at first open the initial 480 px
        // Size clipped everything below its client area.
        UpdateLayout();
    }

    /// <summary>The Alt+letter step kinds (Alt+V, Alt+L, Alt+R, Alt+X, Alt+H): pick the kind and put focus
    /// in the name box, from anywhere in the window. Handled here, on the character message after Windows
    /// handled the key press, like every access key: from ProcessCmdKey (the key-down message, consumed before
    /// Windows saw it) the screen reader said nothing after the focus move, while Alt+W to the same box was read
    /// (owner, 2026-11-28). The form is asked before its controls, so no control's access key takes these
    /// letters. Alt must be held, without Ctrl: this also runs for a plain letter typed while a button has focus,
    /// and AltGr is Ctrl+Alt on many keyboard layouts, so a letter typed with AltGr is never a step kind. An Alt
    /// code typed on the number pad makes no letter here, so the Airport box keeps it.</summary>
    protected override bool ProcessMnemonic(char charCode)
    {
        if ((ModifierKeys & (Keys.Alt | Keys.Control)) == Keys.Alt
            && ClearanceStepEntry.KindForMnemonic(charCode) is ClearanceStepKind kind)
        {
            SelectStepKind(kind, focusName: true);
            return true;
        }
        return base.ProcessMnemonic(charCode);
    }

    /// <summary>Escape: with a step being changed (Enter on it in the Clearance list), keeps the step as it was and goes
    /// back to it there (<see cref="CancelStepChange"/>); with none, closes the window, as it always has. The form sees
    /// every key first (KeyPreview), so no control takes the Escape before this. In an open drop-down list Escape only
    /// closes the list, as Enter there takes the highlighted item (<see cref="OnStepBoxKeyDown"/>): it is left to the
    /// list, so a change stays open and the window with it.</summary>
    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Escape) return;
        Control? focused = ActiveControl;
        while (focused is ContainerControl { ActiveControl: { } inner }) focused = inner;
        if (focused is ComboBox { DroppedDown: true }) return;
        if (_changingStepIndex >= 0)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelStepChange(true);
            return;
        }
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _guidanceManager.StateChanged -= OnGuidanceStateChanged;
            _guidanceManager.ClearedCrossingsChanged -= OnClearedCrossingsChanged;
            _stepSpeechTimer?.Dispose();
            _followTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    // Non-handler async void (called from the airport textbox's Leave lambda and from
    // the nearest-airport auto-load) — wrapped end-to-end so a DB/graph-build fault
    // can't escape as an unobserved async-void exception; only the augmentation
    // prefetch and the graph build had their own local guards before this.
    /// <summary>Outcome of applying an externally-sourced (SayIntentions) route.
    /// Skipped taxiways AND hold-shorts are reported so the caller can tell the pilot
    /// which parts of the clearance did not survive — silently degrading to a
    /// shortest-path route, or dropping a hold-short ATC gave, is the failure a blind
    /// pilot cannot see.</summary>
    public sealed record ExternalRouteOutcome(
        bool DestinationApplied,
        IReadOnlyList<string> AppliedTaxiways,
        IReadOnlyList<string> SkippedTaxiways,
        IReadOnlyList<AppliedHoldShort> AppliedHoldShorts,
        IReadOnlyList<string> SkippedHoldShortRunways);

    /// <summary>A hold-short that became a step: the runway designator spelled the way
    /// the airport's runway list (and therefore the router) spells it, and the taxiway
    /// whose Via step it follows.</summary>
    public sealed record AppliedHoldShort(string Runway, string AfterTaxiway);

    /// <summary>A hold-short from the clearance, tied to the taxiway it FOLLOWS.
    /// TaxiwayIndex indexes the taxiway list passed to ApplyExternalRoute; -1 means
    /// the clearance named no taxiway ahead of it, so no Via step can carry it.</summary>
    public readonly record struct ExternalHoldShort(int TaxiwayIndex, string Runway);

    /// <summary>One destination an external clearance would accept. Callers pass the
    /// whole list in priority order so the form lists each destination type once.
    ///
    /// <paramref name="Position"/> is where the source says that destination IS, for the
    /// gate candidates that have one. It is a FALLBACK for the name and nothing else: it
    /// is consulted only after <see cref="MatchDestinationLabel"/> has failed on this
    /// same candidate, because the name is what the pilot was actually told.</summary>
    public readonly record struct ExternalDestination(
        bool IsRunway, string? Identifier, GeoPoint? Position = null);

    /// <summary>Which step seated a stand the source named something else. The pilot
    /// hears a different sentence for each, because they are different claims about the
    /// airport: an ALIAS says this scenery holds the stand under another label, a
    /// POSITION says no label matched at all and the published coordinate decided.</summary>
    public enum GateSubstitutionKind
    {
        Alias,
        Position
    }

    /// <summary>A destination seated under a label the source did not use.
    /// <paramref name="AssignedName"/> is the name SayIntentions ASKED for. The import's
    /// lead sentence names only the label that WON, so this is the one thing that can
    /// tell the pilot they are being taxied to a stand the controller did not name.</summary>
    public readonly record struct GateSubstitution(
        string AssignedName, GateSubstitutionKind Kind);

    /// <summary>One gate entry as the alias step sees it: the label the combo carries,
    /// and the online names the spot behind it also answers to.</summary>
    internal readonly record struct AliasedDestination(
        string Label, IReadOnlyList<string> Aliases);

    /// <summary>Loads the airport for an external route and returns the taxiway
    /// names its graph knows. The caller resolves its clearance against THIS list
    /// so no second TaxiGraph is ever built.</summary>
    public async Task<IReadOnlyList<string>> LoadAirportForExternalRouteAsync(
        double lat, double lon, double heading, string icao)
    {
        _aircraftLat = lat;
        _aircraftLon = lon;
        _aircraftHeading = heading;
        txtAirport.Text = icao.ToUpperInvariant();
        await LoadAirportDataAsync(icao);
        // Same-ICAO reload early-returns, so an import landing after GSX published
        // this airport (the descent pre-plan case) would otherwise resolve its
        // destination against the identifier-less fallback list and only discover
        // that at Calculate time. Refresh here, before TryResolveExternalDestination
        // reads the combo; the lost-selection result is irrelevant — the import
        // seats its own destination next.
        try { RefreshDestinationsIfGateSourceChanged(); }
        catch (Exception ex) { _taxiFormLog.Error($"Gate-list refresh before import failed: {ex}"); }
        return _graph?.GetAllTaxiwayNames() ?? new List<string>();
    }

    /// <summary>Every named taxiway SEGMENT of the airport this form currently has
    /// loaded, for a caller matching published geometry against the pavement. Exposed
    /// for the same reason the taxiway names are: the SayIntentions import must never
    /// build a second TaxiGraph, and this graph is the one the route will be calculated
    /// on. Empty before an airport is loaded.</summary>
    public IReadOnlyList<(string Name, double FromLat, double FromLon, double ToLat, double ToLon)>
        GetLoadedTaxiwayEdges() =>
        _graph == null
            ? Array.Empty<(string, double, double, double, double)>()
            : _graph.GetNamedEdges().ToList();

    /// <summary>Selects the first candidate that names a real entry in the form's
    /// destination list. The form owns its label formats — callers pass a normalized
    /// identifier ("15L", "A9"), never a constructed "Runway 15L".
    ///
    /// PROBING LEAVES NO MARK. Listing a destination type re-reads the database and
    /// re-walks the taxi graph, so each type is listed at most ONCE however many
    /// candidates ask for it; and when nothing resolves, the form is put back the way
    /// it was found — a failed import must not throw away the destination the pilot
    /// had already selected.
    ///
    /// "The way it was found" includes the ROUTE-SHAPING controls, not just the destination.
    /// Probing a gate candidate sets the destination type, and OnDestTypeChanged puts the
    /// Runway entry list back at Full length and unticks the CAT III / LVP box on the way
    /// out of runway mode. Restoring the type re-SHOWS them, reset. So a pilot who
    /// hand-built "Runway 27L, intersection T4, CAT III hold", pressed Ctrl+Shift+Y and
    /// heard "SayIntentions route unavailable" — i.e. "nothing happened" — silently lost
    /// both, and their next Calculate lined them up at the full-length threshold holding
    /// at the CAT I line. This is the mirror image of what ResetRouteShapingControls
    /// exists to prevent on a SUCCESSFUL import.
    ///
    /// A GATE CANDIDATE IS RESOLVED IN THREE STEPS: its name, then this scenery's online
    /// ALIASES for that name, then the coordinate SayIntentions published beside it. Each
    /// is weaker evidence than the one before — an alias is still the same stand under
    /// another label, a coordinate is a guess at which pavement an unrecognized name must
    /// have meant — so each only runs when the one above it found nothing.
    ///
    /// <paramref name="gateSubstitution"/> names the gate a candidate ASKED for when one
    /// of those two fallbacks seated it, and says which — non-null only then, so the
    /// caller can tell the pilot they are being routed to a stand under a label the
    /// controller did not use.</summary>
    public bool TryResolveExternalDestination(
        IReadOnlyList<ExternalDestination> candidates, out bool isRunway, out string label,
        out GateSubstitution? gateSubstitution, List<string>? probeTrace = null)
    {
        isRunway = false;
        label = "";
        gateSubstitution = null;

        int priorType = cmbDestType.SelectedIndex;
        string priorSearch = txtGateSearch.Text;
        string? priorDestination = cmbDestination.SelectedItem?.ToString();
        RunwayEntryItem? priorRunwayEntry = SelectedRunwayEntry;
        bool priorCatIiiHold = chkCatIiiHold.Checked;

        // A gate search left over from a manual lookup filters the gate list, and a
        // filtered-out gate reads exactly like "this airport has no such gate".
        if (txtGateSearch.Text.Length > 0) txtGateSearch.Text = "";

        // Same reasoning for the occupied-stands filter (see _suppressOccupiedFilter):
        // an assigned stand with AI parked on it must still be resolvable. And for the
        // wingspan filter (see _suppressFitFilter), which is ticked by DEFAULT whenever
        // wingspan data exists and drops stands on scenery size data that is often
        // wrong: a stand ATC named must still be resolvable too. The explicit
        // rebuild is needed because SelectDestinationType only repopulates when the type
        // actually CHANGES — already in gate mode, the stale filtered list would stand.
        bool priorSuppressOccupied = _suppressOccupiedFilter;
        bool priorSuppressFit = _suppressFitFilter;
        _suppressOccupiedFilter = true;
        _suppressFitFilter = true;
        if (cmbDestType.SelectedIndex == 1) PopulateDestinations();

        List<string>? runwayLabels = null;
        List<string>? gateLabels = null;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Identifier)) continue;

            // sayintentions.log's answer to "why did THIS import end where it did":
            // one token per probed candidate, saying which step answered or how the
            // last one missed. The KSTL/KLAX arrival captures could not distinguish
            // "the runway candidate won first" from "every gate step failed" — four
            // presses, two airports, and the log recorded only the winner.
            string probe = $"{(candidate.IsRunway ? "runway" : "gate")}'{candidate.Identifier}'";

            var offered = candidate.IsRunway
                ? (runwayLabels ??= ListDestinations(true))
                : (gateLabels ??= ListDestinations(false));

            string? match = MatchDestinationLabel(offered, candidate.IsRunway, candidate.Identifier);

            // BOTH GATE FALLBACKS RUN HERE, ON THIS CANDIDATE, AND MUST NEVER BE MOVED
            // BELOW THE LOOP. The chain ends with the ARRIVAL RUNWAY — that is the whole
            // reason they exist: a gate name the scenery does not carry fell through every
            // remaining candidate and routed a just-landed aircraft at the runway it had
            // landed on. A fallback placed after the loop would let the runway win first
            // and reproduce exactly that.
            GateSubstitution? substitution = null;
            if (match == null && !candidate.IsRunway)
            {
                // _destinationThresholdMap / _destinationSpotMap only hold GATE entries
                // while gate mode is the selected type, and a runway candidate probed
                // between here and the gate listing has repopulated both with runway
                // entries. The list itself was snapshotted; the maps behind it were not.
                // Selected ONCE for both steps below — they read the same two maps.
                SelectDestinationType(false);

                match = MatchGateByAlias(offered, candidate.Identifier);
                if (match != null)
                {
                    substitution = new GateSubstitution(
                        candidate.Identifier!, GateSubstitutionKind.Alias);
                }
                else if (candidate.Position is GeoPoint published)
                {
                    match = MatchGateByPosition(offered, published, out double nearestMetres);
                    if (match != null)
                    {
                        substitution = new GateSubstitution(
                            candidate.Identifier!, GateSubstitutionKind.Position);
                    }
                    else
                    {
                        // The one distinction the KLAX capture could not make: was the
                        // published coordinate near-but-outside a stand (scenery offset
                        // — the tolerance is the thing to re-examine) or nowhere near
                        // anything (the coordinate itself is unusable)?
                        probeTrace?.Add(double.IsNaN(nearestMetres)
                            ? $"{probe}=miss(position matched no stand)"
                            : $"{probe}=miss(nearest stand {nearestMetres:F0}m from position)");
                        continue;
                    }
                }
                else
                {
                    probeTrace?.Add($"{probe}=miss(no-position)");
                    continue;
                }
            }

            if (match == null)
            {
                probeTrace?.Add($"{probe}=miss");
                continue;
            }

            // The list for this type may have been snapshotted several candidates
            // ago, so switch back to it before selecting.
            SelectDestinationType(candidate.IsRunway);
            int index = cmbDestination.Items.IndexOf(match);
            if (index < 0)
            {
                probeTrace?.Add($"{probe}=miss(matched '{match}' but the combo no longer lists it)");
                continue;
            }
            // The step is DERIVED from substitution rather than tracked beside it, so
            // destProbe and the gateSubstitution field on the same log line can never
            // disagree about which step seated the stand.
            probeTrace?.Add($"{probe}={(substitution is not GateSubstitution seated ? "name"
                : seated.Kind == GateSubstitutionKind.Alias ? "alias" : "position")}");

            // Silently: this is the import seating its own destination, not the pilot
            // choosing one, and the import's own summary must not be talked over.
            SelectDestinationSilently(index);
            isRunway = candidate.IsRunway;
            label = match;
            gateSubstitution = substitution;
            return true;
        }

        // Nothing was seated, so nothing needs a stand kept visible that the pilot's own
        // filters would hide: put both filters back (and rebuild under them) before
        // restoring the rest. Probing leaves no mark.
        _suppressOccupiedFilter = priorSuppressOccupied;
        _suppressFitFilter = priorSuppressFit;
        if (cmbDestType.SelectedIndex == 1) PopulateDestinations();

        RestoreDestinationState(
            priorType, priorSearch, priorDestination,
            priorRunwayEntry, priorCatIiiHold);
        return false;
    }

    /// <summary>The destination label matching a normalized identifier, or null.
    /// BOTH sides are normalized, and zero-padding is a difference in spelling on
    /// either of them: the clearance zero-pads a runway ("05L") where the combo
    /// carries whatever navdata spells ("5L"), and it zero-pads a stand the same way
    /// ("B06" against EDDB's "B 6"). A gate label also carries a terminal descriptor
    /// the clearance never says.</summary>
    internal static string? MatchDestinationLabel(
        IReadOnlyList<string> offered, bool isRunway, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        string wanted = isRunway
            ? SayIntentionsClearanceParser.CleanRunway(identifier) ?? ""
            : SayIntentionsClearanceParser.NormalizeParkingName(identifier);
        if (wanted.Length == 0) return null;

        foreach (string text in offered)
        {
            string candidate = isRunway
                ? SayIntentionsClearanceParser.CleanRunway(text) ?? ""
                : SayIntentionsClearanceParser.NormalizeParkingName(text);

            if (candidate.Length > 0 && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return null;
    }

    /// <summary>The gate label whose ONLINE ALIASES answer to a published gate name, or
    /// null. Runs after the name failed on this candidate and before its coordinate is
    /// consulted.
    ///
    /// It exists because the alias is INVISIBLE to MatchDestinationLabel, not because the
    /// alias was missing. The combo carries ParkingSpot.ToString() — at KDTW,
    /// "A 24A - Gate Medium, also A24 (online)" — and NormalizeParkingName deletes
    /// everything from the first spaced dash onward, which every Describe() branch puts
    /// ahead of the alias (" - {type}"). So on the live 2026-07-31 KDTW arrival, where
    /// the controller, SayIntentions and OSM all said A24 and only the scenery said A24A,
    /// the assigned gate could not resolve by name at all and destination resolution ran
    /// its whole chain to the ARRIVAL RUNWAY — with the taxiway half of the import
    /// (A5, A, R, hold short of 4R) perfectly correct, so nothing else sounded wrong. The
    /// form's own gate search finds that stand: GateSearchFilter reads ParkingSpot.Aliases
    /// directly. The data was there all along; only this resolver could not see it.</summary>
    private string? MatchGateByAlias(IReadOnlyList<string> gateLabels, string? identifier)
    {
        var offered = new List<AliasedDestination>(gateLabels.Count);
        foreach (string gateLabel in gateLabels)
        {
            if (!_destinationSpotMap.TryGetValue(gateLabel, out var spot)) continue;
            if (spot.Aliases.Count == 0) continue;
            offered.Add(new AliasedDestination(gateLabel, spot.Aliases));
        }

        return MatchDestinationAlias(offered, identifier);
    }

    /// <summary>The offered label one of whose aliases IS the identifier, or null. Both
    /// sides go through NormalizeParkingName, exactly as the name match does, so a full
    /// label ("South Terminal Gate A24") meets a bare alias ("A24") and zero-padding is
    /// still a spelling rather than an identity.
    ///
    /// EXACT normalized comparison, never a substring test. A stand id is one or two
    /// characters, so Contains would match almost any entry the combo offers, and "A2"
    /// must never seat the stand aliased A24 — the wrong-stand failure the padding rules
    /// already exist to prevent, pointed a different way.</summary>
    internal static string? MatchDestinationAlias(
        IReadOnlyList<AliasedDestination> offered, string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return null;

        string wanted = SayIntentionsClearanceParser.NormalizeParkingName(identifier);
        if (wanted.Length == 0) return null;

        foreach (var entry in offered)
        {
            foreach (string alias in entry.Aliases)
            {
                string candidate = SayIntentionsClearanceParser.NormalizeParkingName(alias);
                if (candidate.Length > 0
                    && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Label;
                }
            }
        }

        return null;
    }

    /// <summary>The gate label whose own radius reaches a published gate coordinate, or
    /// null. Called only after the NAME and its aliases failed on the same candidate.
    ///
    /// The unit conversion is the point of this method existing at all: ParkingSpot.Radius
    /// is FEET on a navdata spot and METRES on a GSX-sourced one, and the matcher takes
    /// metres so nothing downstream has to remember which it was holding. Feeding raw
    /// feet in is the same mistake ParkingSpot.FitsAircraft records — a navdata stand's
    /// 71 would read as 71 m, a circle three times too big, and a point on the neighbour
    /// would match.
    ///
    /// A gate the maps do not carry is skipped rather than defaulted to (0, 0): a spot
    /// with no known centre cannot be shown to contain anything, and null island is
    /// 150 m from nothing.</summary>
    private string? MatchGateByPosition(
        IReadOnlyList<string> gateLabels, GeoPoint published, out double nearestMetres)
    {
        const double FeetToMetres = 0.3048;

        var candidates = new List<GatePositionCandidate>(gateLabels.Count);
        foreach (string gateLabel in gateLabels)
        {
            if (!_destinationThresholdMap.TryGetValue(gateLabel, out var centre)) continue;
            if (!_destinationSpotMap.TryGetValue(gateLabel, out var spot)) continue;

            double radiusMetres = spot.Source == GateSource.Gsx
                ? spot.Radius
                : spot.Radius * FeetToMetres;

            candidates.Add(new GatePositionCandidate(
                gateLabel, centre.lat, centre.lon, radiusMetres));
        }

        return SayIntentionsGatePositionMatcher.Match(
            candidates, published.Latitude, published.Longitude, out nearestMetres);
    }

    /// <summary>Every label the destination combo offers for one destination type.
    /// Switching type repopulates the list, so callers cache the result per type.</summary>
    private List<string> ListDestinations(bool isRunway)
    {
        SelectDestinationType(isRunway);
        return ComboItemTexts(cmbDestination);
    }

    /// <summary>Switches the destination type WITHOUT speaking the holding-point
    /// call-out. Every caller is an external-route path (the SayIntentions import and
    /// its probing), where the switch is not a pilot choice: OnDestTypeChanged fires
    /// AnnounceDefaultHoldingPoint for runway mode, so probing a runway candidate the
    /// pilot never picked would name its holding point out loud, rewrite the status
    /// line and burn the call-out's dedup key — leaving the real call-out silent when
    /// they later select that runway themselves, and breaking the invariant that
    /// probing leaves no mark. The import speaks its own single summary instead.</summary>
    private void SelectDestinationType(bool isRunway)
    {
        int wanted = isRunway ? 0 : 1;
        if (cmbDestType.SelectedIndex == wanted) return;
        bool prior = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try { cmbDestType.SelectedIndex = wanted; }
        finally { _suppressHoldingPointAnnounce = prior; }
    }

    private void RestoreDestinationState(
        int priorType, string priorSearch, string? priorDestination,
        RunwayEntryItem? priorRunwayEntry, bool priorCatIiiHold)
    {
        // Silent throughout: a restore is the probe undoing itself, so it must not
        // speak — nor burn the holding-point dedup key, which would swallow the real
        // call-out when the pilot next selects that runway ("probing leaves no mark").
        bool priorSuppressAnnounce = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        // Place mode speaks two lines of its own — "Loading places for {icao}." and the
        // no-places line — neither of which belongs to a restore that performs no action.
        bool priorSuppressPlace = _suppressPlaceAnnounce;
        _suppressPlaceAnnounce = true;
        try
        {
            // Restoring Place mode may start a load that fills the list later; hand it the label.
            if (priorType == 4)
                _placeToReselect = priorDestination;

            // Type first: leaving gate mode blanks the gate search, which would undo the
            // search restore if it ran the other way round.
            if (priorType >= 0 && cmbDestType.SelectedIndex != priorType)
                cmbDestType.SelectedIndex = priorType;
            if (txtGateSearch.Text != priorSearch)
                txtGateSearch.Text = priorSearch;

            if (!string.IsNullOrEmpty(priorDestination))
            {
                int index = cmbDestination.Items.IndexOf(priorDestination);
                if (index >= 0) cmbDestination.SelectedIndex = index;
            }

            // With no load in flight the lookup above was final.
            if (!_placesLoading) _placeToReselect = null;

            // Both are runway-only, and the Runway entry list is rebuilt against whichever
            // runway is selected — so this has to run AFTER the destination is back, or the
            // departure is restored onto the wrong runway's entries. Inside the suppression
            // too: restoring the LVP tick raises CheckedChanged, which speaks the CAT III
            // call-out.
            RestoreRunwayEntryState(priorRunwayEntry);
            if (chkCatIiiHold.Checked != priorCatIiiHold) chkCatIiiHold.Checked = priorCatIiiHold;
        }
        finally
        {
            _suppressHoldingPointAnnounce = priorSuppressAnnounce;
            _suppressPlaceAnnounce = priorSuppressPlace;
        }
    }

    /// <summary>Puts the Runway entry choice back after a failed destination probe: an
    /// intersection or a holding point (D15), set DIRECTLY on the rebuilt list, so nothing
    /// moves focus or speaks — the pilot performed no action here, and the announcement rule
    /// [CORE-7] reserves speech for real state changes. Never through the refill that keeps
    /// the choice, which says when the choice is gone.</summary>
    private void RestoreRunwayEntryState(RunwayEntryItem? prior)
    {
        PopulateRunwayEntries(keepSelection: false);
        int index = RestoredRunwayEntryIndex(_runwayEntries, prior);
        if (index >= 0 && cmbRunwayEntry.SelectedIndex != index) cmbRunwayEntry.SelectedIndex = index;
    }

    /// <summary>Which runway entry a restore selects: the one chosen before the probe
    /// (<see cref="RunwayEntryList.IndexToKeep"/>), Full length (item 0) when it is no longer
    /// offered (the probe can leave a different runway selected), and -1 for an empty list.</summary>
    internal static int RestoredRunwayEntryIndex(IReadOnlyList<RunwayEntryItem> offered, RunwayEntryItem? prior)
    {
        if (offered.Count == 0) return -1;
        int kept = RunwayEntryList.IndexToKeep(offered, prior);
        return kept >= 0 ? kept : 0;
    }

    private static List<string> ComboItemTexts(ComboBox combo)
    {
        var texts = new List<string>(combo.Items.Count);
        foreach (object? item in combo.Items)
            texts.Add(item?.ToString() ?? "");
        return texts;
    }

    /// <summary>Index of the entry naming this runway in a list of the airport's runways, or -1.
    /// BOTH sides go through CleanRunway: a clearance always zero-pads ("05L") while
    /// the list carries the raw navdata designator, which need not ("5L"). A literal
    /// match silently drops the hold-short ATC just gave.</summary>
    internal static int FindRunwayItemIndex(IReadOnlyList<string> items, string? runway)
    {
        string? wanted = SayIntentionsClearanceParser.CleanRunway(runway);
        if (string.IsNullOrEmpty(wanted)) return -1;

        for (int i = 0; i < items.Count; i++)
        {
            // The "(none)" sentinel carries no runway number and can never match.
            string? candidate = SayIntentionsClearanceParser.CleanRunway(items[i]);
            if (!string.IsNullOrEmpty(candidate)
                && candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>Fills the route fields from an external clearance: the destination, by
    /// EXACT combo entry (never a substring match, which for a one-character name would
    /// hit almost any entry in the list), and the step list (<see cref="BuildImportedSteps"/>),
    /// added silently and without moving focus.
    ///
    /// An imported route starts from a CLEAN SLATE (ResetRouteShapingControls): a
    /// leftover runway entry, CAT III hold or step from a route the pilot
    /// built by hand would otherwise reshape the imported one with nothing said about it.
    /// Emptying the list also leaves it holding no calculated leg, so a leg end queued
    /// before the fill never empties the steps the fill wrote.
    ///
    /// Anything that could not be seated comes back in the outcome so the caller can
    /// say so out loud.
    ///
    /// This only FILLS the fields. Starting guidance is a separate call
    /// (<see cref="StartImportedRoute"/>) because the caller's summary is built from the
    /// outcome returned here, and that summary has to reach the pilot as part of the
    /// form's own single standstill utterance rather than as queued speech the first
    /// tactical callout discards.</summary>
    public ExternalRouteOutcome ApplyExternalRoute(
        bool isRunway, string destinationLabel, IReadOnlyList<string> taxiways,
        IReadOnlyList<ExternalHoldShort> holdShorts)
    {
        ResetRouteShapingControls();

        // Silent: the import is seating its own destination, and its single standstill
        // summary is what tells the pilot where they are going — a holding-point
        // call-out queued here would either be discarded by that utterance or talk
        // over it, and would burn the call-out's dedup key either way.
        SelectDestinationType(isRunway);
        int destIndex = cmbDestination.Items.IndexOf(destinationLabel);
        bool destinationApplied = destIndex >= 0;
        if (destinationApplied) SelectDestinationSilently(destIndex);

        var applied = new List<string>();
        var skipped = new List<string>();
        var appliedHoldShorts = new List<AppliedHoldShort>();
        var skippedHoldShorts = new List<string>();

        // An empty list is the shortest path, as "(None - calculate shortest path)" was.
        var knownTaxiways = _graph?.GetAllTaxiwayNames() ?? new List<string>();
        foreach (var step in BuildImportedSteps(taxiways, holdShorts, knownTaxiways, _airportRunwayIds,
                     applied, skipped, appliedHoldShorts, skippedHoldShorts))
            AppendStep(step, speak: false);

        return new ExternalRouteOutcome(
            destinationApplied, applied, skipped, appliedHoldShorts, skippedHoldShorts);
    }

    /// <summary>
    /// The steps a SayIntentions fill writes (spec 8.7), in the clearance's order: a Via step for each taxiway the
    /// airport's graph knows (exact name, as the taxiway lists match it); after it, a "Hold short of runway" step for
    /// each hold short the clearance hung on that taxiway, in the order given, as many as it gave. A runway is looked
    /// for among <paramref name="runwayIds"/> ALONE, never among taxiway or holding-point names ("A12" is not runway
    /// 12), and named as the airport spells it. Skipped, and reported for the summary: a taxiway the airport lacks
    /// with every hold short hung on it, a hold short hung on no taxiway (listed first: it precedes them all), and one
    /// whose runway the airport lacks. Never a Cross step (a crossing is cleared only by the pilot) and never a turn
    /// word (the clearance's are not read, as PR #355's import set every Turn list back to Automatic).
    /// </summary>
    internal static List<ClearanceStep> BuildImportedSteps(
        IReadOnlyList<string> taxiways, IReadOnlyList<ExternalHoldShort> holdShorts,
        IReadOnlyCollection<string> knownTaxiways, IReadOnlyList<string> runwayIds,
        List<string> appliedTaxiways, List<string> skippedTaxiways,
        List<AppliedHoldShort> appliedHoldShorts, List<string> skippedHoldShortRunways)
    {
        var steps = new List<ClearanceStep>();

        foreach (var holdShort in holdShorts)
        {
            if (holdShort.TaxiwayIndex < 0 || holdShort.TaxiwayIndex >= taxiways.Count)
                skippedHoldShortRunways.Add(holdShort.Runway);
        }

        for (int i = 0; i < taxiways.Count; i++)
        {
            bool known = knownTaxiways.Contains(taxiways[i], StringComparer.Ordinal);
            if (known)
            {
                steps.Add(new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, taxiways[i]));
                appliedTaxiways.Add(taxiways[i]);
            }
            else
            {
                skippedTaxiways.Add(taxiways[i]);
            }

            foreach (var holdShort in holdShorts)
            {
                if (holdShort.TaxiwayIndex != i) continue;

                int runwayIndex = known ? FindRunwayItemIndex(runwayIds, holdShort.Runway) : -1;
                if (runwayIndex < 0)
                {
                    skippedHoldShortRunways.Add(holdShort.Runway);
                    continue;
                }

                string runway = runwayIds[runwayIndex];
                steps.Add(new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, runway));
                appliedHoldShorts.Add(new AppliedHoldShort(runway, taxiways[i]));
            }
        }

        return steps;
    }

    /// <summary>The summary an in-progress import wants spoken, as a function of whether
    /// guidance actually started. Non-null only for the duration of the
    /// <see cref="OnCalculateClicked"/> call inside <see cref="StartImportedRoute"/>.</summary>
    private Func<bool, string>? _importSummary;

    /// <summary>Calculates and starts guidance for a route that
    /// <see cref="ApplyExternalRoute"/> has just filled in, speaking the caller's summary
    /// as part of the SAME utterance the form already makes at standstill.
    ///
    /// It cannot be spoken by the caller. Announce() queues, and the sequence here is
    /// Calculate → LoadRoute (queued router summary) → StartGuidance (first-taxiway
    /// AnnounceImmediate, which DISCARDS whatever is queued) → the standstill
    /// AnnounceImmediate. A queued import summary therefore reached the pilot only if
    /// nothing tactical happened first, which is exactly backwards for the safety lines it
    /// carries: "could not apply D, E", "could not set hold short of runway 22L", "the
    /// ground track differs from the clearance". This codebase has learned the same thing
    /// twice already — see the comments on the length advisory in
    /// TaxiGuidanceManager.Routing.cs and on the standstill block below.
    ///
    /// <paramref name="describe"/> takes whether guidance started, so a route that failed
    /// to calculate is never announced as "Guidance started."; it gets the "review the
    /// fields" tail instead, which is also the right advice after a failed Calculate.</summary>
    public void StartImportedRoute(Func<bool, string> describe)
    {
        _importSummary = describe;
        try { OnCalculateClicked(btnCalculate, EventArgs.Empty); }
        finally { _importSummary = null; }
    }

    /// <summary>Announces why Calculate stopped, carrying any pending import summary with
    /// it. Consecutive AnnounceImmediate calls stomp each other, so an abort that spoke
    /// only its own reason would swallow the import's "could not apply …" /
    /// "could not set hold short …" lines — the part a blind pilot cannot otherwise
    /// discover. Guidance did not start, so the summary gets its "review the fields"
    /// tail; the reason leads, because it is why there is no route at all.</summary>
    private void AnnounceCalculateAbort(string reason)
    {
        string? imported = _importSummary?.Invoke(false);
        _announcer.AnnounceImmediate(
            string.IsNullOrEmpty(imported) ? reason : reason + " " + imported);
    }

    /// <summary>Records why there is no route, in the two places a pilot can go looking:
    /// the status line and the route-summary box.
    ///
    /// SPEAKS NOTHING. Every caller has already announced the reason — this is the
    /// re-readable copy, and a second utterance would talk over the first (and break
    /// the screen-reader rule about announcing what the pilot has already been told).
    ///
    /// The summary box exists because speech is heard ONCE and a screen reader
    /// routinely interrupts it, so it is the only place a blind pilot can re-read what
    /// happened. That makes a STALE route sitting in it after a failed Calculate worse
    /// than an empty box: nothing distinguishes last route's summary from one that was
    /// actually just built. Every exit that leaves the pilot with no route comes
    /// through here, so the box holds either a real route or the reason there isn't
    /// one — never a route that was not built.
    ///
    /// <para><paramref name="keepSummary"/> is the exception PR #238's review found (Task 7
    /// Defect B): a reachability refusal from <c>TaxiGuidanceManager.LoadRoute</c> now rolls
    /// back and leaves the PREVIOUS route still live (see <c>RestoreLoadRouteRollback</c>),
    /// so the pilot IS still flying a route even though this Calculate attempt failed. Wiping
    /// the box in that case destroys the only re-readable record of that live route's
    /// clearance, hold-shorts and length advisory — worse than the staleness this method
    /// exists to prevent, because the route it would be discarding isn't stale, it's the one
    /// currently being flown. The caller decides this from the manager
    /// (<c>TaxiGuidanceManager.HasLiveRoute</c>), never by pattern-matching the error
    /// string — a guess here is exactly the kind of silent mismatch this whole box exists to
    /// rule out. Default <c>false</c> keeps every other caller's behaviour unchanged: the
    /// reason still replaces the box for every failure that leaves no route behind.
    ///
    /// <para>PR #238 review, Important 3: <c>HasLiveRoute</c> replaced a plain
    /// <c>CurrentRoute != null</c> check here -- <c>HandleArrival</c> sets the manager's
    /// state to <c>Arrived</c> without ever nulling its route, so <c>CurrentRoute</c> stayed
    /// non-null for the rest of the session after a completed, docking-off arrival with no
    /// Stop pressed, and a stale COMPLETED flight's summary wrongly survived the next leg's
    /// failed Calculate. <c>HasLiveRoute</c> reads the guidance STATE instead (see <see
    /// cref="MSFSBlindAssist.Services.LiveRouteStates"/>), which does not carry that
    /// staleness.</para>
    ///
    /// <para>PR #238 review, Important 4: every caller in this file is reachable while a
    /// PREVIOUS Calculate's route is still live in the manager -- this dialog is hide-on-close
    /// and cached, so a pilot can reopen it mid-taxi to plan the next leg and abort on ANY of
    /// this method's callers (a failed airport reload, a gate list that changed under them, no
    /// destination picked yet, an unreachable stand, or a refused <c>LoadRoute</c>) before ever
    /// building a new route. "Does this fire after a <c>LoadRoute</c> call" is not the right
    /// test — "is a route still live" is, and it is the same answer for every one of them, so
    /// all of this file's callers pass <c>keepSummary: _guidanceManager.HasLiveRoute</c>.</para>
    /// </summary>
    public void ShowRouteFailure(string reason, bool keepSummary = false)
    {
        if (string.IsNullOrWhiteSpace(reason)) return;
        lblStatus.Text = reason;
        if (!keepSummary)
            txtRouteSummary.Text = reason;
    }

    /// <summary>Clears the route-SHAPING controls an imported route must not inherit.
    /// OnDestTypeChanged only resets the runway-only controls when the type CHANGES, so a
    /// runway route imported over a hand-built runway route keeps the old runway entry
    /// (an intersection or a holding point) and CAT III hold — a different lineup point
    /// with nothing in the announcement to reveal it.</summary>
    private void ResetRouteShapingControls()
    {
        // Silent: this is the import clearing the pilot's leftovers, not a choice they
        // made, and unticking LVP raises CheckedChanged → the CAT III call-out, which
        // would speak over the import's own single summary.
        bool priorSuppressAnnounce = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try
        {
        // Full length (D15): a hand-picked intersection or holding point carried through a
        // runway-to-runway import would silently move the lineup point.
        SelectFullLengthRunwayEntry();
        if (chkCatIiiHold.Checked) chkCatIiiHold.Checked = false;

        // Repopulates the gate list on its way out, so it has to run BEFORE the
        // destination is selected.
        if (txtGateSearch.Text.Length > 0) txtGateSearch.Text = "";

        // Every step the pilot entered goes, turn words and hold shorts with it. Silent.
        ClearSteps();
        }
        finally { _suppressHoldingPointAnnounce = priorSuppressAnnounce; }

        // chkFitFilter is deliberately NOT reset: it describes the aircraft's
        // wingspan rather than the route, and forcing it either way could hide the
        // very gate the clearance names.
    }

    /// <summary>Fire-and-forget wrapper. LoadAirportData was `async void`, so a
    /// throw crashed the app; a bare discard would swallow it silently instead.
    /// Neither is acceptable — log it and tell the pilot.</summary>
    private async Task LoadAirportDataSafeAsync(string icao)
    {
        try
        {
            await LoadAirportDataAsync(icao);
        }
        catch (Exception ex)
        {
            _taxiFormLog.Error($"Airport load failed for '{icao}': {ex}");
            _announcer.Announce($"Could not load airport data for {icao}.");
        }
    }

    private static readonly LogChannel _taxiFormLog = Log.Channel("taxi_guidance");

    /// <summary>The airport load currently in flight, or null. Never awaited by more than
    /// one chained caller at a time; see <see cref="LoadAirportDataAsync"/>.</summary>
    private Task? _airportLoadInFlight;

    /// <summary>Loads an airport into the form's combos, SERIALIZED against any load
    /// already running.
    ///
    /// Every caller is on the UI thread, but this method awaits — the augmentation
    /// prefetch alone can wait seconds — and it CLEARS the step list, the name box
    /// and cmbDestination before those awaits and repopulates them after. Two loads
    /// interleaving at an await point therefore leave one of them repopulating combos the
    /// other has just emptied. The SayIntentions import is what makes that reachable and
    /// dangerous: it can run 9 s end to end (comms history, position, prefetch, graph
    /// build), and an import landing in that window resolved every leg against an empty
    /// combo — announcing "No taxiways from the clearance matched this airport. Using
    /// shortest path." for a perfectly good clearance, and starting guidance on it.
    ///
    /// Chained rather than dropped: a second load is usually a DIFFERENT airport (typed
    /// ICAO, aircraft moved), and dropping it would leave the form on the wrong one.
    /// Three callers reach here — SetAircraftPosition, the airport textbox's Leave
    /// handler, and LoadAirportForExternalRouteAsync.</summary>
    private async Task LoadAirportDataAsync(string icao)
    {
        Task? previous = _airportLoadInFlight;
        var mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _airportLoadInFlight = mine.Task;
        try
        {
            // The previous load reports its own failures; waiting on it is only about
            // ordering, so a fault there must not cancel this one.
            if (previous != null)
            {
                try { await previous.ConfigureAwait(true); } catch { /* not ours to report */ }
            }

            await LoadAirportDataCoreAsync(icao).ConfigureAwait(true);
        }
        finally
        {
            mine.SetResult();
            if (ReferenceEquals(_airportLoadInFlight, mine.Task)) _airportLoadInFlight = null;
        }
    }

    // Non-handler async void (called from the airport textbox's Leave lambda and from
    // the nearest-airport auto-load) — wrapped end-to-end so a DB/graph-build fault
    // can't escape as an unobserved async-void exception; only the augmentation
    // prefetch and the graph build had their own local guards before this.
    private async Task LoadAirportDataCoreAsync(string icao)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(icao)) return;

        // Refresh wingspan from the live SimConnectManager. This form persists
        // across opens (hide-on-close), so the constructor-time wingspan can be
        // stale after a mid-session aircraft swap or after SimConnect connected.
        // _simConnectManager is null only when the form was constructed without
        // one (test/standalone) — fall back to the constructor value in that case.
        if (_simConnectManager != null && _simConnectManager.AircraftWingSpan > 0)
            _aircraftWingspan = _simConnectManager.AircraftWingSpan;

        // Re-enable the "fitting only" checkbox if wingspan data has become
        // available since the form was constructed. The Visible state is
        // refreshed by OnDestTypeChanged when the user selects a parking
        // destination type; the Enabled state needs its own refresh here.
        chkFitFilter.Enabled = _aircraftWingspan > 0;

        // Same airport AND the same gate-list source the graph's stand names were built from.
        // The token half is what lets a graph built before GSX published this airport be
        // rebuilt once it does; without it this return matched forever and the graph kept
        // navdata's concourse letters for the session — see _graphSourceToken. Compared through
        // ShouldRebuildGateList so a transient GSX drop (an API→fallback DOWNGRADE) does not
        // throw away a good graph, exactly as the dropdown cache treats the same token.
        string graphToken = _gateSource?.GetGateListVersion(icao) ?? "none";
        if (icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase) && _graph != null
            && !Services.GateDataSource.ShouldRebuildGateList(_graphSourceToken, graphToken)) return;

        // DROP THE OLD AIRPORT'S GRAPH BEFORE ANYTHING ELSE, and do not claim the new
        // ICAO until one is actually built (below, right after BuildAsync).
        //
        // Every exit between here and there is a failure — airport not in the database,
        // no taxi paths, an exception — and none of them used to touch _graph, while
        // _currentIcao was claimed up front. So a failed load left the form holding the
        // PREVIOUS airport's graph under the NEW airport's name, and the early return
        // above then matched forever, so it could never rebuild.
        //
        // The SayIntentions import turned that from cosmetic into a wrong route:
        // LoadAirportForExternalRouteAsync reports success as "the graph knows some
        // taxiway names", so after taxiing at LMML and flying to an EDDF with no taxi
        // paths, Ctrl+Shift+Y got LMML's taxiway names, snapped EDDF coordinates onto
        // LMML pavement, and — with auto-start on — began guidance on it. A null graph
        // makes the caller's "no taxi path data available" guard do its job, and every
        // _graph == null path in this form already early-returns.
        _graph = null;

        // A new airport: any Place load in flight is now stale, and its catalog is the old airport's.
        _placesLoadId++;
        _placesLoading = false;
        _placesLoadBackground = false;
        _placeToReselect = null;
        _placeCatalog = null;
        _placeCatalogIcao = "";
        _graphSourceToken = "";
        // Invalidate the gate-branch resolution cache — the new airport has a
        // different graph + parking layout. The cache is also re-validated by
        // ICAO inside the GATE branch of PopulateDestinations (defence in depth),
        // but clearing here frees the old airport's spots immediately.
        _cachedGateSpots = null;
        _cachedGateSpotsIcao = "";
        _cachedGateSpotsSourceToken = "";
        _cachedNavdataOnlyStands = null;
        _gateTaxiDistances = null;
        _gateTaxiDistancesAnchor = -1;
        _gateTaxiDistancesIcao = "";
        // A previous import's occupied-stand and wingspan suppressions belonged to that
        // airport's assigned gate; the pilot's own filter settings own the new airport's
        // list.
        _suppressOccupiedFilter = false;
        _suppressFitFilter = false;
        _destinationNodeMap.Clear();
        _destinationHeadingMap.Clear();

        // The previous airport's runways and holding points go BEFORE the step list is
        // emptied (it refills the name box from them), so a failed load below lists none of
        // the old airport's names. The steps named the old airport's taxiways and runways:
        // they go too, silently, as the taxiway rows did (spec 8.2). A same-airport reload
        // (the GSX gate list arriving) keeps them: the clearance is still this airport's, and
        // the name box is emptied only until the new graph refills it below.
        bool sameAirport = icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase);
        _airportRunwayIds = new List<string>();
        _crossRunwayMap.Clear();
        _crossAtHoldPoints.Clear();
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        _namedHoldingPointsResolved = false;
        if (sameAirport) RefreshStepNameList();
        else ClearSteps();

        cmbDestination.Items.Clear();
        // The old graph's runway entries go with it: Full length, until the runway the list
        // below selects rebuilds it (D12). Nothing kept, nothing said.
        PopulateRunwayEntries(keepSelection: false);

        // Check airport exists
        if (!_dataProvider.AirportExists(icao))
        {
            lblStatus.Text = $"Airport {icao} not found in database.";
            return;
        }

        // Fetch online taxiway-name augmentation BEFORE building the graph, so the taxiway list
        // includes the augmented names on first open. Without this, a cache-miss here returns
        // navdata-only and the graph (which never rebuilds for the same airport — see the early
        // return above) would never pick up the names even after the background fetch lands. A cache
        // hit (departure/destination already prefetched) returns instantly; only a never-fetched
        // airport actually waits, and the status line shows why.
        if (_dataProvider is MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider augProvider
            && augProvider.Enabled)
        {
            lblStatus.Text = $"{icao}: fetching taxiway names…";
            // BOUND the wait. A cache hit (dep/dest already prefetched by the flight triggers)
            // completes instantly. A never-fetched airport — typically only an ad-hoc typed ICAO —
            // would otherwise block the form open on a network round-trip up to the HttpClient's
            // 60 s timeout if an Overpass mirror is slow. Wait at most a few seconds; if the fetch
            // hasn't landed, build from navdata NOW. The fetch keeps running in the background
            // (shared in-flight task) and populates the cache, so augmented names appear the next
            // time this airport's taxi form is opened.
            const int prefetchWaitMs = 8000;
            try
            {
                var prefetch = augProvider.PrefetchAsync(icao);
                await Task.WhenAny(prefetch, Task.Delay(prefetchWaitMs));
            }
            catch { /* offline / fetch failed — fall back to navdata names */ }
        }

        // Read BEFORE the paths: online data landing after this moves it, so Where Am I never
        // takes holding points resolved on a graph that lacks that data's taxiway names.
        long holdingPointDataVersion = _guidanceManager.HoldingPointDataVersion(icao);

        // Build graph (off the UI thread to avoid stalls at large airports)
        var paths = _dataProvider.GetTaxiPaths(icao);
        if (paths.Count == 0)
        {
            lblStatus.Text = $"No taxi data for {icao}.";
            return;
        }

        // Navdata's own spot SET — unchanged in count, coordinates and order — carrying the SAME
        // stand names this form's destination dropdown shows, so a stand cannot be "Gate B 25" in
        // the combo and "Gate A 25" when Where-Am-I reads the graph this build produces. The set
        // must stay navdata's: TaxiGraph.Build's parking pass also marks node TYPES, which the
        // hold-short and named-holding-point resolvers read. See Services/ParkingSpotSource.
        var parking = Services.ParkingSpotSource.GetNamedSpots(_dataProvider, _gateSource, icao);
        var starts = _dataProvider.GetRunwayStarts(icao);

        // Runways are handed to the builder so it can repair laterally-bogus start rows
        // against the runway table (SnapStartToRunwayCenterline). Cache it here — the
        // destination/holding-point passes below want the same list anyway.
        if (_cachedRunways == null || _cachedRunwaysIcao != icao)
        {
            _cachedRunways = _dataProvider.GetRunways(icao);
            _cachedRunwaysIcao = icao;
        }

        lblStatus.Text = $"{icao}: building taxi graph…";
        btnCalculate.Enabled = false;
        try
        {
            _graph = await TaxiGraph.BuildAsync(paths, parking, starts, _cachedRunways);
        }
        finally
        {
            btnCalculate.Enabled = true;
        }

        // Claimed only now, on a graph that exists. Everything below reads _currentIcao
        // (PopulateDestinations, ResolveNamedHoldingPoints, the gate-spot cache), so it
        // has to be set before them and cannot simply move to the end of the method.
        _currentIcao = icao.ToUpperInvariant();
        // Stamped with the graph, for the same reason and at the same moment: these names are
        // now frozen into the nodes, and this records which source they came from.
        _graphSourceToken = graphToken;
        _graphHoldingPointDataVersion = holdingPointDataVersion;

        lblStatus.Text = $"{icao}: {_graph.Nodes.Count} nodes, {paths.Count} paths.";

        // Resolve the airport's published named holding points (VIKAS, N2E…)
        // onto the fresh graph, so the Hold short of list has them ready on first
        // open. A fetch that hasn't landed yet is fine: the name box re-resolves when
        // focus reaches it (EnsureHoldingPointsListed) or its list is refilled.
        ResolveNamedHoldingPoints();

        // Populate destinations
        PopulateDestinations();

        // Name the default full-length holding point for the runway the list just
        // auto-selected. Without this the ONE case that never gets the call-out is the
        // pilot who accepts the pre-selected runway — the case where they are least
        // likely to have thought about which painted line the route will use.
        AnnounceDefaultHoldingPoint(statusPrefix: lblStatus.Text);

        // Cache the airport's runway designators for the Cross and Hold short of
        // name lists. Same source as the destination dropdown, but unfiltered by
        // IsTakeoff — the user might want to hold short of a runway they can't take
        // off from (perfectly fine; it's still a runway you must hold short of).
        // Closed runways ARE excluded — no point holding short of pavement
        // that's marked closed. GetRunways returns BOTH ends as separate entries
        // ("10R" and "28L"), so the pilot picks whichever designator ATC named.
        var openRunways = _dataProvider.GetRunways(icao).Where(r => !r.IsClosed).ToList();
        _airportRunwayIds = openRunways
            .Select(r => r.RunwayID)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Every destination type: a Cross step can come with any destination (see _crossRunwayMap).
        foreach (var rwy in openRunways)
        {
            string label = $"Runway {rwy.RunwayID}";
            if (!_crossRunwayMap.ContainsKey(label))
                _crossRunwayMap[label] = rwy;
        }

        // Fill the name box for the new airport: on an empty list, the nearest taxiway in the
        // direction the aircraft faces is preselected, as the first-taxiway box did.
        RefreshStepNameList();
      }
      catch (Exception ex)
      {
          lblStatus.Text = $"Error loading {icao}: {ex.Message}";
          btnCalculate.Enabled = true;
      }
    }

    /// <summary>Selects a destination without the holding-point call-out — the list rebuild
    /// and the external-route import both drive this without the pilot choosing anything.</summary>
    private void SelectDestinationSilently(int index)
    {
        bool prior = _suppressHoldingPointAnnounce;
        _suppressHoldingPointAnnounce = true;
        try { cmbDestination.SelectedIndex = index; }
        finally { _suppressHoldingPointAnnounce = prior; }
    }

    /// <summary>
    /// Taxi distance from the aircraft to every reachable graph node, computed once per
    /// (airport, anchor node) and reused across search keystrokes and filter toggles.
    /// Null when there is no graph or the aircraft has no nearby node, which sends
    /// <see cref="StandRankMeters"/> back to straight-line ordering — the behaviour
    /// before taxi ranking existed, so a graph this cannot anchor on never loses a list.
    /// </summary>
    private Dictionary<int, double>? GetGateTaxiDistances()
    {
        if (_graph == null) return null;

        var anchor = _graph.FindNearestNode(_aircraftLat, _aircraftLon);
        if (anchor == null) return null;

        if (_gateTaxiDistances != null
            && _gateTaxiDistancesAnchor == anchor.NodeId
            && _gateTaxiDistancesIcao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase))
        {
            return _gateTaxiDistances;
        }

        try
        {
            _gateTaxiDistances = new TaxiRouter(_graph).ComputeGraphDistancesFrom(anchor.NodeId);
            _gateTaxiDistancesAnchor = anchor.NodeId;
            _gateTaxiDistancesIcao = _currentIcao;
        }
        catch (Exception ex)
        {
            // Ordering is a convenience; never let it cost the pilot the list itself.
            Log.Warn("TaxiAssistForm", $"Stand taxi-distance ranking unavailable: {ex.Message}");
            _gateTaxiDistances = null;
            _gateTaxiDistancesAnchor = -1;
            _gateTaxiDistancesIcao = "";
        }
        return _gateTaxiDistances;
    }

    /// <summary>
    /// Sort key for the stand list: taxi distance where the graph can answer, otherwise a
    /// straight-line distance pushed behind every reachable stand by a finite offset.
    /// A stand with no routing node at all (listed as "(no taxi route)") is unreachable by
    /// definition and sorts with them.
    /// </summary>
    private double StandRankMeters(ParkingSpot spot, int nodeId, Dictionary<int, double>? taxiDistances)
    {
        double straightLine = TaxiGraph.CalculateDistanceMeters(
            _aircraftLat, _aircraftLon, spot.Latitude, spot.Longitude);

        if (taxiDistances == null) return straightLine;
        if (nodeId >= 0 && taxiDistances.TryGetValue(nodeId, out double taxiM)) return taxiM;
        return UNREACHABLE_STAND_RANK_M + straightLine;
    }

    /// <summary>Whether the wingspan "show fitting only" filter applies to this
    /// <see cref="PopulateDestinations"/> pass.
    ///
    /// Three conditions, and the middle one is the fix this method exists to carry: an
    /// EXTERNAL clearance's gate is never wingspan-filtered. That data is authored by
    /// the scenery (<see cref="ParkingSpot.FitsAircraft"/> reads a navdata parking radius
    /// or a GSX max wing span) and is often wrong, while a stand SayIntentions named is a
    /// stand a controller told the pilot to taxi to. Filtered out, the label never reaches
    /// the combo or the maps behind it, so the name, alias and published-coordinate steps
    /// of <see cref="TryResolveExternalDestination"/> all miss together and a known
    /// arrival fails loudly for a stand the airport has.
    ///
    /// The pilot's own tick still decides for their own browsing, and a zero wingspan
    /// means there is nothing to filter against at all.</summary>
    internal static bool ShouldApplyFitFilter(
        bool fitFilterChecked, bool suppressedForExternalDestination, double aircraftWingspanFeet)
        => fitFilterChecked && !suppressedForExternalDestination && aircraftWingspanFeet > 0;

    private void PopulateDestinations()
    {
        cmbDestination.Items.Clear();
        _destinationNodeMap.Clear();
        _destinationHeadingMap.Clear();
        _destinationHeadingTrueMap.Clear();
        _destinationThresholdMap.Clear();
        _destinationSpotMap.Clear();

        if (_graph == null) return;

        bool isRunway = cmbDestType.SelectedIndex == 0;
        bool isDeice = cmbDestType.SelectedIndex == 3;
        bool isPlace = cmbDestType.SelectedIndex == 4;

        if (isRunway)
        {
            // Build a runway-name → StartPosition lookup so RunwayLineupTarget (below) can pick
            // the right start row for each runway end. ALL rows per runway end, not the first
            // one: a runway end can carry several, and TaxiGraph.PickFullLengthStart picks the
            // full-length (furthest-back) one and REJECTS a row past 40 % of the runway. Taking
            // g.First() took whichever the DB happened to return — EGLL 09R's is 342 m down the
            // runway with a 67 m row sitting right there, and LatinVFR's LEMD parks four
            // runways' rows at the MIDPOINT — which anchors both the route destination and the
            // lineup target mid-field, and skews the holding-point envelope's LineupAlong with
            // them. See RunwayLineupTarget for why the lineup point is anchored on this table at
            // all (displaced thresholds) and how it becomes a route destination node.
            var startsByRunway = _dataProvider.GetRunwayStarts(_currentIcao)
                .Where(s => !string.IsNullOrEmpty(s.RunwayName))
                .GroupBy(s => s.RunwayName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var runways = _dataProvider.GetRunways(_currentIcao);

            foreach (var rwy in runways)
            {
                // Filter against runway operational flags from the DB. Defaults
                // are permissive (closed=false, can-takeoff=true) so DBs that
                // don't populate these columns still see every runway. Users
                // with Navigraph or third-party scenery — which DOES populate
                // these — won't see closed runways in the destination dropdown.
                if (rwy.IsClosed) continue;

                // One owner for the lineup point and the runway-entry node — see RunwayLineupTarget
                // (the displaced-threshold and LPPT-20 reasoning moved there with the code). The
                // aircraft position anchors the reachability filter; (0,0) means SimConnect has not
                // reported one yet, the same "haveOwnPosition" test the gate list uses.
                bool haveOwnPos = _aircraftLat != 0 || _aircraftLon != 0;
                startsByRunway.TryGetValue(rwy.RunwayID, out var rwyStartRows);
                var lineup = RunwayLineupTarget.Resolve(_graph, rwy, rwyStartRows,
                    haveOwnPos ? _aircraftLat : null, haveOwnPos ? _aircraftLon : null);
                double lineupLat = lineup.LineupLat;
                double lineupLon = lineup.LineupLon;
                var nearNode = lineup.EntryNode;
                if (nearNode != null)
                {
                    // "(backtrack required)" is part of the ITEM LABEL so the screen
                    // reader speaks it while the pilot arrows the list — the LICR
                    // class, where the unticked route can quietly end hundreds of
                    // metres short of the threshold with no warning (the destination
                    // node sits just under the reach warning's 120 m bar).
                    double rwyHalfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
                    bool needsBacktrack = _graph.RunwayNeedsBacktrack(
                        lineupLat, lineupLon,
                        rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
                        rwyHalfWidthM, _aircraftLat, _aircraftLon);
                    string name = $"Runway {rwy.RunwayID}"
                        + (needsBacktrack ? BacktrackRequiredSuffix : "");

                    if (!_destinationNodeMap.ContainsKey(name))
                    {
                        _destinationNodeMap[name] = nearNode.NodeId;
                        _destinationHeadingMap[name] = rwy.HeadingMag;
                        _destinationHeadingTrueMap[name] = rwy.Heading;
                        _destinationThresholdMap[name] = (lineupLat, lineupLon);
                        cmbDestination.Items.Add(name);
                    }
                }
            }
        }
        else if (isDeice)
        {
            // Deice area path: populate from GateDataSource.GetDeiceAreas().
            // Uses the same node-resolution and _destinationSpotMap machinery as the
            // gate path so OnCalculateClicked can resolve the spot and hand it to
            // DockingGuidanceManager.SetDestinationGate (which handles the
            // IsDeiceArea flag internally — emits "Deicing guidance" and uses
            // datum alignment). MAX_PARKING_TO_GRAPH_M matches the gate path so
            // spots without a nearby graph node are silently dropped (no way to
            // taxi there).
            const double MAX_DEICE_TO_GRAPH_M = 100.0;

            var deiceAreas = _gateSource?.GetDeiceAreas(_currentIcao) ?? new List<ParkingSpot>();

            foreach (var spot in deiceAreas.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                // ONE entry per spot — same rule as the gate path: ToString() shows any online
                // alias as a "(also X)" hint, and a separate per-alias dropdown entry is NOT added
                // (it doubled the list at airports whose online names differ from the scenery names).
                string label = spot.ToString();
                if (_destinationNodeMap.ContainsKey(label)) continue;

                // Prefer the GSX stop position (the docking target) for routing;
                // fall back to the spot's base lat/lon when stop position is absent.
                // Test HasValue, not "!= 0": null is the only correct "absent"
                // signal. A stop coordinate or heading that legitimately normalizes
                // to exactly 0.0 — a due-north (0°) stop heading, or the rare 0.0°
                // lon/lat — is a real value, and a `GetValueOrDefault() != 0` test
                // would discard it and silently substitute the parking-position
                // value. This mirrors DockingGuidanceManager's `StopHeading ?? Heading`
                // null-coalescing convention.
                double targetLat = spot.StopLatitude.HasValue
                    ? spot.StopLatitude.Value : spot.Latitude;
                double targetLon = spot.StopLongitude.HasValue
                    ? spot.StopLongitude.Value : spot.Longitude;

                var nearNode = _graph.FindNearestNode(targetLat, targetLon);
                if (nearNode == null) continue;

                double dist = TaxiGraph.CalculateDistanceMeters(
                    nearNode.Latitude, nearNode.Longitude, targetLat, targetLon);
                if (dist > MAX_DEICE_TO_GRAPH_M) continue;

                double stopHeading = spot.StopHeading.HasValue
                    ? spot.StopHeading.Value : spot.Heading;
                _destinationNodeMap[label] = nearNode.NodeId;
                _destinationHeadingMap[label] = stopHeading;
                _destinationHeadingTrueMap[label] = stopHeading;
                _destinationThresholdMap[label] = (targetLat, targetLon);
                _destinationSpotMap[label] = spot;
                cmbDestination.Items.Add(label);
            }
        }
        else if (isPlace)
        {
            // PLACE: FBOs, hangars, fuel, terminals and cargo from the surroundings catalog, each
            // resolved onto a stand or taxi node by PlaceListBuilder. Fills the same maps as the gate
            // branch, so Calculate, routing and docking need nothing Place-specific.
            var catalog = PlaceCatalogOrStartLoad(_currentIcao);
            if (catalog != null)
            {
                var gateSpots = EnsureGateSpotCache();
                var selectable = gateSpots.Select(g => new Navigation.Surroundings.StandCandidate(g.spot, g.nodeId)).ToList();
                var navdataOnly = EnsureNavdataOnlyStands(gateSpots);

                // The same two filters the Gate list applies — a Place must not route a heavy onto a
                // commuter stand, or onto a stand with an aircraft on it. One traffic snapshot per pass.
                bool fit = ShouldApplyFitFilter(chkFitFilter.Checked, _suppressFitFilter, _aircraftWingspan);
                var ground = chkHideOccupied.Checked && !_suppressOccupiedFilter && _tcasService != null
                    ? _tcasService.GetTraffic(onGround: true) : null;
                bool Allowed(ParkingSpot s) => (!fit || s.FitsAircraft(_aircraftWingspan))
                    && (ground == null || ground.Count == 0 || SpotIsOccupied(ground, s.Latitude, s.Longitude) == null);

                foreach (var entry in Navigation.Surroundings.PlaceListBuilder.Build(catalog, selectable, navdataOnly, NearestRoutableNode, Allowed))
                {
                    _destinationNodeMap[entry.Label] = entry.NodeId;
                    if (entry.HeadingDeg is double heading)
                    {
                        // A STAND: same target and heading the "Gate, stand or parking" type uses for it.
                        _destinationHeadingMap[entry.Label] = heading;
                        _destinationHeadingTrueMap[entry.Label] = heading;   // parking heading is true heading
                        _destinationThresholdMap[entry.Label] = (entry.Lat, entry.Lon);
                    }
                    // else a "nearest taxiway point" place: NO heading and NO lineup target, so arrival
                    // takes the "no lineup data — just stop" path instead of aligning the nose on a building.
                    if (entry.Spot != null) _destinationSpotMap[entry.Label] = entry.Spot;
                    cmbDestination.Items.Add(entry.Label);
                }
            }
        }
        else
        {
            // PARITY WITH THE GATE-TELEPORT DIALOG. Earlier the parking listing
            // was driven off graph nodes that happened to be tagged with a
            // ParkingName during graph build — which silently dropped any
            // parking spot whose lat/lon didn't have a nearby graph node
            // (common in third-party scenery whose taxi-path data lags the
            // parking layout). Result: a pilot given "Parking 21" by ATC
            // would see "Parking 21" in the gate teleport dialog but NOT in
            // the taxi guidance form — confusing and route-blocking.
            //
            // The fix: drive the dropdown directly from the parking list —
            // resolved through `Services.ParkingSpotSource`, the one resolution
            // gate teleport, the graph builds and the SayIntentions
            // parked-at-the-right-stand check also use, so a stand cannot be
            // named two different ways in one session — and use
            // `ParkingSpot.ToString()` for identical display labels
            // (e.g. "P 21 - Ramp GA Large (Jetway)").
            // Each parking spot's actual lat/lon is the convergence target,
            // matching what TeleportToParkingSpot places you at, so taxi
            // guidance and gate teleport end up at the same physical position.
            // Routing endpoint = nearest graph node to the parking spot
            // (within 100 m); if the graph has no reachable node within that
            // radius, the spot is dropped — there's no way to taxi there.
            var gateSpots = EnsureGateSpotCache();

            // ── Per-pass filter + ordering (cheap, in-memory) ─────────────────
            // Category display order matching GateTeleportForm: gates first
            // (small → extra), then ramp types, then dock/other.
            var categoryOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Gate Small"] = 1, ["Gate Medium"] = 2, ["Gate Large"] = 3,
                ["Gate Heavy"] = 4, ["Gate Extra"] = 5,
                ["Ramp GA"] = 6, ["Ramp Cargo"] = 7, ["Ramp Military"] = 8,
                ["Dock"] = 9, ["Other"] = 10
            };

            IEnumerable<(ParkingSpot spot, int nodeId)> filtered = gateSpots;

            // Gate search filter: type-to-filter on name+number+suffix. Run against
            // the cached resolved list per keystroke (GateSearchFilter operates on
            // ParkingSpot, so project, filter, then re-pair with the node id).
            if (!string.IsNullOrEmpty(txtGateSearch.Text))
            {
                var matched = new HashSet<ParkingSpot>(
                    Services.GateSearchFilter.Filter(gateSpots.Select(r => r.spot).ToList(), txtGateSearch.Text));
                filtered = filtered.Where(r => matched.Contains(r.spot));
            }

            // Wingspan filter: spot must be large enough for the aircraft. Applied
            // PER PASS against the cached (unfiltered) list — never baked into the
            // cache — because _aircraftWingspan can change between passes (mid-session
            // aircraft swap) and chkFitFilter toggles re-run PopulateDestinations.
            // Source-aware (see ParkingSpot.FitsAircraft): GSX spots use the
            // authoritative max wing span (metres); navdata spots use the physical
            // parking radius (feet). The old "Radius >= wingspan/2" mixed units for
            // GSX spots (metres vs a feet threshold) and filtered nearly everything out.
            // Inert while an external clearance's gate is being resolved or is seated
            // (see _suppressFitFilter) — that size data is scenery-authored and often
            // wrong, and the pilot was TOLD to go to that stand.
            if (ShouldApplyFitFilter(chkFitFilter.Checked, _suppressFitFilter, _aircraftWingspan))
                filtered = filtered.Where(r => r.spot.FitsAircraft(_aircraftWingspan));

            // Occupied-stand filter: a stand with an aircraft parked on it is not a
            // destination, so drop it rather than let the pilot taxi to it and hear the
            // "someone is at your gate" warning only at Calculate. Same rule as
            // CheckGateOccupancy (SpotIsOccupied) so the list and that warning can never
            // disagree. Applied PER PASS, never baked into _cachedGateSpots — traffic
            // moves. Silently inert when there is no traffic feed.
            // NOT gated on chkHideOccupied.Visible: that is EFFECTIVE visibility, false
            // while the form itself is hidden, so the same list came back filtered on a
            // shown form and unfiltered on a hidden one (the SayIntentions import builds
            // it hidden). This block already sits inside the gate branch, which is the
            // condition the Visible flag was standing in for.
            int occupiedHidden = 0;
            if (chkHideOccupied.Checked && !_suppressOccupiedFilter && _tcasService != null)
            {
                // Snapshot the traffic ONCE for the whole pass. This runs on every gate-
                // search keystroke, and GetTraffic copies its dictionary under a lock —
                // calling it per spot would be hundreds of locked copies per keystroke.
                var ground = _tcasService.GetTraffic(onGround: true);
                if (ground.Count > 0)
                {
                    var kept = new List<(ParkingSpot spot, int nodeId)>();
                    foreach (var r in filtered)
                    {
                        if (SpotIsOccupied(ground, r.spot.Latitude, r.spot.Longitude) != null) occupiedHidden++;
                        else kept.Add(r);
                    }
                    filtered = kept;
                }
            }

            // Nearest first — a taxi destination is chosen from where the aircraft is
            // standing, so the stands it might actually be going to belong at the top of
            // a list a screen reader walks one item at a time. Falls back to the old
            // category / number / name ordering when the position isn't known yet
            // (LoadAirportData before the first position fix).
            //
            // "Nearest" is TAXI distance, never straight-line. A stand on the far side of
            // a runway is not near, however close it looks as the crow flies — the same
            // lesson the router already learned for exit and entry-node picking (the KDEN
            // M4 and EVRA cases). Measured live 2026-09-20 at EGLL: after vacating 27L at
            // N7 on the NORTH side, Gate 405 (Terminal 4, 706 m SOUTH of the centreline)
            // sat near the top of the list at 1.87 km straight-line, while the taxi to it
            // was 2.4 km, opened with a 166-degree turn back and crossed runway 27L. The
            // pilot picked it believing the list was ordered by what was closest.
            bool haveOwnPosition = _aircraftLat != 0 || _aircraftLon != 0;
            var taxiDistances = haveOwnPosition ? GetGateTaxiDistances() : null;
            var parkingSpots = haveOwnPosition
                ? filtered
                    .OrderBy(r => StandRankMeters(r.spot, r.nodeId, taxiDistances))
                    .ThenBy(r => r.spot.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : filtered
                    .OrderBy(r => categoryOrder.TryGetValue(r.spot.GetFilterCategory(), out int o) ? o : 99)
                    .ThenBy(r => r.spot.Number > 0 ? r.spot.Number : int.MaxValue)
                    .ThenBy(r => r.spot.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var (spot, nodeId) in parkingSpots)
            {
                // ONE entry per spot — matches the gate-teleport dialog (GateTeleportForm). ToString()
                // appends any online alias as a "(also X)" hint, and the alias-aware gate search box
                // above (GateSearchFilter, which matches ParkingSpot.Aliases) lets the pilot type the
                // ATC name to find it. We deliberately do NOT add a separate dropdown entry per alias:
                // at airports whose online names differ from the scenery names (e.g. LIMC) that doubled
                // the gate list with a near-identical "online" duplicate of every "scenery" gate.
                string label = spot.ToString();
                if (nodeId < 0) label += " (no taxi route)";
                if (_destinationNodeMap.ContainsKey(label)) continue;

                _destinationNodeMap[label] = nodeId;
                _destinationHeadingMap[label] = spot.Heading;
                _destinationHeadingTrueMap[label] = spot.Heading; // parking heading is true heading
                _destinationThresholdMap[label] = (spot.Latitude, spot.Longitude);
                _destinationSpotMap[label] = spot;
                cmbDestination.Items.Add(label);
            }

            // Say how many stands the occupancy filter removed, on the status line only
            // (never spoken — the pilot is typing in the search box or arrowing the list
            // and the screen reader is already talking). Without it a stand that is
            // simply absent from the list reads as missing scenery data.
            if (occupiedHidden > 0)
                lblStatus.Text = $"{cmbDestination.Items.Count} listed, " +
                                 $"{occupiedHidden} hidden (occupied by traffic).";
        }

        // Progressive Taxi (index 2) lists nothing here: the leg has no destination and ends
        // where its steps say. The runways its end and the Cross steps resolve against
        // (_crossRunwayMap) are filled once per airport, for every destination type.

        // Silently: in gate mode this runs on every search keystroke. Not while a Place label is
        // waiting to be put back — item 0 is exactly what that must not become.
        if (cmbDestination.Items.Count > 0
            && !(cmbDestType.SelectedIndex == 4 && _placeToReselect != null))
            SelectDestinationSilently(0);
    }

    /// <summary>A stand with no reachable taxi-graph node within this radius has no route to
    /// it. The gate list keeps such a spot and marks it "(no taxi route)"; the Place list
    /// resolves it to -1, which PlaceListBuilder treats as unreachable.</summary>
    private const double MAX_PARKING_TO_GRAPH_M = 100.0;

    /// <summary>
    /// The SELECTABLE stands for the loaded airport, each already paired with its routing node —
    /// resolved ONCE per (ICAO, gate-list SOURCE) into <see cref="_cachedGateSpots"/>.
    /// <para>Cached because <see cref="PopulateDestinations"/> runs on every search keystroke and this
    /// is heavy UI-thread work. Keyed on the gate-list token as well as the ICAO (a property read), so
    /// a list bound before GSX published the airport rebuilds the moment it does.</para>
    /// </summary>
    private List<(ParkingSpot spot, int nodeId)> EnsureGateSpotCache()
    {
        string sourceToken = CurrentGateSourceToken();
        if (_cachedGateSpots != null
            && _cachedGateSpotsIcao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase)
            && !Services.GateDataSource.ShouldRebuildGateList(_cachedGateSpotsSourceToken, sourceToken))
            return _cachedGateSpots;

        // The SELECTABLE list, because a destination is acted on (wingspan, stop position,
        // GsxIdentifier, TerminalName), plus this scenery's online gate aliases so the pilot can
        // pick the gate ATC names.
        var sourceSpots = Services.ParkingSpotSource.GetSelectableGates(_dataProvider, _gateSource, _currentIcao);
        var resolved = new List<(ParkingSpot spot, int nodeId)>(sourceSpots.Count);
        foreach (var spot in sourceSpots)
        {
            int nodeId = -1; // -1 = no reachable taxi-graph node (kept, marked "(no taxi route)")
            var nearNode = _graph?.FindNearestNode(spot.Latitude, spot.Longitude);
            if (nearNode != null)
            {
                double dist = TaxiGraph.CalculateDistanceMeters(
                    nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                if (dist <= MAX_PARKING_TO_GRAPH_M)
                    nodeId = nearNode.NodeId;
            }
            resolved.Add((spot, nodeId));
        }
        _cachedGateSpots = resolved;
        _cachedGateSpotsIcao = _currentIcao;
        _cachedGateSpotsSourceToken = sourceToken;
        // The navdata-only set is defined by SUBTRACTING the selectable list, so it is stale
        // the moment that list is rebuilt.
        _cachedNavdataOnlyStands = null;
        return resolved;
    }

    /// <summary>
    /// Navdata stands the selectable list lacks (GSX drops vehicle, fuel and headingless stands —
    /// just where an FBO, hangar or fuel place ends). A second-choice source for PlaceListBuilder,
    /// with no GsxIdentifier. "Lacks" is judged by position (10 m), never by name, which differs
    /// between the lists. The navdata list is only read: it may be the provider's own instance.
    /// </summary>
    private List<Navigation.Surroundings.StandCandidate> EnsureNavdataOnlyStands(
        List<(ParkingSpot spot, int nodeId)> selectable)
    {
        // EnsureGateSpotCache drops this whenever it rebuilds, so the ICAO/token keys it
        // checked are the ones this list was built under.
        if (_cachedNavdataOnlyStands != null) return _cachedNavdataOnlyStands;

        const double SAME_STAND_M = 10.0;
        var named = Services.ParkingSpotSource.GetNamedSpots(_dataProvider, _gateSource, _currentIcao);
        var extra = new List<Navigation.Surroundings.StandCandidate>();
        foreach (var spot in named)
        {
            bool alreadySelectable = false;
            foreach (var (s, _) in selectable)
            {
                if (TaxiGraph.CalculateDistanceMeters(s.Latitude, s.Longitude, spot.Latitude, spot.Longitude) <= SAME_STAND_M)
                {
                    alreadySelectable = true;
                    break;
                }
            }
            if (alreadySelectable) continue;

            int nodeId = -1;
            var nearNode = _graph?.FindNearestNode(spot.Latitude, spot.Longitude);
            if (nearNode != null
                && TaxiGraph.CalculateDistanceMeters(nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude) <= MAX_PARKING_TO_GRAPH_M)
                nodeId = nearNode.NodeId;
            extra.Add(new Navigation.Surroundings.StandCandidate(spot, nodeId));
        }
        _cachedNavdataOnlyStands = extra;
        return extra;
    }

    /// <summary>See <see cref="Navigation.Surroundings.PlaceListBuilder.NearestRoutableNode"/>.</summary>
    private Navigation.Surroundings.NearestNode? NearestRoutableNode(double lat, double lon)
        => _graph == null ? null : Navigation.Surroundings.PlaceListBuilder.NearestRoutableNode(_graph, lat, lon);

    /// <summary>
    /// The catalog to list Places from: the cache's fresh one, else the one this form last loaded
    /// for the same airport (stale but usable). When the cache has nothing fresh a load starts —
    /// silently in the background if a list is already showing.
    /// </summary>
    private Navigation.Surroundings.AirportFeatureCatalog? PlaceCatalogOrStartLoad(string icao)
    {
        var fresh = SurroundingsCatalogCached?.Invoke(icao);
        if (fresh != null)
        {
            _placeCatalog = fresh;
            _placeCatalogIcao = icao;
            return fresh;
        }
        bool haveOld = _placeCatalog != null && string.Equals(_placeCatalogIcao, icao, StringComparison.OrdinalIgnoreCase);
        if (!_placesLoading && !_placesSettling) LoadPlaces(icao, background: haveOld);
        return haveOld ? _placeCatalog : null;
    }

    /// <summary>
    /// Loads the catalog off the UI thread and rebuilds the Place list when it lands. The pilot's
    /// selection is put back if the new list still carries it; otherwise it is cleared (never item
    /// 0, which a Calculate would route to) and the pilot is told. A foreground load says "Loading
    /// places" and then the count; a background refresh speaks only if the list changed.
    /// </summary>
    private async void LoadPlaces(string icao, bool background)
    {
        if (SurroundingsCatalogAsync == null) return;
        int id = ++_placesLoadId;
        _placesLoading = true;
        _placesLoadBackground = background;
        bool silent = _suppressPlaceAnnounce;   // RestoreDestinationState restores it long before we settle
        if (!silent && !background && Visible) _announcer.Announce($"Loading places for {icao}.");

        Navigation.Surroundings.AirportFeatureCatalog? built = null;
        try { built = await SurroundingsCatalogAsync(icao); }
        catch (Exception ex) { _taxiFormLog.Info($"Place load for {icao} failed: {ex.Message}"); }
        // Never settle on the caller's stack: GetAsync can complete synchronously, and the caller
        // is PopulateDestinations itself.
        await Task.Yield();

        if (id != _placesLoadId) return;   // superseded by a newer load or an airport change
        _placesLoading = false;
        bool wasBackground = _placesLoadBackground;
        try
        {
            if (IsDisposed || !IsHandleCreated || _graph == null || cmbDestType.SelectedIndex != 4
                || !string.Equals(icao, _currentIcao, StringComparison.OrdinalIgnoreCase))
            {
                _placeToReselect = null;
                return;
            }
            if (built != null) { _placeCatalog = built; _placeCatalogIcao = icao; }

            string? keep = _placeToReselect ?? cmbDestination.SelectedItem?.ToString();
            _placeToReselect = null;
            bool hadList = cmbDestination.Items.Count > 0;
            int countBefore = cmbDestination.Items.Count;
            _placesSettling = true;
            try { PopulateDestinations(); }
            finally { _placesSettling = false; }

            bool lost = false;
            if (keep != null)
            {
                int idx = cmbDestination.Items.IndexOf(keep);
                if (idx >= 0) SelectDestinationSilently(idx);
                else
                {
                    if (cmbDestination.Items.Count > 0) cmbDestination.SelectedIndex = -1;
                    lost = cmbDestination.Items.Count > 0;
                    _taxiFormLog.Info($"Place list for {icao} rebuilt; previous destination '{keep}' is no longer listed.");
                }
            }
            else if (hadList && cmbDestination.Items.Count > 0)
                cmbDestination.SelectedIndex = -1;   // it was deliberately cleared; keep it so

            if (silent || !Visible) return;
            string? countLine = wasBackground
                ? DescribeBackgroundPlaceRefresh(Visible, countBefore, cmbDestination.Items.Count, _placeCatalog != null, icao)
                : DescribePlaceList(built != null, cmbDestination.Items.Count, icao);
            if (lost) _announcer.Announce(PlaceListUpdatedMessage);
            else if (countLine != null) _announcer.Announce(countLine);
        }
        catch (Exception ex)
        {
            _taxiFormLog.Error($"Place list refresh for {icao} failed: {ex}");
        }
    }

    /// <summary>
    /// The OSM buildings for <paramref name="icao"/> arrived after the catalog was built
    /// (OnlineFeatureStore.FeaturesUpdated, marshalled to the UI thread). Refreshes a showing Place
    /// list in the background — after the pilot closes the dropdown if it is open.
    /// </summary>
    public void OnSurroundingsInvalidated(string icao)
    {
        if (IsDisposed || !IsHandleCreated || _graph == null || cmbDestType.SelectedIndex != 4) return;
        if (!string.Equals(icao, _currentIcao, StringComparison.OrdinalIgnoreCase)) return;
        if (_placesLoading) return;
        if (cmbDestination.DroppedDown) { _placesRefreshAfterDropDown = icao; return; }
        LoadPlaces(icao, background: true);
    }

    private void OnDestinationDropDownClosed(object? sender, EventArgs e)
    {
        string? icao = _placesRefreshAfterDropDown;
        _placesRefreshAfterDropDown = null;
        if (icao != null) OnSurroundingsInvalidated(icao);
    }

    /// <summary>What the Place list says about itself, or null with no airport to name. An empty
    /// list is either "this airport has no places" or "nothing could be loaded".</summary>
    internal static string? DescribePlaceList(bool catalogPresent, int listedCount, string icao)
    {
        if (string.IsNullOrWhiteSpace(icao)) return null;
        if (!catalogPresent) return $"Places could not be loaded for {icao}.";
        return listedCount > 0
            ? $"{listedCount} place{(listedCount == 1 ? "" : "s")} listed."
            : $"No places to route to at {icao}.";
    }

    /// <summary>A background refresh speaks only when the list really changed and the dialog is
    /// open to hear it.</summary>
    internal static string? DescribeBackgroundPlaceRefresh(
        bool visible, int countBefore, int countAfter, bool catalogPresent, string icao)
        => !visible || countBefore == countAfter ? null : DescribePlaceList(catalogPresent, countAfter, icao);

    /// <summary>The gate-list source token for the loaded airport — see
    /// <see cref="Services.GateDataSource.GetGateListVersion"/>. "none" when this form was
    /// built without a gate source, so the token can never differ and the cache falls back to
    /// its original ICAO-only invalidation. Never throws (GetGateListVersion never does).</summary>
    private string CurrentGateSourceToken()
        => _gateSource?.GetGateListVersion(_currentIcao) ?? "none";

    /// <summary>
    /// Rebuilds the gate destination list when the gate-list SOURCE has moved since the cache
    /// was filled — the fallback → Remote API flip described at <see cref="_cachedGateSpots"/>.
    /// <see cref="PopulateDestinations"/> already checks the token on every pass, but it runs
    /// only on airport load, destination-type change, fit-filter change and gate-search
    /// keystrokes — NOT when the form is re-shown and NOT before Calculate. So a form opened,
    /// hidden and re-shown after GSX caught up, or left open through a whole flight, kept
    /// serving the identifier-less list. This closes those two gaps and is called from
    /// <see cref="OnVisibleChanged"/> and the top of <see cref="OnCalculateClicked"/>.
    /// <para>
    /// A no-op (one string compare) unless the token differs AND the destination type is
    /// "Gate, stand or parking" AND a graph is loaded — the only combination in which the cache is even
    /// consulted. When it does rebuild, the pilot's current selection is put back by label
    /// where the new list still carries it; the return value says whether it could NOT be —
    /// which the caller must act on, because <see cref="PopulateDestinations"/> lands the combo
    /// on item 0, a stand the pilot never chose. On the Calculate path that means ABORT, not
    /// "route to item 0"; on the show path it means a queued announcement. Neither is a
    /// UI-interaction echo: the list changed under the pilot because of a background GSX event.
    /// </para>
    /// </summary>
    /// <returns>True when a destination was selected before the rebuild and could not be
    /// restored afterwards (the selection is now cleared — never item 0). False when nothing
    /// was rebuilt, the selection survived, or nothing was selected to begin with.</returns>
    private bool RefreshDestinationsIfGateSourceChanged()
    {
        // Place is here for the same reason Gate is: its list is built from EnsureGateSpotCache,
        // so a Place list bound before GSX published the airport carries the same identifier-less
        // stands, and is rebuilt on the same token move.
        if (_graph == null || cmbDestType.SelectedIndex is not (1 or 4)) return false;

        string token = CurrentGateSourceToken();
        // Upgrade/refresh only — a transient drop downgrades the token and must NOT
        // rebuild (see GateDataSource.ShouldRebuildGateList); the check inside
        // PopulateDestinations applies the same rule, so both agree.
        if (_cachedGateSpots == null
            || !Services.GateDataSource.ShouldRebuildGateList(_cachedGateSpotsSourceToken, token))
            return false;

        string? previous = cmbDestination.SelectedItem?.ToString();
        PopulateDestinations();

        // A Place list still loading is filled later: hand the label to that load, which puts it
        // back or reports it gone.
        if (cmbDestType.SelectedIndex == 4 && _placesLoading && cmbDestination.Items.Count == 0)
        {
            if (!string.IsNullOrEmpty(previous)) _placeToReselect = previous;
            return false;
        }

        if (string.IsNullOrEmpty(previous))
        {
            // Nothing was selected before this rebuild — which, on this form, means
            // an EARLIER rebuild already lost the pilot's stand and cleared the
            // selection (PopulateDestinations itself always seats item 0). Keep it
            // cleared: a later same-tier refresh (GSX republishes handlerData on an
            // arrival-gate assignment; a reconnect snapshot) must not quietly seat
            // item 0 and let the next Calculate route to it. The pilot was already
            // told to choose again; nothing new to say.
            if (cmbDestination.Items.Count > 0)
                cmbDestination.SelectedIndex = -1;
            return false;
        }
        int idx = cmbDestination.Items.IndexOf(previous);
        if (idx >= 0)
        {
            cmbDestination.SelectedIndex = idx;
            return false;
        }
        // The pilot's stand is gone from the rebuilt list. LEAVE NOTHING SELECTED:
        // PopulateDestinations lands the combo on item 0, and a later Calculate on
        // that would route (and gate.select) to a stand the pilot never chose —
        // with -1 it aborts with "Please select a destination." instead.
        cmbDestination.SelectedIndex = -1;
        _taxiFormLog.Info($"Gate list for {_currentIcao} rebuilt from a new source ({token}); previous destination '{previous}' is no longer listed.");
        return true;
    }

    /// <summary>Spoken when the gate list was rebuilt from a new source and the pilot's chosen
    /// destination is no longer in it. Shared by the show path (queued) and the Calculate path
    /// (immediate abort) so the pilot hears the same words for the same event.</summary>
    internal const string GateListUpdatedMessage = "Gate list updated from GSX. Please choose the destination again.";

    /// <summary>The Place list's words when a refresh dropped the pilot's chosen place (usually a
    /// rename: a late OSM name absorbing a synthesized one).</summary>
    internal const string PlaceListUpdatedMessage = "Places updated. Please choose the destination again.";

    /// <summary>
    /// This form is hide-on-close (see <see cref="OnFormClosing"/>), so re-opening it does not
    /// reload the airport (<see cref="LoadAirportDataCoreAsync"/> early-returns for the same
    /// ICAO) and nothing else re-populated the gate list. Becoming visible is therefore the
    /// moment to notice that GSX has published this airport since the list was last built —
    /// the arrival planned during descent, or the spawn before the first handlerData frame.
    /// The refresh is a single string compare when nothing changed. A lost selection is
    /// announced QUEUED, never immediate: it is a background state change, not the echo of
    /// anything the pilot just did, and it must not stomp whatever the screen reader is
    /// speaking about the window that just opened.
    /// <para>
    /// The follow timer (<see cref="OnFollowTimerTick"/>) runs only while the window is shown:
    /// started here when it becomes visible, stopped when it hides. A hidden window has no list
    /// anyone is reading, and its activation catches up when it is shown again.
    /// </para>
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible || IsDisposed)
        {
            _followTimer?.Stop();
            return;
        }
        _timesShown++;
        if (_followTimer == null)
        {
            _followTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _followTimer.Tick += OnFollowTimerTick;
        }
        _followTimer.Start();

        try
        {
            if (RefreshDestinationsIfGateSourceChanged())
                _announcer.Announce(GateListUpdatedMessage);
        }
        catch (Exception ex)
        {
            // Never let a refresh failure break showing the form.
            _taxiFormLog.Error($"Gate-list refresh on show failed: {ex}");
        }
    }

    private void OnDestTypeChanged(object? sender, EventArgs e)
    {
        bool isGate = cmbDestType.SelectedIndex == 1;
        bool isProgressive = cmbDestType.SelectedIndex == 2;
        bool isRunway = cmbDestType.SelectedIndex == 0;
        // A Place resolves onto a stand, so the two stand filters apply to it exactly as they
        // do to the gate list — and a filter the pilot cannot see is a filter they cannot undo.
        bool isPlace = cmbDestType.SelectedIndex == 4;
        chkFitFilter.Visible = (isGate || isPlace) && _aircraftWingspan > 0;
        chkHideOccupied.Visible = (isGate || isPlace) && _tcasService != null;
        lblGateSearch.Visible = isGate;
        txtGateSearch.Visible = isGate;
        if (!isGate)
            txtGateSearch.Text = string.Empty;

        // The Runway entry list applies only to runway destinations. Any type change puts
        // it back at Full length (D12), so a stale intersection or holding point can't leak
        // into a later route; the runway the list below selects rebuilds it for that runway.
        lblRunwayEntry.Visible = isRunway;
        cmbRunwayEntry.Visible = isRunway;
        PopulateRunwayEntries(keepSelection: false);

        // CAT III / LVP hold is runway-only too. Leaving runway mode unticks it so
        // a stale low-visibility preference can't leak into a later runway route.
        chkCatIiiHold.Visible = isRunway;
        if (!isRunway)
            chkCatIiiHold.Checked = false;

        // Full-length backtrack departure is runway-only as well — untick on leaving
        // runway mode so a stale backtrack preference can't leak into a later route.
        chkFullLengthBacktrack.Visible = isRunway;
        if (!isRunway)
            chkFullLengthBacktrack.Checked = false;

        // Progressive Taxi has no final destination — hide the gate/runway
        // destination picker; the leg ends where its steps say. Other destination
        // types restore the picker (mirrors the gate visibility toggling above).
        lblDestination.Visible = !isProgressive;
        cmbDestination.Visible = !isProgressive;

        PopulateDestinations();
        UpdateLayout();

        // Announce "no deicing areas" immediately after populating so the user
        // knows before pressing Calculate that the airport has nothing to route to.
        if (cmbDestType.SelectedIndex == 3 && cmbDestination.Items.Count == 0)
            _announcer.AnnounceImmediate("No deicing areas at this airport.");

        // An empty Place list needs a word: "loading" if a load is running (a background refresh
        // becomes a foreground one now the pilot is looking), else why it is empty. Never for a
        // silent restore, and only with an airport loaded.
        if (isPlace && _graph != null && cmbDestination.Items.Count == 0 && !_suppressPlaceAnnounce)
        {
            if (_placesLoading)
            {
                if (_placesLoadBackground && Visible) _announcer.Announce($"Loading places for {_currentIcao}.");
                _placesLoadBackground = false;
            }
            else
            {
                string? line = DescribePlaceList(_placeCatalog != null, 0, _currentIcao);
                if (line != null) _announcer.AnnounceImmediate(line);
            }
        }

        // Entering gate or Place mode: kick a traffic sweep and rebuild once it lands, so the
        // occupied-stand filter works on the first list rather than only after the pilot
        // has typed something. The sweep is asynchronous (responses arrive over the next
        // few dozen ms), which is why this is a request-then-rebuild rather than a read.
        // Place too: its stands pass through the same filter (SpotIsOccupied).
        if (isGate || isPlace) RefreshTrafficThenRepopulate();

        // Name the default holding point for the runway that is now selected (the list
        // was just rebuilt, which mutes the call-out inside PopulateDestinations).
        if (isRunway) AnnounceDefaultHoldingPoint();
    }

    /// <summary>
    /// Requests a fresh SimConnect traffic sweep and rebuilds the gate list when it has
    /// had time to land. TcasService promotes each arriving aircraft into its snapshot,
    /// so a short delay is enough; no-ops without a traffic service or the filter off.
    /// Fire-and-forget by design — the list is already usable meanwhile.
    /// </summary>
    private async void RefreshTrafficThenRepopulate()
    {
        if (_tcasService == null || !chkHideOccupied.Checked) return;
        try
        {
            _tcasService.PollNow();
            await Task.Delay(600);
            // The pilot may have moved on (closed the form, switched destination type)
            // while the sweep was in flight. "Gate, stand or parking" and Place both filter on it.
            if (IsDisposed || !IsHandleCreated || cmbDestType.SelectedIndex is not (1 or 4)) return;

            // Keep whatever the pilot has selected/typed — PopulateDestinations rebuilds
            // the list and re-selects index 0, which would otherwise silently move the
            // destination out from under them.
            string? selected = cmbDestination.SelectedItem?.ToString();
            PopulateDestinations();
            if (!string.IsNullOrEmpty(selected))
            {
                int idx = cmbDestination.Items.IndexOf(selected);
                if (idx >= 0) SelectDestinationSilently(idx);
            }
        }
        catch (Exception ex)
        {
            Utils.Logging.Log.Debug("TaxiAssist", $"Traffic refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The aircraft parked on the stand at these coordinates, or null. ONE rule, shared
    /// by the destination-list filter and the Calculate-time warning
    /// (<see cref="CheckGateOccupancy"/>) so the list and the warning can never disagree.
    /// Reads TcasService's existing snapshot — never polls; callers that need freshness
    /// request the sweep themselves.
    /// </summary>
    private Models.TcasTraffic? SpotIsOccupied(double lat, double lon) =>
        _tcasService == null ? null : SpotIsOccupied(_tcasService.GetTraffic(onGround: true), lat, lon);

    /// <summary>Same rule against an already-taken traffic snapshot — for callers testing
    /// many stands in one pass (the destination-list filter).</summary>
    private static Models.TcasTraffic? SpotIsOccupied(
        IReadOnlyList<Models.TcasTraffic> groundTraffic, double lat, double lon)
    {
        foreach (var t in groundTraffic)
        {
            if (NavigationCalculator.CalculateDistance(lat, lon, t.Latitude, t.Longitude) <= GATE_OCCUPIED_NM)
                return t;
        }
        return null;
    }

    /// <summary>The entry chosen in the Runway entry list, or null when it holds nothing chosen.</summary>
    private RunwayEntryItem? SelectedRunwayEntry =>
        cmbRunwayEntry.SelectedIndex is int i && i >= 0 && i < _runwayEntries.Count ? _runwayEntries[i] : null;

    /// <summary>Selects Full length (always item 0), without rebuilding the list.</summary>
    private void SelectFullLengthRunwayEntry()
    {
        if (cmbRunwayEntry.Items.Count > 0 && cmbRunwayEntry.SelectedIndex != 0) cmbRunwayEntry.SelectedIndex = 0;
    }

    /// <summary>
    /// Fills the Runway entry list for the selected runway (D10-D12): Full length, then its
    /// intersections and painted holding points down the runway. What the refill does is
    /// <see cref="RunwayEntryList.Refill"/>'s decision: with <paramref name="keepSelection"/> (the
    /// list getting focus or dropping down) the choice is kept; a choice no longer offered falls
    /// back to Full length and is said once, as a background change the pilot did not make.
    /// Without it (a runway picked, a type change, an airport load) the list starts at Full length.
    /// The same lines leave the combo alone. Builds through
    /// <see cref="ResolveHoldingPointsForRunway"/>, which can insert graph nodes: UI thread only
    /// [RTE-30].
    /// </summary>
    private void PopulateRunwayEntries(bool keepSelection)
    {
        var rebuilt = BuildRunwayEntries();
        var refill = RunwayEntryList.Refill(_runwayEntries, cmbRunwayEntry.SelectedIndex, rebuilt, keepSelection);

        // The combo shows _runwayEntries' labels, in order; the fresh items always replace the old
        // ones, behind the same lines when the labels did not change.
        _runwayEntries = rebuilt;
        if (refill.Rebuild)
        {
            cmbRunwayEntry.BeginUpdate();
            try
            {
                cmbRunwayEntry.Items.Clear();
                foreach (var item in rebuilt) cmbRunwayEntry.Items.Add(item.Label);
            }
            finally { cmbRunwayEntry.EndUpdate(); }
        }

        if (refill.SelectIndex >= 0 && cmbRunwayEntry.SelectedIndex != refill.SelectIndex)
            cmbRunwayEntry.SelectedIndex = refill.SelectIndex;

        // Queued: it follows the screen reader's reading of the list rather than cutting it.
        if (refill.Speech != null) _announcer.Announce(refill.Speech);
    }

    /// <summary>
    /// The Runway entry list's items for the selected runway: the taxiways that meet it partway
    /// (<see cref="TaxiGraph.GetRunwayIntersections"/>, the normal full-length entrance left out)
    /// and its painted holding points (<see cref="ResolveHoldingPointsForRunway"/>, full-length
    /// stubs included: choosing between two by their painted names, LSZH A2 vs A1, is the point),
    /// merged by <see cref="RunwayEntryList.Build"/>. Full length alone with no graph, no runway
    /// destination or nothing to offer.
    /// </summary>
    private IReadOnlyList<RunwayEntryItem> BuildRunwayEntries()
    {
        static string Distance(double metres) => DistanceFormatter.FromMetres(metres);
        var fullLengthOnly = RunwayEntryList.Build(
            Array.Empty<TaxiGraph.RunwayIntersection>(),
            Array.Empty<(TaxiGraph.RunwayIntersection, string, bool)>(), 0, 0, 0, 0, Distance);

        if (_graph == null || cmbDestType.SelectedIndex != 0) return fullLengthOnly;
        string? destName = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destName)) return fullLengthOnly;

        // "Runway 22R (backtrack required)" → "22R". destName itself stays the full
        // combo label — the _destination*Map lookups below are keyed on it.
        string cleanName = StripBacktrackSuffix(destName);
        string runwayId = cleanName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? cleanName.Substring(7).Trim()
            : cleanName.Trim();

        // Physical runway geometry (runway_end thresholds), NOT the start-table
        // centerline — the start table sits inside the pavement at displaced
        // thresholds, which would understate the runway length / remaining. The
        // Runway model gives the departure threshold (StartLat/Lon), the far end
        // (EndLat/Lon), and the width, so remaining reflects the true runway, and the
        // list is ordered down this one centreline.
        if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
        {
            _cachedRunways = _dataProvider.GetRunways(_currentIcao);
            _cachedRunwaysIcao = _currentIcao;
        }
        var rwy = _cachedRunways
            .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
        if (rwy == null) return fullLengthOnly;

        // Half-width from the runway width (feet → metres); default 150 ft wide.
        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

        // The start-table lineup point (same one full-length departures line up
        // at) lets the enumeration drop the normal full-length entrance — only
        // genuine shortcuts past it are offered (displaced-threshold fix).
        double? lineupLat = null, lineupLon = null;
        if (_destinationThresholdMap.TryGetValue(destName, out var lineup))
        {
            lineupLat = lineup.lat;
            lineupLon = lineup.lon;
        }

        // The aircraft's side of the runway decides where an angled entry reaches the pavement
        // (GetRunwayIntersections); (0,0) = no position yet, and the list is as before.
        bool haveOwnPos = _aircraftLat != 0 || _aircraftLon != 0;
        var intersections = _graph.GetRunwayIntersections(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
            lineupLat, lineupLon,
            haveOwnPos ? _aircraftLat : null, haveOwnPos ? _aircraftLon : null);

        // A set-back hold keeps its kind in its label ("A3 (holding point, ILS hold)"), so a pair
        // at one entrance (EGKK 26L A1/A3, the same entrance and distances) stays tellable apart.
        var holdingPoints = ResolveHoldingPointsForRunway(destName);

        return RunwayEntryList.Build(
            intersections, holdingPoints, rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, Distance);
    }

    /// <summary>
    /// Resolves this airport's PAINTED holding points onto the entries of the runway
    /// named by <paramref name="destName"/> ("Runway 26L"), carrying each point's OSM
    /// <c>holding_position:type</c> kind and whether its entry sits meaningfully past
    /// the full-length lineup point (Partway ⇒ intersection-style lineup semantics).
    ///
    /// Shared by the Runway entry list (<see cref="BuildRunwayEntries"/>) and the default-hold
    /// call-out (<see cref="AnnounceDefaultHoldingPoint"/>) so the two can never
    /// describe the runway differently. Empty when augmentation is off/uncached, the
    /// destination is not a runway, or nothing resolves onto it.
    /// </summary>
    private List<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)>
        ResolveHoldingPointsForRunway(string? destName)
    {
        var resolved = new List<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)>();

        if (_graph == null || cmbDestType.SelectedIndex != 0) return resolved;
        if (string.IsNullOrEmpty(destName)) return resolved;

        var augProvider = _dataProvider as MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider;
        if (augProvider == null) return resolved;
        // The KIND-carrying reader: the kind tag is what distinguishes the normal
        // runway hold from the set-back CAT II/III line of the same pair (EGKK M1/M3),
        // which is exactly the choice the call-out below has to make.
        var namedPoints = augProvider.GetNamedHoldingPoints(_currentIcao);
        if (namedPoints.Count == 0) return resolved;

        var kindByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _, _, kind) in namedPoints)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            string key = name.Trim();
            // First non-empty tag wins: a name can be painted twice (two lines of the
            // same designator) and only one of them may carry the type tag.
            if (!kindByName.TryGetValue(key, out var existing) || string.IsNullOrEmpty(existing))
                kindByName[key] = kind?.Trim() ?? "";
        }

        var holdingPoints = namedPoints
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => (Name: p.Name.Trim(), Lat: p.Lat, Lon: p.Lon))
            .ToList();
        if (holdingPoints.Count == 0) return resolved;

        // "Runway 28 (backtrack required)" → "28". destName itself stays the full
        // combo label — the _destination*Map lookups below are keyed on it.
        string cleanName = StripBacktrackSuffix(destName);
        string runwayId = cleanName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? cleanName.Substring(7).Trim()
            : cleanName.Trim();

        if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
        {
            _cachedRunways = _dataProvider.GetRunways(_currentIcao);
            _cachedRunwaysIcao = _currentIcao;
        }
        var rwy = _cachedRunways
            .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
        if (rwy == null) return resolved;

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

        // Measure against the OUTER ENVELOPE of the pavement edges (Runway.Start/End,
        // i.e. runway_end) and the start-table departure lineup points, because navdata
        // disagrees with itself in BOTH directions and either source alone loses entries.
        // EGKK 26L departs 406 m beyond its runway_end, so the edge anchor hid the whole
        // A/M holding area; EGLL 09R and EHAM 36C put the start row 45 m and 480 m INTO
        // the runway, so the lineup anchor dropped the full-length holds behind it (that
        // cost EGLL 09R its N1/NB1 and EHAM 36C its CAT III when measured). See
        // TaxiGraph.ChooseHoldingPointExtent.
        //
        // The far lineup point is found BY DESIGNATOR (26L → 08R) rather than by
        // proximity: at EGKK the 08L lineup point is 359 m from 08R's pavement edge while
        // 08R's own is 442 m, so a nearest-point search picks the wrong runway's far end
        // and skews the whole line. Absent (e.g. the opposite end is closed, so
        // PopulateDestinations never listed it), the pavement edge stands in and the
        // envelope degrades to the edge.
        double lineupLat = rwy.StartLat, lineupLon = rwy.StartLon;
        if (_destinationThresholdMap.TryGetValue(destName, out var lineup))
        {
            lineupLat = lineup.lat;
            lineupLon = lineup.lon;
        }

        double farLineupLat = rwy.EndLat, farLineupLon = rwy.EndLon;
        string? reciprocal = TaxiGraph.ReciprocalRunwayName(rwy.RunwayID);
        // The reciprocal's combo label may carry "(backtrack required)" — the map keys on the label.
        if (reciprocal != null &&
            (_destinationThresholdMap.TryGetValue($"Runway {reciprocal}", out var farLineup)
             || _destinationThresholdMap.TryGetValue($"Runway {reciprocal}{BacktrackRequiredSuffix}", out farLineup)))
        {
            farLineupLat = farLineup.lat;
            farLineupLon = farLineup.lon;
        }

        var (thrLat, thrLon, farLat, farLon, lineupAlong) = TaxiGraph.ChooseHoldingPointExtent(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon,
            lineupLat, lineupLon, farLineupLat, farLineupLon);

        // Entries meaningfully past the lineup point get intersection-style lineup
        // semantics (Partway); entries at/before it are the normal full-length
        // departure via the chosen stub.
        const double FULL_LENGTH_MARGIN_M = 50.0;

        foreach (var entry in _graph.ResolveHoldingPointEntries(
                     holdingPoints,
                     thrLat, thrLon, farLat, farLon, halfWidthM,
                     _aircraftLat, _aircraftLon,
                     // Behind-threshold holds (EGCC 23L VB1/T1) are admitted only when
                     // OSM tags them as GUARDING a runway — the "runway" line or its
                     // set-back "ils" CAT II/III twin. "intermediate" queue-ladder
                     // holds (EGCC V1–V6) and untagged points stay out. So does the
                     // "NO ENTRY" ref EHAM's mappers put on no-entry bars — a prose
                     // safety marking, not a holding-point designator ATC would name.
                     behindThresholdEligible: name =>
                         !string.Equals(name.Replace(" ", "").Replace("-", ""), "NOENTRY",
                             StringComparison.OrdinalIgnoreCase) &&
                         kindByName.TryGetValue(name, out var k) &&
                         (string.Equals(k, "runway", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(k, "ils", StringComparison.OrdinalIgnoreCase)),
                     runwayName: rwy.RunwayID))
        {
            bool partway = entry.AlongMetersFromThreshold > lineupAlong + FULL_LENGTH_MARGIN_M;
            kindByName.TryGetValue(entry.TaxiwayName, out var kind);
            resolved.Add((entry, kind ?? "", partway));
        }

        return resolved;
    }

    /// <summary>
    /// The TAXIWAY a plain full-length departure enters the named runway by ("K16"), from
    /// NAVDATA alone — the fallback for the very common airport whose OSM holding points
    /// carry no ref, where there is no painted name to speak but the entrance is named.
    ///
    /// Measured against the PHYSICAL runway (runway_end pavement edges) like the
    /// intersection list, not ChooseHoldingPointExtent's envelope: the answer is relative
    /// to the LINEUP POINT, so the extent only has to contain the runway, and a starter
    /// extension's negative along-track is handled by FindFullLengthEntrance itself.
    ///
    /// Null — and therefore silent — when no named taxiway meets the runway near the
    /// lineup point. Never substitute the nearest named taxiway at any distance: at OMDB
    /// 30L that would be M18, 695 m down a 3,606 m runway, and calling an intersection
    /// departure "full length" is the kind of confidently-wrong answer this app must not
    /// give a pilot who cannot see the sign.
    /// </summary>
    private string? ResolveFullLengthEntranceName(string? destName)
    {
        if (_graph == null || cmbDestType.SelectedIndex != 0) return null;
        if (string.IsNullOrEmpty(destName)) return null;

        // Without a start-table lineup point there is no reference for "full length".
        if (!_destinationThresholdMap.TryGetValue(destName, out var lineup)) return null;

        string cleanName = StripBacktrackSuffix(destName);
        string runwayId = cleanName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? cleanName.Substring(7).Trim()
            : cleanName.Trim();

        if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
        {
            _cachedRunways = _dataProvider.GetRunways(_currentIcao);
            _cachedRunwaysIcao = _currentIcao;
        }
        var rwy = _cachedRunways
            .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
        if (rwy == null) return null;

        double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;

        var entrance = _graph.FindFullLengthEntrance(
            rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
            lineup.lat, lineup.lon);

        return string.IsNullOrWhiteSpace(entrance?.TaxiwayName) ? null : entrance!.TaxiwayName;
    }

    /// <summary>
    /// Speaks (and writes to the status line) WHICH painted holding point a plain
    /// runway departure will use, so the pilot knows before they calculate whether the
    /// default matches their clearance — and therefore whether they need to choose a
    /// different entry in the Runway entry list. At EGKK 26L the default is M1 on the
    /// starter extension, while a "line up via A1" clearance means the A entrance 112 m
    /// further down.
    ///
    /// Named from the SAME resolution the list uses, so the two can never disagree.
    /// This is an INDICATION of the full-length entry, not a promise about the exact
    /// stop point: hold-short placement on the route stays navdata-authoritative
    /// (TruncateToHoldShort). Silent when the airport has no online holding-point data,
    /// when an entry other than Full length is chosen (D14), and on the repeat of an
    /// already-spoken answer (the combo re-selects itself on every list rebuild).
    /// </summary>
    /// <param name="statusPrefix">When given, the status line keeps this text and the
    /// holding point is appended to it — used by the airport-load path, whose own
    /// "N nodes, M paths" line is the only confirmation the airport loaded.</param>
    private void AnnounceDefaultHoldingPoint(string? statusPrefix = null)
    {
        if (_suppressHoldingPointAnnounce) return;
        if (cmbDestType.SelectedIndex != 0) return;
        // The pilot has chosen an entry themselves (holding point / intersection) — that
        // choice is the answer and the screen reader already read it out.
        if (SelectedRunwayEntry is { Kind: not RunwayEntryKind.FullLength }) return;

        string? destName = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destName)) return;

        bool lvp = chkCatIiiHold.Checked;
        string key = $"{_currentIcao}|{destName}|{(lvp ? "lvp" : "full")}";
        if (key == _lastHoldingPointAnnounceKey) return;
        _lastHoldingPointAnnounceKey = key;

        // Full-length entries only: a partway entry is an intersection departure, which
        // this (Full length) route never takes.
        var fullLength = ResolveHoldingPointsForRunway(destName)
            .Where(r => !r.Partway)
            .ToList();
        if (fullLength.Count == 0)
        {
            // No PAINTED name to speak — but the ENTRANCE still has one in navdata, and
            // that is what the pilot actually wanted to know. OMDB publishes 235 OSM
            // holding_position nodes and not one carries a ref, so the whole airport was
            // silent here while every runway had a perfectly good named entrance (30R via
            // N9, 30L via K16). Navdata-only, so it works with augmentation off as well.
            string? entrance = ResolveFullLengthEntranceName(destName);
            if (entrance == null) return;

            // The LVP tick can't be honoured without painted names — say so rather than
            // letting the pilot hear a full-length entrance as if it answered their
            // CAT III question.
            string entryMsg = lvp
                ? $"CAT three hold not named in online data. Full length entry via {entrance}."
                : $"Full length entry via {entrance}.";

            lblStatus.Text = statusPrefix == null
                ? $"{destName}: {entryMsg}"
                : $"{statusPrefix} {destName}: {entryMsg}";
            _announcer.Announce(entryMsg);
            return;
        }

        // Default (no LVP): the line closest to the runway — the normal clearance, and
        // the one OSM tags holding_position:type=runway (EGKK M1 of the M1/M3 pair).
        var normal = fullLength.FirstOrDefault(
            r => string.Equals(r.Kind.Trim(), "runway", StringComparison.OrdinalIgnoreCase));
        if (normal.Entry == null) normal = fullLength[0];

        string message;
        if (!lvp)
        {
            message = $"Full length holding point {normal.Entry.TaxiwayName}.";
        }
        else
        {
            // LVP: the set-back CAT II/III line of the same entrance — same entry node,
            // different painted line (EGKK M3 behind M1). Never name the normal line as
            // if it were the CAT III one; say so instead when the pair isn't published.
            var catIii = fullLength.FirstOrDefault(
                r => r.Entry.NodeId == normal.Entry.NodeId
                     && !string.Equals(r.Entry.TaxiwayName, normal.Entry.TaxiwayName,
                                       StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(r.Kind.Trim(), "runway", StringComparison.OrdinalIgnoreCase));

            message = catIii.Entry != null
                ? $"CAT three hold {catIii.Entry.TaxiwayName}."
                : $"CAT three hold not named in online data. Full length holding point {normal.Entry.TaxiwayName}.";
        }

        lblStatus.Text = statusPrefix == null
            ? $"{destName}: {message}"
            : $"{statusPrefix} {destName}: {message}";
        // Queued, never AnnounceImmediate: the screen reader is already speaking the
        // combo item the pilot just landed on, and this follows it rather than cutting
        // across it.
        _announcer.Announce(message);
    }

    // ---- The step block -------------------------------------------------------------------

    /// <summary>The kind chosen in the step kind list.</summary>
    private ClearanceStepKind SelectedStepKind() => ClearanceStepEntry.KindAt(cmbStepKind.SelectedIndex);

    /// <summary>Sets the kind without the kind list's handler refilling the name box.</summary>
    private void SetStepKindSilently(ClearanceStepKind kind)
    {
        int index = (int)kind;
        if (cmbStepKind.SelectedIndex == index) return;
        _settingStepKind = true;
        try { cmbStepKind.SelectedIndex = index; }
        finally { _settingStepKind = false; }
    }

    /// <summary>
    /// Picks the step kind (today only the access keys call it, through <c>ProcessMnemonic</c>), refills the name box
    /// for it and, with <paramref name="focusName"/>, puts focus there [CORE-7]. From anywhere else focus moves to the
    /// name box with Select(), the way a label's access key moves it, and the screen reader names the box, so the
    /// window says nothing. When focus is already in the name box nothing moves and the reader says nothing (it
    /// keeps only the last focus event of a quick burst and ignores one for the object that has focus), so the window
    /// speaks the box's new name once, at once, dropping any step sentence still waiting.
    /// </summary>
    private void SelectStepKind(ClearanceStepKind kind, bool focusName)
    {
        bool alreadyInNameBox = cmbStepName.Focused;
        SetStepKindSilently(kind);
        RefreshStepNameList();
        if (!focusName) return;
        string? readback = ClearanceStepEntry.KindKeyReadback(alreadyInNameBox, cmbStepName.AccessibleName);
        if (readback != null) SpeakStepBlockNow(readback);
        else cmbStepName.Select();
    }

    /// <summary>The taxiway of the last Via, Left onto or Right onto step the name box lists for
    /// (<see cref="StepsBeforeEntry"/>), or null.</summary>
    private string? LastViaStepName()
    {
        var before = StepsBeforeEntry();
        for (int i = before.Count - 1; i >= 0; i--)
            if (before[i].IsVia) return before[i].Name;
        return null;
    }

    /// <summary>The steps the name box is listed for: the whole list, or, while a step is being changed, only the steps
    /// before it (its step above and its last Via), so the change is offered what was offered when it was entered.</summary>
    private IReadOnlyList<ClearanceStep> StepsBeforeEntry()
        => _changingStepIndex >= 0 ? _steps.GetRange(0, Math.Min(_changingStepIndex, _steps.Count)) : _steps;

    /// <summary>
    /// Fills the name box for the chosen kind from <see cref="ClearanceNameLists"/>, likely names first, and sets its
    /// label and accessible name. Nothing is selected, so Enter never adds a name the pilot did not choose, except on
    /// an empty list for a Via kind: the nearest taxiway in the direction the aircraft faces, as the first-taxiway box
    /// did. <paramref name="keepSelection"/> keeps the pick by its label and kind when it is still listed (a late
    /// holding-point fetch). A holding point read from a sign is listed for every destination type ([HLD-18] since
    /// 2026-11-24). While a step is being changed the names are those offered after the steps before it
    /// (<see cref="StepsBeforeEntry"/>), the accessible name says which step, and nothing is preselected. Refreshes the
    /// "At" box after, keeping its pick with <paramref name="keepSelection"/>: a late holding-point fetch as focus
    /// reaches the name box (Shift+Tab back from "At") never drops "hold short of runway 27L at B".
    /// <paramref name="preselect"/> false never makes the nearest-taxiway pick: the list following guidance
    /// (<see cref="FollowGuidance"/>) refills the box in the background, and a background refill never picks a name the
    /// pilot did not choose.
    /// </summary>
    private void RefreshStepNameList(bool keepSelection = false, bool preselect = true)
    {
        var kind = SelectedStepKind();
        lblStepName.Text = ClearanceStepEntry.NameBoxLabel(kind);
        cmbStepName.AccessibleName = _changingStepIndex >= 0
            ? ClearanceStepEntry.NameBoxAccessibleName(kind, _changingStepIndex + 1)
            : ClearanceStepEntry.NameBoxAccessibleName(kind);
        cmbStepName.AccessibleDescription = ClearanceStepEntry.NameBoxDescription(kind);

        var previous = keepSelection ? cmbStepName.SelectedItem as ClearanceNameLists.Item : null;
        _fillingStepName = true;
        cmbStepName.BeginUpdate();
        try
        {
            cmbStepName.Items.Clear();
            foreach (var item in StepNameItems(kind))
                cmbStepName.Items.Add(item);

            int select = -1;
            if (previous != null)
                select = IndexOfStepName(i => i.Kind == previous.Kind
                    && string.Equals(i.Label, previous.Label, StringComparison.OrdinalIgnoreCase));
            if (select < 0 && previous == null && preselect && _changingStepIndex < 0 && StepsBeforeEntry().Count == 0 && _graph != null
                && kind is ClearanceStepKind.Via or ClearanceStepKind.LeftOnto or ClearanceStepKind.RightOnto
                && _graph.GetClosestTaxiwayInDirection(_aircraftLat, _aircraftLon, AircraftHeadingTrue()) is string closest)
                select = IndexOfStepName(i => string.Equals(i.Name, closest, StringComparison.Ordinal));
            cmbStepName.SelectedIndex = select;
        }
        finally
        {
            cmbStepName.EndUpdate();
            _fillingStepName = false;
        }
        RefreshStepAtList(keepSelection);
    }

    private int IndexOfStepName(Func<ClearanceNameLists.Item, bool> matches)
    {
        for (int i = 0; i < cmbStepName.Items.Count; i++)
            if (cmbStepName.Items[i] is ClearanceNameLists.Item item && matches(item)) return i;
        return -1;
    }

    /// <summary>The name box's items for a kind (TRUE heading for the nearest-first list: the graph's bearings are
    /// true; see AircraftHeadingTrue). Empty with no airport loaded.</summary>
    private List<ClearanceNameLists.Item> StepNameItems(ClearanceStepKind kind)
    {
        if (_graph == null) return new List<ClearanceNameLists.Item>();
        var graph = _graph;
        var all = graph.GetAllTaxiwayNames();
        var before = StepsBeforeEntry();
        string? lastVia = LastViaStepName();
        // The taxiways the last Via taxiway meets (within two named-taxiway crossings), nearest the aircraft first;
        // the rest of the airport follows them alphabetically in ClearanceNameLists, so a clearance the heuristic
        // misses can still be entered.
        IReadOnlyList<string> connected = lastVia == null
            ? Array.Empty<string>()
            : SortTaxiwaysByDistance(graph.GetConnectedTaxiwayNames(lastVia));

        // The taxiways crossing each runway, once per refill (the runways the last taxiway crosses come first).
        var crossers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> TaxiwaysCrossing(string runwayId)
        {
            if (crossers.TryGetValue(runwayId, out var known)) return known;
            IReadOnlyList<string> found = _crossRunwayMap.TryGetValue($"Runway {runwayId}", out var runway)
                ? GetTaxiwaysCrossingRunway(runway)
                : Array.Empty<string>();
            crossers[runwayId] = found;
            return found;
        }

        switch (kind)
        {
            case ClearanceStepKind.Cross:
                return ClearanceNameLists.CrossTargets(_airportRunwayIds, TaxiwaysCrossing, connected, all, before);
            case ClearanceStepKind.HoldShort:
                if (!_namedHoldingPointsResolved)
                    ResolveNamedHoldingPoints();
                return ClearanceNameLists.HoldTargets(_airportRunwayIds, TaxiwaysCrossing, all, _namedHoldingPoints, before);
            default:
                IReadOnlyList<string> nearestFirst = lastVia == null
                    ? graph.GetTaxiwayNamesSortedByDistance(_aircraftLat, _aircraftLon, AircraftHeadingTrue())
                    : Array.Empty<string>();
                return ClearanceNameLists.Taxiways(nearestFirst, connected, all, before);
        }
    }

    /// <summary>
    /// Shows the "At" box only for a Cross or Hold short of step with a runway chosen
    /// (<see cref="ClearanceStepEntry.ShowsAtBox"/>), its description for that kind, and fills it the same way for both:
    /// "(none)" (where the route first meets the runway), then the taxiways that cross that runway, then the painted
    /// holding points from which it can be crossed on the aircraft's side (never one read from a sign, [HLD-18]). Hidden,
    /// it holds nothing, and <see cref="StepFromBoxes"/> reads no "at" from it. While a runway crossing or hold short is
    /// being changed with its own runway chosen, its own place follows when none of those names it
    /// (<see cref="ClearanceStepEntry.OwnPlaceLabel"/>): a crossing made at a holding point now behind the aircraft keeps
    /// its place and pin. <paramref name="keepSelection"/> (the box's own refresh when focus arrives or its list opens)
    /// keeps the pick by its label when it is still offered. Otherwise, and when that pick was the step's own place under
    /// a label since changed, the crossing being changed gets its own place back with its own runway chosen (arrowing the
    /// name box away and back, a re-pick after a reload); a new runway starts at "(none)"
    /// (<see cref="ClearanceStepEntry.AtIndexAfterRefill"/>).
    /// </summary>
    private void RefreshStepAtList(bool keepSelection = false)
    {
        string? previous = keepSelection ? cmbStepAt.SelectedItem?.ToString() : null;
        string? previousOwnLabel = _changingOwnAtLabel;
        Runway? runway = null;
        var kind = SelectedStepKind();
        var picked = cmbStepName.SelectedItem as ClearanceNameLists.Item;
        string? pickedRunway = picked?.Name;
        bool show = ClearanceStepEntry.ShowsAtBox(kind, picked?.Kind)
            && _crossRunwayMap.TryGetValue($"Runway {picked!.Name}", out runway);
        if (show) cmbStepAt.AccessibleDescription = ClearanceStepEntry.AtBoxDescription(kind);

        cmbStepAt.BeginUpdate();
        try
        {
            cmbStepAt.Items.Clear();
            _crossAtHoldPoints.Clear();
            _changingOwnAtLabel = null;
            if (show && runway != null)
            {
                cmbStepAt.Items.Add(ClearanceStepEntry.NoAtPlace);
                foreach (string taxiway in GetTaxiwaysCrossingRunway(runway))
                    cmbStepAt.Items.Add(taxiway);

                // After the taxiways, so first-letter type-ahead lands on a taxiway first.
                if (!_namedHoldingPointsResolved)
                    ResolveNamedHoldingPoints();
                foreach (var (hp, _) in GetHoldingPointsCrossingRunway(runway))
                {
                    string label = hp.Name + CrossAtHoldPointSuffix;
                    if (_crossAtHoldPoints.TryAdd(label, hp))
                        cmbStepAt.Items.Add(label);
                }

                // The place of the crossing being changed, when nothing above names it (judged on the items as listed).
                ClearanceStep? changing = null;
                int ownPlace = -1;
                if (_changingStepIndex >= 0 && _changingStepIndex < _steps.Count)
                {
                    changing = _steps[_changingStepIndex];
                    string? ownLabel = ClearanceStepEntry.OwnPlaceLabel(changing, pickedRunway, placeOffered: IndexOfStepAtFor(changing) >= 0,
                        ClearanceStepEntry.PlaceIsHoldingPoint(changing, _graph?.GetAllTaxiwayNames() ?? new List<string>(),
                            _namedHoldingPoints.Select(p => p.Name)));
                    if (ownLabel != null && !cmbStepAt.Items.Contains(ownLabel))
                    {
                        cmbStepAt.Items.Add(ownLabel);
                        _changingOwnAtLabel = ownLabel;
                    }
                    // Read by AtIndexAfterRefill only with the step's own runway chosen.
                    ownPlace = IndexOfStepAtFor(changing);
                }
                var labels = cmbStepAt.Items.Cast<object>().Select(item => item?.ToString() ?? "").ToList();
                cmbStepAt.SelectedIndex = ClearanceStepEntry.AtIndexAfterRefill(labels,
                    previous, previousOwnLabel, changing, pickedRunway, ownPlace);
            }
        }
        finally { cmbStepAt.EndUpdate(); }

        if (show == _stepAtShown) return;
        _stepAtShown = show;
        lblStepAt.Visible = show;
        cmbStepAt.Visible = show;
        UpdateLayout();
    }

    /// <summary>
    /// Brings the Hold short of list up to date when focus reaches the name box: the named holding points come from
    /// an online fetch that can land after the list was filled, and arrowing a DropDownList never raises DropDown.
    /// Refilled (keeping the pick) only when they have JUST been found: with online names off, or an airport that has
    /// none, the resolve returns at once and the list is left alone, so focus never loses the pilot's place.
    /// </summary>
    private void EnsureHoldingPointsListed()
    {
        if (_graph == null || _namedHoldingPointsResolved) return;
        ResolveNamedHoldingPoints();
        if (_namedHoldingPointsResolved && SelectedStepKind() == ClearanceStepKind.HoldShort)
            RefreshStepNameList(keepSelection: true);
    }

    /// <summary>Enter in the step kind, name or "At" box adds the step, as Add Step does. Not while a list is
    /// dropped down: there Enter takes the highlighted item first.</summary>
    private void OnStepBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Modifiers != Keys.None) return;
        if (sender is ComboBox { DroppedDown: true }) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        AddStepFromInputs();
    }

    /// <summary>
    /// Add Step: the chosen kind and name (and "At") become a step at the end of the list, spoken once. With no name
    /// chosen it is refused out loud. Then the kind goes back to Via with the list refilled and focus in the name box,
    /// so the next taxiway costs a letter and Enter. With a step being changed it replaces that step instead
    /// (<see cref="ReplaceStepFromInputs"/>).
    /// <para>A runway crossing or hold short added where the list already holds the other for the same runway (either
    /// end, never another runway) replaces that step in place: ATC's later instruction replaces the earlier one (spec
    /// 2026-11-28, <see cref="ClearanceStepEntry.IndexReplacedOnAdd"/>: the first such step, never one naming the
    /// destination runway, never a crossing already made). Said once with what it replaced, "Step 3: cross runway 15R,
    /// replacing hold short of runway 15R." ([CORE-7]: the screen reader cannot say it), queued after the focus move as an
    /// added step's is (<see cref="SpeakStepAdded"/>); the boxes then go back to Via with focus in the name box, as after
    /// an append.</para>
    /// </summary>
    private void AddStepFromInputs()
    {
        if (_changingStepIndex >= 0)
        {
            ReplaceStepFromInputs();
            return;
        }

        var name = cmbStepName.SelectedItem as ClearanceNameLists.Item;
        string? refusal = ClearanceStepEntry.Refusal(name);
        if (refusal != null)
        {
            SpeakStepBlockNow(refusal);
            return;
        }

        var step = StepFromBoxes(name!);

        bool focusMoves = !cmbStepName.Focused;
        int replaced = ClearanceStepEntry.IndexReplacedOnAdd(_steps, step, PlanDestinationRunway(cmbDestType.SelectedIndex == 0));
        if (replaced >= 0)
        {
            var old = _steps[replaced];
            ReplaceStepAt(replaced, step);
            SetStepKindSilently(ClearanceStepKind.Via);
            // No stale pick, as after Add Step: Remove Step with nothing selected takes the last step.
            lstClearance.SelectedIndex = -1;
            RefreshStepNameList();
            SpeakStepAdded(ClearanceStepEntry.ReplacedOnAdd(step, replaced + 1, old), focusMoves);
            cmbStepName.Focus();
            return;
        }

        SetStepKindSilently(ClearanceStepKind.Via);
        AppendStep(step, speak: true, speakAfterFocusMove: focusMoves);
        cmbStepName.Focus();
    }

    /// <summary>The step the boxes make with <paramref name="name"/> chosen: the kind, the name and, for a runway
    /// crossed or held short of, the "At" pick (a holding point by its node), read only while the box is shown. While a
    /// step is being changed, its own place picked in the "At" box keeps that step's place
    /// (<see cref="ClearanceStepEntry.BuildForChange"/>).</summary>
    private ClearanceStep StepFromBoxes(ClearanceNameLists.Item name)
    {
        string? atLabel = _stepAtShown ? cmbStepAt.SelectedItem?.ToString() : null;
        NamedHoldingPoint? atPoint = atLabel != null && _crossAtHoldPoints.TryGetValue(atLabel, out var point) ? point : null;
        return _changingStepIndex >= 0 && _changingStepIndex < _steps.Count
            ? ClearanceStepEntry.BuildForChange(SelectedStepKind(), name, atLabel, atPoint, _steps[_changingStepIndex], _changingOwnAtLabel)
            : ClearanceStepEntry.Build(SelectedStepKind(), name, atLabel, atPoint);
    }

    /// <summary>The step the boxes make for the step being changed, and whether it says what that step says
    /// (<see cref="ClearanceStepEntry.SameAsEntered"/>): Replace keeps the old record then, and Calculate goes ahead. Null
    /// (and not the same) with no name chosen or no change open. The one comparison both use.</summary>
    private (ClearanceStep? Rebuilt, bool Same) RebuildStepBeingChanged()
    {
        if (_changingStepIndex < 0 || _changingStepIndex >= _steps.Count
            || cmbStepName.SelectedItem is not ClearanceNameLists.Item name)
            return (null, false);
        var rebuilt = StepFromBoxes(name);
        return (rebuilt, ClearanceStepEntry.SameAsEntered(_steps[_changingStepIndex], rebuilt));
    }

    /// <summary>Enter on a selected step in the Clearance list changes it in place (<see cref="BeginStepChange"/>);
    /// Delete removes it (<see cref="RemoveStep"/>). With nothing selected both act on the list's focused step, the one
    /// the screen reader named (<see cref="StepActedOn"/>).</summary>
    private void OnClearanceListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Modifiers != Keys.None) return;
        if (e.KeyCode == Keys.Delete)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            RemoveStep();
        }
        else if (e.KeyCode == Keys.Enter && StepActedOn(lstClearance, fromList: true) is int index and >= 0)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            BeginStepChange(index);
        }
    }

    /// <summary>
    /// Starts changing step <paramref name="index"/> in place (Enter on it in the Clearance list): the step boxes are
    /// loaded kind, then name (listed after the steps before it, <see cref="StepsBeforeEntry"/>), then, for a runway
    /// crossed or held short of, "At" ("(none)" when it names no place; its own place, listed for the change, when no
    /// offered item names it, so a made crossing keeps its pin and a put-back one its place); a name no longer offered is
    /// left unselected. Add Step
    /// reads "Replace step N" and focus goes to the name box, whose accessible name says "changing step N", so the screen
    /// reader says it there and nothing is spoken here ([CORE-7]). Enter on another step while one is being changed
    /// starts that one instead, the first kept as it was.
    /// </summary>
    private void BeginStepChange(int index)
    {
        if (index < 0 || index >= _steps.Count) return;
        if (_changingStepIndex >= 0) CancelStepChange(false);
        var step = _steps[index];
        _changingStepIndex = index;
        SetStepKindSilently(step.Kind);
        RefreshStepNameList();
        // The At box is rebuilt once, for the name picked, below.
        _fillingStepName = true;
        try { cmbStepName.SelectedIndex = IndexOfStepNameFor(step); }
        finally { _fillingStepName = false; }
        RefreshStepAtList();
        if (_stepAtShown) cmbStepAt.SelectedIndex = IndexOfStepAtFor(step);
        btnAddStep.Text = ClearanceStepEntry.ReplaceButtonText(index + 1);
        btnAddStep.AccessibleName = ClearanceStepEntry.ReplaceButtonName(index + 1);
        btnAddStep.AccessibleDescription = ReplaceStepDescription;
        cmbStepName.Focus();
    }

    /// <summary>The name box's item for <paramref name="step"/> (<see cref="ClearanceStepEntry.NameItemMatches"/>: by
    /// its target kind and name, a holding point by its node only); -1 when it is no longer offered, so Replace says
    /// "Choose a name first." rather than move the step.</summary>
    private int IndexOfStepNameFor(ClearanceStep step)
        => IndexOfStepName(i => ClearanceStepEntry.NameItemMatches(i, step));

    /// <summary>The "At" box's item for a runway crossing or hold short: "(none)" when the step names no place (a blank
    /// "at" is none); else an item a pick of which rebuilds the same place (<see cref="ClearanceStepEntry.AtItemMatches"/>),
    /// or the step's own place listed for the change (<see cref="_changingOwnAtLabel"/>; only ever set for the step being
    /// changed). -1 when none names it.</summary>
    private int IndexOfStepAtFor(ClearanceStep step)
    {
        if (string.IsNullOrWhiteSpace(step.At)) return cmbStepAt.Items.IndexOf(ClearanceStepEntry.NoAtPlace);
        for (int i = 0; i < cmbStepAt.Items.Count; i++)
        {
            string label = cmbStepAt.Items[i]?.ToString() ?? "";
            if (label == _changingOwnAtLabel) return i;
            _crossAtHoldPoints.TryGetValue(label, out var point);
            if (ClearanceStepEntry.AtItemMatches(label, point, step)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Add Step with a step being changed: refused out loud with no name chosen, as Add Step is. A step that says what
    /// the old one said (<see cref="ClearanceStepEntry.SameAsEntered"/>) keeps the old record and its origin, so a made
    /// crossing's pin or a put-back hold short's place survives; anything else is a new step in the same place
    /// (<see cref="ReplaceStepAt"/>). Read back once with its own number ("Step 3: via D."), then the boxes go back to Via
    /// with focus in the name box, as after Add Step. A change never replaces another step for its runway
    /// (<see cref="ClearanceStepEntry.IndexReplacedOnAdd"/> is Add Step's only).
    /// </summary>
    private void ReplaceStepFromInputs()
    {
        int index = _changingStepIndex;
        if (index >= _steps.Count)
        {
            // Never reached: whatever changes the list closes the change first. Never write a step that has moved.
            CancelStepChange(false);
            return;
        }

        string? refusal = ClearanceStepEntry.Refusal(cmbStepName.SelectedItem as ClearanceNameLists.Item);
        if (refusal != null)
        {
            SpeakStepBlockNow(refusal);
            return;
        }

        var (rebuilt, same) = RebuildStepBeingChanged();
        if (!same && rebuilt != null) ReplaceStepAt(index, rebuilt);

        bool focusMoves = !cmbStepName.Focused;
        EndStepChange();
        SetStepKindSilently(ClearanceStepKind.Via);
        // No stale pick, as after Add Step: Remove Step with nothing selected takes the last step.
        lstClearance.SelectedIndex = -1;
        RefreshStepNameList();
        SpeakStepAdded(_steps[index].Spoken(index + 1), focusMoves);
        cmbStepName.Focus();
    }

    /// <summary>Puts <paramref name="step"/> in place of step <paramref name="index"/>, the ONE write both replacements
    /// make (Replace, <see cref="ReplaceStepFromInputs"/>; a runway step added in place of the other for its runway,
    /// <see cref="AddStepFromInputs"/>): a new step in the same place (origin -1, changed since the last Calculate, so a
    /// trim keeps it where it is), the old step's origin, when it has one, kept out of every later trim so a trim never
    /// brings it back, and the Clearance list's item set to its sentence. Selects and speaks nothing: the callers do.</summary>
    private void ReplaceStepAt(int index, ClearanceStep step)
    {
        if (_stepOrigins[index] >= 0) _removedOrigins.Add(_stepOrigins[index]);
        _steps[index] = step;
        _stepOrigins[index] = -1;   // changed since the last Calculate: kept where it is through a trim
        lstClearance.Items[index] = step.Sentence();
    }

    /// <summary>
    /// Closes the change with the step left as it was (<paramref name="focusTheStep"/>: Escape, focus back on that step
    /// in the Clearance list, which the screen reader reads; else silently, for whatever is about to change the list, the
    /// window hiding, or a Calculate the change would not alter). The boxes go back to Via, listed for the whole list.
    /// </summary>
    private void CancelStepChange(bool focusTheStep)
    {
        if (_changingStepIndex < 0) return;
        int index = _changingStepIndex;
        EndStepChange();
        SetStepKindSilently(ClearanceStepKind.Via);
        RefreshStepNameList();
        if (!focusTheStep) return;
        // Already in the list, the reader would say nothing: focus passes through the button on its way back (a screen
        // reader reads the last of two quick focus moves), as SelectStepKind does for the name box.
        if (lstClearance.Focused) btnAddStep.Focus();
        if (index < lstClearance.Items.Count) lstClearance.SelectedIndex = index;
        lstClearance.Focus();
    }

    /// <summary>No step is being changed: Add Step's own text, name and description back. The caller refills the name
    /// box, which takes its own accessible name back.</summary>
    private void EndStepChange()
    {
        _changingStepIndex = -1;
        _changingOwnAtLabel = null;
        btnAddStep.Text = _addStepText;
        btnAddStep.AccessibleName = _addStepName;
        btnAddStep.AccessibleDescription = _addStepDescription;
    }

    /// <summary>Adds a step at the end of the list and the Clearance list (as its sentence) and refills the name box.
    /// With <paramref name="speak"/>, says it once ("Step 2: cross runway 24L at P."): the pilot added it. The
    /// SayIntentions fill passes false.</summary>
    private void AppendStep(ClearanceStep step, bool speak) => AppendStep(step, speak, speakAfterFocusMove: false);

    private void AppendStep(ClearanceStep step, bool speak, bool speakAfterFocusMove)
    {
        _steps.Add(step);
        _stepOrigins.Add(-1);   // added since the last Calculate
        lstClearance.Items.Add(step.Sentence());
        // A crossing already made that ended the list is completed now that a step follows it (ruling 4): it goes,
        // before the step is numbered.
        DropCompletedCrossings();
        // No stale pick: Remove Step with nothing selected takes the last step, the one just added.
        lstClearance.SelectedIndex = -1;
        RefreshStepNameList();
        if (speak) SpeakStepAdded(step.Spoken(_steps.Count), speakAfterFocusMove);
    }

    /// <summary>
    /// Drops each crossing already made that some step now follows (<see cref="ClearanceTrim.CompletedCrossings"/>): it
    /// said only where the leg ended, and a fresh Calculate must never take it for a crossing (ruling 4). Kept out of
    /// every later trim. Speaks nothing: the step added, or the window opening, is what the pilot hears.
    /// </summary>
    private void DropCompletedCrossings()
    {
        var completed = ClearanceTrim.CompletedCrossings(_steps);
        for (int c = completed.Count - 1; c >= 0; c--)
        {
            int i = completed[c];
            if (_stepOrigins[i] >= 0) _removedOrigins.Add(_stepOrigins[i]);
            _steps.RemoveAt(i);
            _stepOrigins.RemoveAt(i);
            lstClearance.Items.RemoveAt(i);
        }
    }

    /// <summary>
    /// Speaks the added step. With focus staying in the name box it interrupts whatever the screen reader says of the
    /// refilled list. When focus is moving there (Add Step pressed on the button, Enter in the kind or "At" box), the
    /// screen reader first names the box, and a focus change cancels speech already sent: the sentence follows that,
    /// queued, a moment later. Anything the step block says at once, and Calculate, drops a sentence still waiting,
    /// so it is never heard out of order or after the Calculate utterance ([HLD-17], [SI-22]).
    /// </summary>
    private void SpeakStepAdded(string sentence, bool afterFocusMove)
    {
        if (!afterFocusMove)
        {
            SpeakStepBlockNow(sentence);
            return;
        }
        _pendingStepSpeech = sentence;
        if (_stepSpeechTimer == null)
        {
            _stepSpeechTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _stepSpeechTimer.Tick += (s, e) =>
            {
                _stepSpeechTimer!.Stop();
                string? text = _pendingStepSpeech;
                _pendingStepSpeech = null;
                if (!string.IsNullOrEmpty(text)) _announcer.Announce(text);
            };
        }
        _stepSpeechTimer.Stop();
        _stepSpeechTimer.Start();
    }

    /// <summary>Drops a step sentence still waiting to be spoken (see <see cref="SpeakStepAdded"/>).</summary>
    private void CancelPendingStepSpeech()
    {
        _stepSpeechTimer?.Stop();
        _pendingStepSpeech = null;
    }

    /// <summary>Speaks at once for the step block (a step added, a refusal, a removal), dropping any step sentence
    /// still waiting, so the two are never heard out of order.</summary>
    private void SpeakStepBlockNow(string text)
    {
        CancelPendingStepSpeech();
        _announcer.AnnounceImmediate(text);
    }

    /// <summary>Remove Step (the button or Alt+M, or Delete in the Clearance list): removes the selected step; with none
    /// selected, from inside the list the step the list's focus is on, the one the screen reader named
    /// (<see cref="StepActedOn"/>), and from anywhere else the last one; refused out loud when the list is empty. From
    /// the Clearance list it speaks nothing while a step remains (the screen reader reads the step now in its place);
    /// from anywhere else, where the button's access key clicks it without moving focus and the reader says nothing of
    /// the change, it says once which step went: "Removed step 3: cross runway 24L."; and a removal that leaves the list
    /// empty says "Steps cleared." whatever the focus, as an empty list gives the reader nothing to say ([CORE-7],
    /// <see cref="ClearanceStepEntry.RemovalSentence"/>). A step of the leg calculated stays out of every later trim. A
    /// step being changed is kept as it was first, silently (the removal is what the pilot hears).</summary>
    private void RemoveStep()
    {
        CancelStepChange(false);
        if (_steps.Count == 0)
        {
            SpeakStepBlockNow(ClearanceStepEntry.NoStepToRemove);
            return;
        }
        bool fromList = lstClearance.Focused;
        int acted = StepActedOn(lstClearance, fromList);
        int index = acted >= 0 && acted < _steps.Count ? acted : _steps.Count - 1;
        var removed = _steps[index];
        if (_stepOrigins[index] >= 0) _removedOrigins.Add(_stepOrigins[index]);
        _steps.RemoveAt(index);
        _stepOrigins.RemoveAt(index);
        lstClearance.Items.RemoveAt(index);
        // Deleting from the list keeps the pilot's place there, on the step that took its place; from the button,
        // nothing stays selected, so the next Remove Step takes the last step again.
        lstClearance.SelectedIndex = fromList && lstClearance.Items.Count > 0
            ? Math.Min(index, lstClearance.Items.Count - 1)
            : -1;
        RefreshStepNameList();
        string? sentence = ClearanceStepEntry.RemovalSentence(removed, index + 1, fromList, listNowEmpty: _steps.Count == 0);
        if (sentence != null) SpeakStepBlockNow(sentence);
    }

    /// <summary>Clear Steps (the button or Alt+Z): empties the list and says so once: "Steps cleared." Alt+Z clicks the
    /// button from anywhere without moving focus, a key away from Alt+X, and from inside the Clearance list an empty
    /// list gives the screen reader nothing to say, so it is said whatever the focus ([CORE-7]).</summary>
    private void OnClearStepsClicked()
    {
        ClearSteps();
        SpeakStepBlockNow(ClearanceStepEntry.StepsCleared);
    }

    /// <summary>Empties the step list (Clear Steps, a SayIntentions fill, an airport change, Stop Guidance, the end of
    /// the leg it was calculated for), the kind back on Via and the name box refilled; the list then holds no leg being
    /// flown (<see cref="_calculatedSteps"/>), and a step sentence still waiting is dropped, as it names a step that is
    /// gone; a step being changed goes with the rest, its change closed first. Speaks nothing: Clear Steps speaks for
    /// itself (<see cref="OnClearStepsClicked"/>).</summary>
    private void ClearSteps()
    {
        CancelStepChange(false);
        CancelPendingStepSpeech();
        _steps.Clear();
        _stepOrigins.Clear();
        _removedOrigins.Clear();
        _calculatedSteps = null;
        lstClearance.Items.Clear();
        SetStepKindSilently(ClearanceStepKind.Via);
        RefreshStepNameList();
    }

    // ---- The list follows guidance: focus on opening, emptying, trimming, the Hold short key (Task 7) ----------

    /// <summary>Where focus lands when the window opens (spec 8.2).</summary>
    internal enum OpeningFocusTarget { Airport, StepKind, DestinationType }

    /// <summary>
    /// The Airport box the first time the window is shown; after that the step kind list when a route is live (the pilot
    /// adds the new instruction), else the destination type, so the pilot always hears whether a destination is set.
    /// </summary>
    internal static OpeningFocusTarget OpeningFocus(bool firstShow, bool routeLive)
        => firstShow ? OpeningFocusTarget.Airport
            : routeLive ? OpeningFocusTarget.StepKind
            : OpeningFocusTarget.DestinationType;

    /// <summary>The guidance states that end the leg a step list was calculated for: the aircraft arrives, guidance
    /// stops, or a leg with no destination ends (spec 8.2); and a landing begins (LandingRollout): guidance stops at
    /// departure only through take-off assist, so without it a departure list survived the flight, and a departure
    /// "Cross runway 22R" cleared 22R on the taxi-in at the same airport.</summary>
    internal static bool EndsTheLeg(TaxiGuidanceState state)
        => state is TaxiGuidanceState.Arrived or TaxiGuidanceState.Inactive or TaxiGuidanceState.ProgressiveHold
            or TaxiGuidanceState.LandingRollout;

    /// <summary>Whether a leg's end keeps the steps typed or changed since the last Calculate (spec 2026-11-28): only a
    /// Progressive leg's (<see cref="TaxiGuidanceState.ProgressiveHold"/>), where the pilot types the next leg's
    /// instruction while stopped at the end of this one. Every other leg end empties the list (<see cref="EmptyStepsAfterLeg"/>).</summary>
    internal static bool LegEndKeepsTypedSteps(TaxiGuidanceState state) => state == TaxiGuidanceState.ProgressiveHold;

    /// <summary>
    /// Whether a leg end queued with <paramref name="generationQueued"/> empties the list now: only when no Calculate
    /// has built a list since (<paramref name="generationNow"/> unchanged), and only when the list holds the leg it was
    /// calculated for (<paramref name="listHoldsCalculatedLeg"/>). Steps entered after the last leg ended, or before any
    /// Calculate (a taxi-in clearance typed during the landing rollout, after the landing emptied the departure's list),
    /// are for the leg to come and stay.
    /// </summary>
    internal static bool LegEndEmptiesList(int generationQueued, int generationNow, bool listHoldsCalculatedLeg)
        => generationQueued == generationNow && listHoldsCalculatedLeg;

    /// <summary>
    /// Called by MainForm BEFORE <c>Show()</c>: picks where focus lands (spec 8.2, <see cref="OpeningFocus"/>), with a
    /// route live first trimming the list to what is still ahead (<see cref="TrimStepsToWhatIsAhead"/>), and makes it the
    /// active control, so the window's own activation, which restores the active control, lands there and the screen
    /// reader names one control, once. Set after <c>Show()</c>, activation first restored the control focused last, the
    /// reader named it, and then the new one. The activation then follows guidance (<see cref="WndProc"/>), which
    /// finds nothing more to drop after this trim; it is what keeps the list up to date when the pilot comes back by
    /// Alt+Tab or a click, and while the window stays open.
    /// </summary>
    public void PrepareForOpening()
    {
        bool routeLive = _guidanceManager.HasLiveRoute;
        // This opening's number: a hidden window is counted when Show() makes it visible (OnVisibleChanged).
        int opening = Visible ? _timesShown : _timesShown + 1;
        var target = OpeningFocus(firstShow: opening <= 1, routeLive);
        // A trim that leaves the list empty is said once, after the screen reader has named the box focus moves to.
        if (target == OpeningFocusTarget.StepKind)
        {
            if (TrimStepsToWhatIsAhead()) SpeakStepAdded(ClearanceStepEntry.StepsCleared, afterFocusMove: true);
        }
        Control control = target switch
        {
            OpeningFocusTarget.Airport => txtAirport,
            OpeningFocusTarget.StepKind => cmbStepKind,
            _ => cmbDestType,
        };
        _openingFocusControl = control;
        ActiveControl = control;
    }

    /// <summary>
    /// Called by MainForm after <c>Show()</c>: focuses the control <see cref="PrepareForOpening"/> chose, only when the
    /// window's activation has not already (a window already open is not activated again), so it is named once.
    /// </summary>
    public void FocusForOpening()
    {
        var control = _openingFocusControl;
        _openingFocusControl = null;
        if (control == null || control.Focused) return;
        ActiveControl = control;
        control.Focus();
    }

    // The activation message, and the LOWORD of its wParam that means the window is being deactivated (WA_ACTIVE and
    // WA_CLICKACTIVE activate it).
    private const int WM_ACTIVATE = 0x0006;
    private const int WA_INACTIVE = 0;

    /// <summary>Coming back to the planner (Alt+Tab, a click, Shift+Y's <c>Show()</c>): the list follows guidance at
    /// once (<see cref="FollowGuidance"/>), so the pilot never reads a step already finished. Before WinForms handles
    /// the activation, never in <c>OnActivated</c>: WinForms puts focus back on the active control while it handles
    /// WM_ACTIVATE, before it raises <c>OnActivated</c>, so a follow there came after the screen reader had named a step
    /// that finished while the pilot was away, and then the step now in its place. Here the list is up to date when focus
    /// lands, and the reader names one step, the right one. The follow never throws (<see cref="FollowGuidanceSafely"/>),
    /// so WinForms always handles the message.</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ACTIVATE && (m.WParam.ToInt64() & 0xFFFF) != WA_INACTIVE)
            FollowGuidanceSafely(activation: true);
        base.WndProc(ref m);
    }

    /// <summary>The follow timer, once a second while the window is shown (<see cref="OnVisibleChanged"/>): the list
    /// follows guidance only while this window is the active one, never while the pilot is in another window.</summary>
    private void OnFollowTimerTick(object? sender, EventArgs e)
    {
        if (Form.ActiveForm == this) FollowGuidanceSafely(activation: false);
    }

    // The last failure of the follow logged (its type and message), and how long one follow may take before it is logged.
    private string? _lastFollowFailure;
    private const double SlowFollowMs = 50;

    /// <summary>
    /// <see cref="FollowGuidance"/> for its two callers, the activation (<see cref="WndProc"/>) and the tick. It never
    /// throws: a failure must neither stop the window's activation nor raise an error once a second. A failure is logged
    /// the first time and again only when it changes, never every second. A follow slower than
    /// <see cref="SlowFollowMs"/> is logged with its time: the window takes guidance's lock once a second
    /// (<c>GetClearanceProgress</c>), and the log shows whether that ever holds the window up.
    /// </summary>
    private void FollowGuidanceSafely(bool activation)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            FollowGuidance(activation);
        }
        catch (Exception ex)
        {
            string failure = ex.GetType().Name + ": " + ex.Message;
            if (failure != _lastFollowFailure)
            {
                _lastFollowFailure = failure;
                _taxiFormLog.Error($"The step list could not follow guidance (activation: {activation}): {ex}");
            }
        }
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed > SlowFollowMs)
            _taxiFormLog.Info(FormattableString.Invariant(
                $"The step list took {elapsed:F0} ms to follow guidance (activation: {activation})."));
    }

    /// <summary>True while the step kind, name or At list is dropped down: the pilot is picking from it.</summary>
    private bool StepListDroppedDown => cmbStepKind.DroppedDown || cmbStepName.DroppedDown || cmbStepAt.DroppedDown;

    /// <summary>
    /// The list follows guidance while the window is open: finished steps drop off when the pilot comes back to the
    /// window (<paramref name="activation"/>) and, checked once a second, while it is the active one. The owner stays in
    /// the planner while taxiing, to get to things quickly, or Alt+Tabs out to a First Officer flow and back; trimmed
    /// only on Shift+Y and at Calculate, the list the pilot read there still held steps already finished.
    /// <para>Never while the pilot is editing (<see cref="ClearanceListFollow.FollowsNow"/>): with a step being changed
    /// (Enter on it in the Clearance list) the step boxes hold that step and Add Step replaces it by its place, and an
    /// open step list is being picked from; the list waits, and the next check after catches up. The window must be the
    /// active one, a route live and the list the leg calculated (<see cref="StepsStillAhead"/>, the one computation every
    /// trim uses).</para>
    /// <para>With the focus in the Clearance list the update is at once (the owner's "Update right away"): the pilot is
    /// reading the list now, and a list that waited for them to leave it would go on reading finished steps. "In the
    /// list" is the control the focus is in or goes back to (<c>ActiveControl</c>), the same rule for both paths: on
    /// activation this runs before focus is put back, so the list does not have the focus yet. The list is updated in
    /// place where it only lost steps (<see cref="ClearanceListFollow.RemovedIndices"/>, <see cref="ApplyFollowToList"/>),
    /// so the steps that stay keep their items; the pilot's place maps by identity
    /// (<see cref="ClearanceListFollow.SelectionAfter"/>): the selected step stays selected, a finished one gives its place
    /// to the step now there only with the focus in the list, and the selection is set only when it moves, so the screen
    /// reader reads a step once, and only one it has not just read; BA says nothing of it ([CORE-7]). The name box is
    /// refilled only when what it offers changes, keeping its pick and never picking a name the pilot did not
    /// choose.</para>
    /// <para>Speaks only when it leaves a list that had steps empty ("Steps cleared.": an empty list gives the screen
    /// reader nothing to say): on coming back, after the reader names the focused control, as on Shift+Y; while open,
    /// queued behind guidance.</para>
    /// </summary>
    private void FollowGuidance(bool activation)
    {
        if (!ClearanceListFollow.FollowsNow(activation || Form.ActiveForm == this, _guidanceManager.HasLiveRoute,
                _calculatedSteps != null, _changingStepIndex >= 0, StepListDroppedDown))
            return;
        var (steps, origins, dropped) = StepsStillAhead();
        if (steps.SequenceEqual(_steps) && origins.SequenceEqual(_stepOrigins)) return;

        var oldOrigins = _stepOrigins.ToList();
        int oldSelected = lstClearance.SelectedIndex;
        // The control focus is in, or goes back to: on activation this runs before focus is put back (WndProc).
        bool listFocused = ActiveControl == lstClearance;
        bool hadSteps = _steps.Count > 0;

        _steps.Clear();
        _steps.AddRange(steps);
        _stepOrigins.Clear();
        _stepOrigins.AddRange(origins);
        _removedOrigins.UnionWith(dropped);

        ApplyFollowToList(lstClearance, ClearanceListFollow.RemovedIndices(oldOrigins, origins),
            _steps.Select(step => step.Sentence()).ToList(),
            ClearanceListFollow.SelectionAfter(oldOrigins, oldSelected, origins, listFocused));

        if (!NameBoxLists(StepNameItems(SelectedStepKind())))
            RefreshStepNameList(keepSelection: true, preselect: false);

        if (hadSteps && _steps.Count == 0)
        {
            if (activation) SpeakStepAdded(ClearanceStepEntry.StepsCleared, afterFocusMove: true);
            else _announcer.Announce(ClearanceStepEntry.StepsCleared);
        }
    }

    /// <summary>
    /// The Clearance list after the list followed guidance: with <paramref name="removed"/> (the old list's indexes of the
    /// steps that went, <see cref="ClearanceListFollow.RemovedIndices"/>) the items are updated in place, each removed
    /// from the highest down, then each item set only where its text differs (a step the trim changed: a turn made reads
    /// as Via, a crossing put back or pinned); with none, the list is rebuilt from <paramref name="sentences"/>. Then
    /// <paramref name="select"/> (<see cref="ClearanceListFollow.SelectionAfter"/>) is selected only when the selection
    /// differs, so the screen reader never reads the same step again. A list box has a selected item and a focused item
    /// (its caret): removing an item above the selection moves the selection up but leaves the caret where it was, and
    /// focus back in the list (Tab, Shift+Tab, Alt+Tab) names the focused item while Enter and Delete act on the selected
    /// one. So when the selection did not move, the caret is put back on it with <c>LB_SETCURSEL</c>, which raises no
    /// SelectedIndexChanged. Internal static so it is tested on a list box never shown (ClearanceListSyncTests).
    /// </summary>
    internal static void ApplyFollowToList(ListBox list, IReadOnlyList<int>? removed, IReadOnlyList<string> sentences,
                                           int select)
    {
        list.BeginUpdate();
        try
        {
            if (removed != null)
            {
                for (int r = removed.Count - 1; r >= 0; r--) list.Items.RemoveAt(removed[r]);
                for (int i = 0; i < sentences.Count; i++)
                {
                    if (!string.Equals(list.Items[i]?.ToString(), sentences[i], StringComparison.Ordinal))
                        list.Items[i] = sentences[i];
                }
            }
            else
            {
                list.Items.Clear();
                foreach (string sentence in sentences) list.Items.Add(sentence);
            }
        }
        finally { list.EndUpdate(); }

        if (list.SelectedIndex != select) list.SelectedIndex = select;
        if (select >= 0 && list.IsHandleCreated && FocusedItem(list) != select)
            SendMessage(list.Handle, LB_SETCURSEL, (IntPtr)select, IntPtr.Zero);
    }

    /// <summary>
    /// The step Delete, Enter or Remove Step acts on in the Clearance list: the selected step; with none selected and the
    /// key pressed in the list (<paramref name="fromList"/>), the list's focused item (<see cref="FocusedItem"/>), the
    /// step the screen reader named as focus came into the list. Nothing stays selected after a step selected with the
    /// focus elsewhere finishes, or after Add Step, Replace and the opening trim, but the focused item is still on a step:
    /// Delete took the LAST step then, silently from inside the list, and Enter did nothing. -1 for none: from outside
    /// the list Remove Step then takes the last step, as it always has.
    /// </summary>
    internal static int StepActedOn(ListBox list, bool fromList)
    {
        if (list.SelectedIndex >= 0 && list.SelectedIndex < list.Items.Count) return list.SelectedIndex;
        return fromList ? FocusedItem(list) : -1;
    }

    /// <summary>The list box's focused item (its caret, <c>LB_GETCARETINDEX</c>), the one the screen reader names as focus
    /// comes into it, which can differ from the selected one; -1 for none (no handle, or an empty list). The one caret
    /// read (<see cref="ApplyFollowToList"/>, <see cref="StepActedOn"/>).</summary>
    internal static int FocusedItem(ListBox list)
    {
        if (!list.IsHandleCreated) return -1;
        int caret = (int)SendMessage(list.Handle, LB_GETCARETINDEX, IntPtr.Zero, IntPtr.Zero);
        return caret >= 0 && caret < list.Items.Count ? caret : -1;
    }

    // The list box caret and selection messages: WinForms reads the caret but never sets it.
    private const int LB_SETCURSEL = 0x0186;
    private const int LB_GETCARETINDEX = 0x019F;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Whether the name box already lists <paramref name="items"/>, item for item (kind and label, in order).</summary>
    private bool NameBoxLists(IReadOnlyList<ClearanceNameLists.Item> items)
    {
        if (cmbStepName.Items.Count != items.Count) return false;
        for (int i = 0; i < items.Count; i++)
        {
            if (cmbStepName.Items[i] is not ClearanceNameLists.Item shown || shown.Kind != items[i].Kind
                || !string.Equals(shown.Label, items[i].Label, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// While a route is live, the list shows only what is still ahead (spec 8.2), as <see cref="StepsStillAhead"/>
    /// computes it, the list replaced as a whole (<see cref="ShowSteps"/>: a step being changed is kept as it was first,
    /// and nothing stays selected) and the made crossings it took out kept out of every later trim. Run when the window
    /// opens through Shift+Y (<see cref="PrepareForOpening"/>) and at the top of every Calculate; between them the window
    /// left open, or come back to by Alt+Tab, follows guidance by itself (<see cref="FollowGuidance"/>), so the pilot
    /// never reads, and Calculate never calculates from, steps already finished ("Cross runway 04L" clearing 04L again
    /// for the new route, "Left onto C" judged again). Speaks nothing; returns true when it left a list that had steps
    /// empty, for the window opening to say so (Calculate's one utterance says what it does with an empty list). Nothing
    /// to trim when the list holds no leg calculated from it.
    /// </summary>
    private bool TrimStepsToWhatIsAhead()
    {
        if (_calculatedSteps == null || !_guidanceManager.HasLiveRoute) return false;
        var (steps, stepOrigins, dropped) = StepsStillAhead();

        bool hadSteps = _steps.Count > 0;
        bool changed = !steps.SequenceEqual(_steps) || !stepOrigins.SequenceEqual(_stepOrigins);
        if (!changed) return false;
        ShowSteps(steps, stepOrigins);
        _removedOrigins.UnionWith(dropped);
        RefreshStepNameList();
        return hadSteps && _steps.Count == 0;
    }

    /// <summary>
    /// The list with only what is still ahead, the ONE computation every trim uses (the window opening, Calculate, the
    /// list following guidance). <see cref="ClearanceTrim.Remaining"/> from the list as last calculated
    /// (<see cref="_calculatedSteps"/>, the numbering guidance's progress uses), never from one already trimmed, with
    /// <c>GetClearanceProgress</c>'s lists passed straight in; then the pilot's edits since are kept
    /// (<see cref="ClearanceTrim.KeepingEdits"/>) and each made crossing a step now follows is taken out
    /// (<see cref="ClearanceTrim.CompletedCrossings"/>), its origin, when it has one, in <c>DroppedOrigins</c> for the
    /// caller to keep out of every later trim, as <see cref="DropCompletedCrossings"/> does. A turn already made reads as
    /// Via (TurnOntoFirstRemainingMade, the made turns remembered across a recalculation included), so a later Calculate
    /// never judges it again from the aircraft's heading. With "A, C, A" the Via steps done are counted on the route by
    /// its cursor, so on the last A only that A is left; on a route recalculated from a later row the position cannot
    /// tell the two A apart, nothing is counted done, and nothing still ahead is dropped; a crossing still ahead is kept
    /// (spec 8.3). Changes nothing. The callers run it only with a list calculated and a route live; with no list
    /// calculated it is the list as it stands.
    /// </summary>
    private (List<ClearanceStep> Steps, List<int> Origins, List<int> DroppedOrigins) StepsStillAhead()
    {
        if (_calculatedSteps == null) return (_steps.ToList(), _stepOrigins.ToList(), new List<int>());
        var progress = _guidanceManager.GetClearanceProgress();
        var trimmed = ClearanceTrim.Remaining(_calculatedSteps, progress.ViaStepsDone, _calculatedWithDestination,
            progress.ClearedInForce, progress.ClearedUsed, progress.PutBack, progress.HoldsPassed,
            progress.TurnOntoFirstRemainingMade, out var origins);
        var (steps, stepOrigins) = ClearanceTrim.KeepingEdits(trimmed, origins, _steps, _stepOrigins, _removedOrigins);

        var dropped = new List<int>();
        var completed = ClearanceTrim.CompletedCrossings(steps);
        for (int c = completed.Count - 1; c >= 0; c--)
        {
            int i = completed[c];
            if (stepOrigins[i] >= 0) dropped.Add(stepOrigins[i]);
            steps.RemoveAt(i);
            stepOrigins.RemoveAt(i);
        }
        return (steps, stepOrigins, dropped);
    }

    /// <summary>Replaces the list and the Clearance list's items with <paramref name="steps"/> and their origins (a trim),
    /// a step being changed kept as it was first, silently: its place may move or go.</summary>
    private void ShowSteps(IReadOnlyList<ClearanceStep> steps, IReadOnlyList<int> origins)
    {
        CancelStepChange(false);
        _steps.Clear();
        _steps.AddRange(steps);
        _stepOrigins.Clear();
        _stepOrigins.AddRange(origins);
        lstClearance.BeginUpdate();
        try
        {
            lstClearance.Items.Clear();
            foreach (var step in _steps) lstClearance.Items.Add(step.Sentence());
            lstClearance.SelectedIndex = -1;
        }
        finally { lstClearance.EndUpdate(); }
    }

    /// <summary>
    /// A successful Calculate used the list as it stands: it is the list later trims start from, numbered afresh (every
    /// step its own origin), with whether the clearance had a destination (the trim's leg end depends on it). Bumps the
    /// generation, so a leg end queued before this Calculate never empties the list it just used. Called just BEFORE
    /// StartGuidance: called after it, a leg that ended in between (a position frame on guidance's own thread, the
    /// aircraft already at its end) was queued with the old generation, and the guard ignored it.
    /// </summary>
    private void RecordCalculatedSteps(bool hasDestination)
    {
        _calculatedSteps = _steps.ToList();
        _calculatedWithDestination = hasDestination;
        _stepOrigins.Clear();
        _stepOrigins.AddRange(Enumerable.Range(0, _steps.Count));
        _removedOrigins.Clear();
        Volatile.Write(ref _calculateGeneration, _calculateGeneration + 1);
    }

    /// <summary>
    /// <see cref="TaxiGuidanceManager.StateChanged"/>, raised under the manager's lock on its own thread: a state that
    /// ends the leg (<see cref="EndsTheLeg"/>) queues the emptying on the UI thread with the generation seen now and the
    /// state (a Progressive leg's end keeps the steps typed for the next, <see cref="LegEndKeepsTypedSteps"/>), never
    /// waiting for it ([A380C-6]).
    /// </summary>
    private void OnGuidanceStateChanged(object? sender, TaxiGuidanceState state)
    {
        if (!EndsTheLeg(state)) return;
        int generation = Volatile.Read(ref _calculateGeneration);
        SafeBeginInvoke(() => EmptyStepsAfterLeg(generation, state));
    }

    /// <summary>
    /// The leg the list was calculated for is over: nothing changes when a Calculate has built a list since or the list
    /// holds no calculated leg (<see cref="LegEndEmptiesList"/>). The KBOS route back over 04L came from an entry that
    /// outlived its leg.
    /// <para>A Progressive leg's end (<see cref="LegEndKeepsTypedSteps"/>) keeps the steps typed or changed since that
    /// Calculate (<see cref="ClearanceStepEntry.KeptAtProgressiveLegEnd"/>), in order, for the next leg (spec 2026-11-28):
    /// the pilot types the next instruction while stopped at the end of this one. A step being changed is closed first,
    /// silently, before any step moves, and a step sentence still waiting is dropped, as its number may be gone. The leg's
    /// own steps go, the Clearance list updated in place with the pilot's place mapped as the follow maps it
    /// (<see cref="ApplyFollowToList"/>), and the list then holds no calculated leg, so the follow never trims the kept
    /// steps before Calculate. Nothing is said: guidance says the leg's end, and every step the pilot typed is still
    /// there ([CORE-7]).</para>
    /// <para>Otherwise, and with nothing typed, the list is emptied, silently (the manager speaks the arrival, the stop,
    /// the leg's end or the touchdown). Steps the pilot typed since that Calculate go too; with the window focused they
    /// would vanish unsaid, so "Steps cleared." is said once, queued behind guidance's own sentence, never over it
    /// (<see cref="ClearanceStepEntry.LegEndSentence"/>).</para>
    /// </summary>
    private void EmptyStepsAfterLeg(int generation, TaxiGuidanceState state)
    {
        if (IsDisposed || !LegEndEmptiesList(generation, _calculateGeneration, _calculatedSteps != null)) return;
        var kept = LegEndKeepsTypedSteps(state) ? ClearanceStepEntry.KeptAtProgressiveLegEnd(_stepOrigins) : Array.Empty<int>();
        if (kept.Count > 0)
        {
            CancelStepChange(false);
            CancelPendingStepSpeech();
            var oldOrigins = _stepOrigins.ToList();
            int oldSelected = lstClearance.SelectedIndex;
            // The control focus is in, or goes back to, as the follow reads it.
            bool listFocused = ActiveControl == lstClearance;

            var keptSteps = kept.Select(i => _steps[i]).ToList();
            _steps.Clear();
            _steps.AddRange(keptSteps);
            _stepOrigins.Clear();
            _stepOrigins.AddRange(kept.Select(_ => -1));
            _removedOrigins.Clear();
            _calculatedSteps = null;

            ApplyFollowToList(lstClearance, ClearanceListFollow.RemovedIndices(oldOrigins, _stepOrigins),
                _steps.Select(step => step.Sentence()).ToList(),
                ClearanceListFollow.SelectionAfter(oldOrigins, oldSelected, _stepOrigins, listFocused));
            RefreshStepNameList(keepSelection: true, preselect: false);
            return;
        }

        string? sentence = ClearanceStepEntry.LegEndSentence(_stepOrigins, windowFocused: Visible && ContainsFocus);
        ClearSteps();
        if (sentence != null) _announcer.Announce(sentence);
    }

    /// <summary>
    /// <see cref="TaxiGuidanceManager.ClearedCrossingsChanged"/>: the Hold short key put back a stop the pilot was
    /// cleared across. Raised under the manager's lock on the key's thread, so the window queues its answer with the
    /// crossings put back and the generation seen now ([A380C-6]); the progress is read only after marshalling.
    /// </summary>
    private void OnClearedCrossingsChanged(object? sender, ClearedCrossingsChangedEventArgs e)
    {
        var restored = e.Restored;
        int generation = Volatile.Read(ref _calculateGeneration);
        SafeBeginInvoke(() => FollowPutBackCrossings(restored, generation));
    }

    /// <summary>
    /// The step list follows the Hold short key (spec 8.8): the Cross step of each crossing put back becomes "Hold short
    /// of runway R" at its place, so a later Calculate does not clear it again. Matched by RECORD (runway, Via step and
    /// place, as the trim matches), never by runway name, through the progress numbered as the list was calculated
    /// (<see cref="ClearanceTrim.PutBackSteps"/>); a crossing a Continue cleared has no step. Nothing else in the list
    /// moves, and nothing is said: the key's own sentence is what the pilot hears. Skipped when a Calculate has run since.
    /// A step being changed is kept as it was first, silently: the step put back may be that very one, and a replacement
    /// from the boxes would clear the crossing again.
    /// </summary>
    private void FollowPutBackCrossings(IReadOnlyList<ClearedCrossing> restored, int generation)
    {
        if (IsDisposed || generation != _calculateGeneration || _calculatedSteps == null) return;
        var progress = _guidanceManager.GetClearanceProgress();
        var putBack = ClearanceTrim.PutBackSteps(_calculatedSteps, restored, progress);
        if (putBack.Count == 0) return;
        CancelStepChange(false);
        var steps = ClearanceTrim.FollowingPutBack(_steps, _stepOrigins, putBack);
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] == _steps[i]) continue;
            _steps[i] = steps[i];
            lstClearance.Items[i] = steps[i].Sentence();
        }
    }

    /// <summary>Queues <paramref name="action"/> on the UI thread without waiting ([A380C-6]): BeginInvoke inside
    /// try/catch, as a handle check alone races its destruction (ObjectDisposedException is an
    /// InvalidOperationException).</summary>
    private void SafeBeginInvoke(Action action)
    {
        try { if (IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    /// <summary>Lays out the step block inside its panel, with the "At" line only when shown; returns its height.</summary>
    private int LayoutStepBlock()
    {
        const int labelGap = 20;
        int y = 0;
        lblStepKind.Location = new System.Drawing.Point(0, y + 3);
        cmbStepKind.Location = new System.Drawing.Point(120, y);
        y += 30;
        lblStepName.Location = new System.Drawing.Point(0, y);
        y += labelGap;
        cmbStepName.Location = new System.Drawing.Point(0, y);
        y += 30;
        if (_stepAtShown)
        {
            lblStepAt.Location = new System.Drawing.Point(0, y + 3);
            cmbStepAt.Location = new System.Drawing.Point(120, y);
            y += 30;
        }
        btnAddStep.Location = new System.Drawing.Point(0, y);
        y += 35;
        lblClearance.Location = new System.Drawing.Point(0, y);
        y += labelGap;
        lstClearance.Location = new System.Drawing.Point(0, y);
        y += lstClearance.Height + 6;
        btnRemoveStep.Location = new System.Drawing.Point(0, y);
        btnClearSteps.Location = new System.Drawing.Point(150, y);
        y += 33;
        return y;
    }

    // ---- What Calculate takes from the step list ------------------------------------------------

    /// <summary>The destination runway a Hold short step may name without adding a stop (guidance holds there
    /// already): the runway picked when the destination type is Runway, else null.</summary>
    private string? PlanDestinationRunway(bool hasDestination)
        => hasDestination && cmbDestType.SelectedIndex == 0 && cmbDestination.SelectedItem?.ToString() is string label
            ? StripBacktrackSuffix(label)
            : null;

    /// <summary>The plan's stops on the way of one target kind, as LoadRoute takes them: (the taxiway-sequence index
    /// of the Via step entered before it, -1 when none, the stop) pairs, any number per taxiway. Null when there is
    /// none.</summary>
    private static List<KeyValuePair<int, T>>? PlanStops<T>(ClearancePlan plan, ClearanceTargetKind kind,
        Func<ClearanceStep, T> stop)
    {
        var stops = plan.Holds
            .Where(h => h.Step.TargetKind == kind)
            .Select(h => new KeyValuePair<int, T>(h.SeqIndex, stop(h.Step)))
            .ToList();
        return stops.Count > 0 ? stops : null;
    }

    /// <summary>The plan's Turn picks as LoadRoute takes them: the index of the Left onto or Right onto step's own
    /// taxiway in the sequence -> the side; null when there is none. They reach guidance only this way ([TURN-1]).</summary>
    private static Dictionary<int, TurnSide>? PlanTurnPicks(ClearancePlan plan)
        => plan.TurnPicks.Count > 0 ? new Dictionary<int, TurnSide>(plan.TurnPicks) : null;

    /// <summary>A Calculate that builds no route: the reason, spoken with any pending import summary
    /// (<see cref="AnnounceCalculateAbort"/>), and kept in the status line and, unless a route is still being flown,
    /// the summary box ([RTE-29]: a refusal leaves that route untouched).</summary>
    private void RefuseCalculate(string reason)
    {
        AnnounceCalculateAbort(reason);
        ShowRouteFailure(reason, keepSummary: _guidanceManager.HasLiveRoute);
    }

    private List<string> SortTaxiwaysByDistance(List<string> taxiwayNames)
    {
        if (_graph == null) return taxiwayNames;

        return taxiwayNames
            .Select(name =>
            {
                double minDist = double.MaxValue;
                foreach (var node in _graph.Nodes.Values)
                {
                    if (node.TaxiwayNames.Contains(name))
                    {
                        double d = TaxiGraph.CalculateDistanceMeters(
                            _aircraftLat, _aircraftLon, node.Latitude, node.Longitude);
                        if (d < minDist) minDist = d;
                    }
                }
                return (name, minDist);
            })
            .OrderBy(x => x.minDist)
            .Select(x => x.name)
            .ToList();
    }

    /// <summary>
    /// Resolves the loaded airport's online NAMED holding points (VIKAS, N2E…)
    /// onto navdata graph nodes. Alias-style per the augmentation safety rules:
    /// the online source contributes the NAME only; the route target is always
    /// the navdata node it resolves to, and unresolvable points are dropped.
    /// One pass over the graph per point, run at most once per airport load once the
    /// online source has data (_namedHoldingPointsResolved); until then the async fetch
    /// may still be in flight, so an empty source leaves the latch clear and the combo
    /// retries on demand.
    /// </summary>
    private void ResolveNamedHoldingPoints()
    {
        _namedHoldingPoints = new List<NamedHoldingPoint>();
        if (_graph == null || string.IsNullOrEmpty(_currentIcao)) return;
        if (_dataProvider is not MSFSBlindAssist.Services.TaxiAugment.AugmentingAirportDataProvider aug
            || !aug.Enabled)
            return;
        var raw = aug.GetNamedHoldingPoints(_currentIcao);
        var signs = aug.GetSignHoldingPoints(_currentIcao);
        // Leave the latch clear on an empty source: the online fetch is async, so this
        // is "not yet", not "none" — and retrying is free, we returned before scanning.
        if (raw.Count == 0 && signs.Count == 0) return;
        _namedHoldingPoints = NamedHoldingPointResolver.Resolve(_graph, raw);
        _namedHoldingPointsResolved = true;

        // X-Plane red signs fill in ONLY names no painted line carries (OMDB "KK"). Their
        // position is approximate, so they are labelled so and kept out of runway entries
        // and cross-at points (GetHoldingPointsCrossingRunway).
        if (signs.Count > 0)
        {
            var taken = new HashSet<string>(
                _namedHoldingPoints.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var fromSigns = NamedHoldingPointResolver.ResolveSigns(
                _graph, signs, taken, msg => _taxiRouterLog.Debug($"  {msg}"));
            _taxiRouterLog.Info(
                $"{_currentIcao}: sign holding points raw={signs.Count} added={fromSigns.Count}" +
                (fromSigns.Count > 0 ? $" ({string.Join(", ", fromSigns.Select(p => p.Name))})" : ""));
            if (fromSigns.Count > 0)
                _namedHoldingPoints = _namedHoldingPoints.Concat(fromSigns)
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        // Field diagnostics: a "why isn't VIKAS in my list?" report is otherwise
        // unanswerable without an ad-hoc probe against the user's navdata.
        var resolvedNames = new HashSet<string>(
            _namedHoldingPoints.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var rawNames = raw.Select(p => (p.Name ?? "").Trim())
                          .Where(n => n.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();
        var dropped = rawNames.Where(n => !resolvedNames.Contains(n))
                              .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                              .ToList();
        int projected = _namedHoldingPoints.Count(p => p.InsertedOnEdge);
        _taxiRouterLog.Info(
            $"{_currentIcao}: named holding points raw={raw.Count} distinct={rawNames.Count} " +
            $"resolved={_namedHoldingPoints.Count}" +
            (projected > 0 ? $" (of which edge-projected={projected})" : "") +
            (dropped.Count > 0 ? $" dropped={string.Join(", ", dropped)}" : ""));
        foreach (var hp in _namedHoldingPoints)
            _taxiRouterLog.Debug(
                $"  {hp.Name} -> node {hp.NodeId}, {hp.SnapDistanceMeters:F1} m, " +
                $"designated={hp.SnappedToDesignatedNode}, projected={hp.InsertedOnEdge}, " +
                $"kind={(hp.Kind.Length > 0 ? hp.Kind : "-")}");

        // Where Am I names the point the aircraft is near. It takes this list only while the graph
        // was built from the airport's current online data, and resolves its own otherwise.
        _guidanceManager.SetNamedHoldingPoints(
            _currentIcao, _namedHoldingPoints, _graphHoldingPointDataVersion, _dataProvider);
    }

    private void UpdateLayout()
    {
        // The step block's panel is as tall as its lines (the "At" line comes and goes).
        int panelHeight = LayoutStepBlock();
        pnlSteps.Height = panelHeight;

        // Reposition buttons and status below the panel
        int y = pnlSteps.Location.Y + panelHeight;
        if (panelHeight > 0) y += 5;

        btnSayIntentions.Location = new System.Drawing.Point(15, y);
        y += 35;

        btnCalculate.Location = new System.Drawing.Point(15, y);
        btnStop.Location = new System.Drawing.Point(15 + 190, y);
        y += 40;

        lblStatus.Location = new System.Drawing.Point(15, y);
        y += 25;

        // The route-summary block moves with everything above it. Before this,
        // UpdateLayout left it at its construction position, so every added
        // taxiway row pushed the buttons/status DOWN OVER the summary box, and
        // formHeight never accounted for it (its bottom ~27 px sat below the
        // computed client area).
        lblRouteSummary.Location = new System.Drawing.Point(15, y);
        y += 22;
        txtRouteSummary.Location = new System.Drawing.Point(15, y);
        y += txtRouteSummary.Height;

        // Resize form to fit
        int formHeight = y + 15;
        if (formHeight < 480) formHeight = 480;
        this.ClientSize = new System.Drawing.Size(this.ClientSize.Width, formHeight);
    }

    /// <summary>Runs the SayIntentions import — the same call the Ctrl+Shift+Y hotkey
    /// makes, which re-enters this very form (it loads the airport here and applies the
    /// route to these controls). That is safe: the import's own one-at-a-time latch is
    /// taken by the CALLER, not by anything on this path, so a click never contends with
    /// itself; and the airport load it triggers CHAINS on any load already running rather
    /// than rejecting it, so a nested call waits for pending work instead of deadlocking
    /// on it.
    ///
    /// NOTHING IS ANNOUNCED HERE. The screen reader already speaks the button activation,
    /// and the import speaks its own progress and its summary — a third utterance would
    /// only talk over them. A second press while one is running is answered by the
    /// caller's latch, out loud, which is also why the button is not disabled for the
    /// duration: disabling the control that currently has focus moves focus off it.
    ///
    /// try/catch because this is `async void`: the import handles its own failures, but an
    /// escaped throw here would take the app down rather than the operation. QUEUED, not
    /// immediate — the import's own handler reports the specific failure immediately, and
    /// an immediate announcement here would discard that queue to say something vaguer.</summary>
    private async void OnSayIntentionsClicked(object? sender, EventArgs e)
    {
        if (_importFromSayIntentions == null) return;

        try
        {
            await _importFromSayIntentions();
        }
        catch (Exception ex)
        {
            _taxiFormLog.Error($"SayIntentions import from the taxi form failed: {ex}");
            _announcer.Announce("SayIntentions taxi route failed.");
        }
    }

    private void OnCalculateClicked(object? sender, EventArgs e)
    {
        // A step sentence still waiting (a step added a moment ago) must not land after the one Calculate utterance
        // ([HLD-17], [SI-22]): the clearance it confirms is in what Calculate says.
        CancelPendingStepSpeech();

        // A step still being changed: Calculate goes ahead only when the boxes would make the same step (the change is
        // closed, the step as it was); otherwise it refuses with the step's number and changes nothing. Before the trim,
        // which would close the change itself and lose what the pilot was entering.
        if (_changingStepIndex >= 0)
        {
            if (!RebuildStepBeingChanged().Same)
            {
                RefuseCalculate(ClearanceStepEntry.StillChanging(_changingStepIndex + 1));
                return;
            }
            CancelStepChange(false);
        }

        // With a route live, the steps already finished go first (spec 8.2), silently: the window may have stayed open,
        // or come back by Alt+Tab, while the aircraft crossed 04L, and the plan built from the untrimmed list would clear
        // 04L again for the new route and judge a turn already made again. A crossing still ahead stays (spec 8.3).
        TrimStepsToWhatIsAhead();

        if (_graph == null)
        {
            const string noAirport = "No airport loaded. Enter an ICAO code first.";
            AnnounceCalculateAbort(noAirport);
            // PR #238 review, Important 4: this dialog is hide-on-close and cached, and
            // LoadAirportDataAsync drops _graph before its awaits and only reassigns it on
            // success -- so this form-level field can be null mid-taxi with the MANAGER
            // still flying a route from an earlier, successful Calculate (a different
            // dialog session, or an earlier airport load in this one). Reopen Taxi Assist,
            // have the reload fail, press Calculate: without keepSummary the live route's
            // summary box was wiped for a reason that has nothing to do with that route.
            ShowRouteFailure(noAirport, keepSummary: _guidanceManager.HasLiveRoute);
            return;
        }

        // A form left open through a whole flight never re-shows and never reloads the
        // airport, so this is the last moment to notice GSX has published it since the gate
        // list was built (see _cachedGateSpots). Before the destination lookup below and
        // before SelectGsxGateAsync, because both read _destinationSpotMap: acting on the
        // stale entry is exactly the "GSX could not prepare this stand" every-route failure.
        // A no-op (one string compare) unless the source moved. If it did AND the pilot's
        // destination is no longer in the rebuilt list, ABORT rather than route to whatever
        // item 0 now is -- a stand they never chose. Immediate, not queued: this is the answer
        // to the Calculate press, and AnnounceCalculateAbort carries any import summary too.
        if (RefreshDestinationsIfGateSourceChanged())
        {
            AnnounceCalculateAbort(GateListUpdatedMessage);
            // Important 4, same class as the _graph == null guard above: a rebuilt gate
            // list can abort Calculate while an EARLIER Calculate's route is still live.
            ShowRouteFailure(GateListUpdatedMessage, keepSummary: _guidanceManager.HasLiveRoute);
            return;
        }

        // Refresh aircraft position from the latest SimConnect sample (if
        // available) before route construction. Without this, the route starts
        // from wherever the aircraft was when the form was OPENED — typically
        // pre-pushback, several meters from where the post-pushback aircraft
        // actually is. The off-route detector then fires within seconds of the
        // pilot starting to taxi because they were "off route" from the very
        // first frame. LastKnownPosition is updated by every position-bearing
        // SimConnect sample (visual guidance, hand-fly, etc.) so it's almost
        // always within a frame of the truth even when no taxi-specific
        // position monitor is active yet.
        if (_simConnectManager?.LastKnownPosition is { } pos)
        {
            _aircraftLat = pos.Latitude;
            _aircraftLon = pos.Longitude;
            // MAGNETIC, like every other writer of this field (see the
            // HeadingExitSideSign consumer, whose contract pins the frame).
            // Consumers that compare against TRUE graph bearings convert via
            // AircraftHeadingTrue() instead.
            _aircraftHeading = pos.HeadingMagnetic;
        }

        // The clearance as entered: ONE plan for every destination type and for a leg with no destination
        // (ClearancePlanner.Build). Its refusal is checked here for every type, before anything is looked up,
        // and leaves the route being flown as it is ([RTE-29]).
        bool hasDestination = cmbDestType.SelectedIndex != 2;
        // Diagnostics: the steps the plan is built from (trimmed to what is ahead) and the destination, so a report
        // can be read from the logs.
        _taxiRouterLog.Info(ClearanceStepEntry.LogLine(_steps, cmbDestType.SelectedItem?.ToString() ?? "",
            hasDestination ? cmbDestination.SelectedItem?.ToString() : null));
        var plan = ClearancePlanner.Build(_steps, hasDestination, PlanDestinationRunway(hasDestination));
        if (plan.Refusal != null)
        {
            RefuseCalculate(plan.Refusal);
            return;
        }

        // Progressive Taxi (no destination): the leg ends as its steps say. No final gate/runway, no lineup,
        // no Takeoff-Assist — guidance ends in the manager's ProgressiveHold state and announces the hold/end
        // (see TaxiGuidanceManager.HandleArrival).
        if (!hasDestination)
        {
            CalculateLegWithNoDestination(plan);
            return;
        }

        // Get destination. destLabel is the combo item and the key for every
        // _destination*Map (it may carry the "(backtrack required)" affordance);
        // destName is the CLEAN name — the one that is parsed for a runway id,
        // passed to LoadRoute, and spoken in every callout.
        string? destLabel = cmbDestination.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(destLabel) || !_destinationNodeMap.TryGetValue(destLabel, out int destNodeId))
        {
            const string noDestination = "Please select a destination.";
            AnnounceCalculateAbort(noDestination);
            // Important 4: reachable with a route already live too -- the dialog is
            // reopened mid-taxi to plan the next leg before a destination is picked yet.
            ShowRouteFailure(noDestination, keepSummary: _guidanceManager.HasLiveRoute);
            return;
        }
        string destName = StripBacktrackSuffix(destLabel);

        if (destNodeId < 0)
        {
            string unreachable =
                $"No taxi route to {destName}. This stand can't be reached by the taxi network.";
            AnnounceCalculateAbort(unreachable);
            // The spoken sentence, not the old terse status line ("Selected stand has no
            // taxi route."): the box is where the pilot goes to re-read what they heard,
            // so it must carry the same words, including WHICH stand. Important 4: same
            // live-route protection as every other early return in this method.
            ShowRouteFailure(unreachable, keepSummary: _guidanceManager.HasLiveRoute);
            return;
        }

        // Load route through guidance manager
        var settings = SettingsManager.Current;
        double? destHeading = _destinationHeadingMap.TryGetValue(destLabel, out double h) ? h : null;
        double? destHeadingTrue = _destinationHeadingTrueMap.TryGetValue(destLabel, out double ht) ? ht : null;
        double? thresholdLat = null, thresholdLon = null;
        if (_destinationThresholdMap.TryGetValue(destLabel, out var threshold))
        {
            thresholdLat = threshold.lat;
            thresholdLon = threshold.lon;
        }
        bool isRunwayDest = cmbDestType.SelectedIndex == 0;

        // Intersection departure: retarget the route to the chosen taxiway
        // intersection. The destination node becomes the taxiway's on-runway
        // node and the lineup target becomes that point on the centerline, so
        // guidance holds short there and lines up partway down the runway
        // instead of at the full-length threshold. destHeading/destHeadingTrue
        // stay the runway's takeoff heading — the centerline is the same line,
        // just entered further along (so Takeoff Assist, seeded from this lineup
        // target, tracks it unchanged). Everything else — TruncateToHoldShort,
        // the hold-short/continue/lineup flow, auto-activate — is relative to the
        // lineup target, so pointing it at the intersection reuses it all.
        // Full-length backtrack departure: retarget the route to an INTERMEDIATE
        // runway entrance (found geometrically, so it works even where taxiways are
        // unnamed — iniBuilds EGNM), but KEEP the lineup target at the full-length
        // threshold. Guidance holds short of the entrance, then backtracks to the
        // threshold and lines up full length. Mutually exclusive with the
        // intersection shortcut (which lines up PARTWAY down and does not backtrack).
        bool backtrackActive = false;
        // Spoken as part of the ONE standstill utterance (or the abort) further down,
        // never on its own — see where it is set.
        string? backtrackFallbackNote = null;
        if (isRunwayDest && chkFullLengthBacktrack.Checked && _graph != null)
        {
            string runwayId = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
                ? destName.Substring(7).Trim() : destName.Trim();
            if (_cachedRunways == null || _cachedRunwaysIcao != _currentIcao)
            {
                _cachedRunways = _dataProvider.GetRunways(_currentIcao);
                _cachedRunwaysIcao = _currentIcao;
            }
            var rwy = _cachedRunways
                .FirstOrDefault(r => string.Equals(r.RunwayID, runwayId, StringComparison.OrdinalIgnoreCase));
            if (rwy != null)
            {
                double halfWidthM = (rwy.Width > 0 ? rwy.Width : 150.0) * 0.3048 / 2.0;
                var entry = _graph.FindBacktrackEntryNode(
                    rwy.StartLat, rwy.StartLon, rwy.EndLat, rwy.EndLon, halfWidthM,
                    _aircraftLat, _aircraftLon);
                if (entry != null)
                {
                    destNodeId = entry.NodeId;       // route ends at the entrance
                    backtrackActive = true;          // lineup target stays full length
                }
                else
                {
                    // HELD, not spoken here. An AnnounceImmediate at this point is
                    // stomped moments later by the single standstill AnnounceImmediate
                    // (and by an abort's), so the pilot who ticked backtrack would taxi
                    // off expecting to be taken onto the runway with no audible sign the
                    // mode was dropped. It rides in that one utterance instead — the
                    // same lesson as the reach warning below.
                    backtrackFallbackNote =
                        "No backtrack entrance found for this runway. Full length departure.";
                }
            }
        }

        // Intersection departure (skipped when backtracking — the two are different
        // maneuvers): retarget the route to the chosen taxiway intersection. The
        // destination node becomes the taxiway's on-runway node and the lineup
        // target becomes that point on the centerline, so guidance holds short there
        // and lines up partway down the runway instead of at the full-length
        // threshold. destHeading/destHeadingTrue stay the runway's takeoff heading —
        // the centerline is the same line, just entered further along.
        // The Runway entry list's choice (D13): an intersection runs this branch, a holding
        // point the next one, Full length neither.
        RunwayEntryItem? runwayEntry = isRunwayDest ? SelectedRunwayEntry : null;
        TaxiGraph.RunwayIntersection? intersection = null;
        if (!backtrackActive
            && runwayEntry is { Kind: RunwayEntryKind.Intersection, Entry: { } ix })
        {
            intersection = ix;
            destNodeId = ix.NodeId;
            thresholdLat = ix.Latitude;
            thresholdLon = ix.Longitude;
        }

        // Named-holding-point departure (skipped when backtracking or when an
        // intersection already retargeted the route — same precedence pattern as
        // backtrack-over-intersection). The painted point SELECTS the entry node;
        // hold-short placement on the resulting route stays navdata-authoritative
        // (TruncateToHoldShort), so guidance holds short at that entry and the
        // normal Continue → lineup → Takeoff Assist flow runs from there. A
        // Partway entry moves the lineup target to the entry's centerline
        // projection (intersection semantics); a full-length entry keeps the
        // start-table lineup point and just forces WHICH stub is used.
        TaxiGraph.RunwayIntersection? holdingPointEntry = null;
        if (!backtrackActive && intersection == null
            && runwayEntry is { Kind: RunwayEntryKind.HoldingPoint, Entry: { } hp })
        {
            holdingPointEntry = hp;
            destNodeId = hp.NodeId;
            if (runwayEntry.Partway)
            {
                thresholdLat = hp.Latitude;
                thresholdLon = hp.Longitude;
            }
        }

        // Sizes the setback of a taxiway stop; _aircraftWingspan is in feet, 0 when unknown.
        _guidanceManager.AircraftWingspanMetres = _aircraftWingspan * 0.3048;
        string? error = _guidanceManager.LoadRoute(
            _dataProvider, _currentIcao,
            _aircraftLat, _aircraftLon, AircraftHeadingTrue(),
            destNodeId, destName,
            // The Via steps' taxiways, in order (a fresh list: LoadRoute edits its own).
            plan.TaxiwaySequence.Count > 0 ? plan.TaxiwaySequence.ToList() : null,
            userHoldShortIndices: null,
            destHeading,
            thresholdLat, thresholdLon, destHeadingTrue,
            isRunwayDest,
            prebuiltGraph: _graph,
            // The Hold short of steps on the way, keyed by the Via step each was entered after (-1: none); a runway's
            // with its place ("at B"), where it stops.
            userRunwayHoldShorts: plan.RunwayHoldPicks(),
            userTaxiwayHoldShorts: PlanStops(plan, ClearanceTargetKind.Taxiway, s => s.Name),
            preferIlsHold: isRunwayDest && chkCatIiiHold.Checked,
            fullLengthBacktrack: backtrackActive,
            // Pin the route through the chosen painted hold line, not just its runway entry
            // — otherwise the approach corridor is a free A* choice and can run up a
            // NEIGHBOURING stub that merges with this one short of the runway (EGLL 27R:
            // picked A2, taxied and held at A3). See ApplyHoldingPointPin.
            holdingPointHoldNodeId: holdingPointEntry?.HoldNodeId,
            // The point's label ("A1") so the hold-short stop can name the line the
            // pilot picked — spoken only when the stop node IS that point's node.
            holdingPointName: holdingPointEntry?.TaxiwayName,
            intersectionDeparture: intersection != null,
            userHoldingPointHoldShorts: PlanStops(plan, ClearanceTargetKind.HoldingPoint,
                s => new HoldingPointHoldShort(s.Name, s.NodeId)),
            userTurnPicks: PlanTurnPicks(plan),
            // The Cross runway steps: the only crossings, besides Continue, that a route clears ([CLR-1]).
            clearedCrossings: plan.Crossings.Count > 0 ? plan.Crossings : null);

        if (error != null)
        {
            // The dropped-backtrack note leads: it explains what the pilot asked for
            // and did not get, and the abort's own reason follows it.
            string spoken = backtrackFallbackNote == null ? error : backtrackFallbackNote + " " + error;
            // A runway whose item label carries "(backtrack required)" can fail to
            // route at all when its full-length entrance is an island the taxi network
            // never reaches (iniBuilds LMML 31: taxiways A/A1 at the threshold are their
            // own component). The failure alone leaves a blind pilot with nothing to try;
            // the backtrack option is the one recalculation that works, so say so here
            // as well as on the successful-route advisory below.
            if (isRunwayDest && !backtrackActive
                && destLabel.EndsWith(BacktrackRequiredSuffix, StringComparison.OrdinalIgnoreCase))
                spoken += " This runway has no full length taxiway. Recalculate with the " +
                          "full length departure with backtrack option.";
            AnnounceCalculateAbort(spoken);
            // A reachability refusal rolls back and leaves the PREVIOUS route still live
            // (TaxiGuidanceManager.RestoreLoadRouteRollback) -- the pilot IS still flying a
            // route even though this Calculate failed, so the summary box must keep showing
            // it rather than being overwritten with the failure reason (PR #238 review, Task
            // 7 Defect B). Decided from the manager's own guidance STATE (HasLiveRoute),
            // never guessed from the error string, and never from CurrentRoute != null,
            // which stays true well past arrival (PR #238 review, Important 3).
            ShowRouteFailure(spoken, keepSummary: _guidanceManager.HasLiveRoute);
            return;
        }

        // Capture the spoken summary into the read-only box so the user
        // can re-read it later — screen readers often interrupt the
        // spoken version, and the shortest-path branch produces a
        // particularly long string. This is the only place that surfaces
        // what the router actually decided when no taxiways were picked.
        txtRouteSummary.Text = _guidanceManager.LastRouteSummary;

        lblStatus.Text = "Route loaded. Guidance active.";

        // Docking guidance: set the target gate unconditionally when heading to
        // a gate (independent of GSX setting / availability), or clear it when
        // heading to a runway so a prior gate target doesn't persist.
        if (isRunwayDest)
        {
            _dockingManager?.SetDestinationGate(null);
        }
        else
        {
            _destinationSpotMap.TryGetValue(destLabel, out var destSpot);
            _dockingManager?.SetDestinationGate(destSpot);
            ApplyGsxStopOffset(destSpot);
        }

        CheckGateOccupancy(isRunwayDest, thresholdLat, thresholdLon);

        // Recorded just before guidance starts, so a leg that ends as it starts is never ignored (RecordCalculatedSteps).
        RecordCalculatedSteps(hasDestination: true);
        _guidanceManager.StartGuidance(settings);

        // The departure this runway route lines up for, said in the one Calculate utterance below.
        string rwyLabel = destName.StartsWith("Runway ", StringComparison.OrdinalIgnoreCase)
            ? "runway " + destName.Substring(7).Trim()
            : destName;
        string? intersectionLine = intersection == null ? null
            : $"Intersection {intersection.TaxiwayName} departure, {rwyLabel}. " +
              $"About {DistanceFormatter.FromMetres(intersection.RemainingMeters)} of runway ahead.";
        string? holdingPointLine = holdingPointEntry == null ? null
            : $"Holding point {holdingPointEntry.TaxiwayName}, {rwyLabel}. " +
              $"About {DistanceFormatter.FromMetres(holdingPointEntry.RemainingMeters)} of runway ahead.";
        // Backtrack advisory: the pilot calculated to a runway whose item label
        // carries "(backtrack required)" WITHOUT ticking the full-length backtrack
        // box. The label covers arrowing the combo; this covers every other way the
        // destination gets set (SayIntentions import, restored selection) — and the
        // stakes: at the LICR class the unticked route can quietly end hundreds of
        // metres short of the threshold with no reach warning. Advisory, not a
        // refusal — an intersection-style departure from the mid-field entrance is
        // legitimate if the pilot wants it.
        string? backtrackAdvisory = isRunwayDest && !backtrackActive
            && destLabel.EndsWith(BacktrackRequiredSuffix, StringComparison.OrdinalIgnoreCase)
            ? "This runway has no full length taxiway. For the full length, " +
              "recalculate with the full length departure with backtrack option."
            : null;
        SpeakCalculateUtterance(plan, backtrackFallbackNote, intersectionLine, holdingPointLine, backtrackAdvisory);

        // GSX gate auto-select: fire-and-forget when the destination is one GSX can prepare
        // (ShouldSendGateSelect — which destination types ask, and for a Place which stands)
        // and the feature is enabled. Conditions:
        //   - setting is on
        //   - a selector was provided (i.e. GsxService existed in this session when
        //     MainForm built this form) — no separate live "is GSX running" check is
        //     needed here any more: GsxRemoteGateSelector.SelectGateAsync feature-checks
        //     the 'gate' capability itself on every call, before ever sending gate.select,
        //     and returns Unavailable (silent) when GSX isn't running, or
        //     GateSelectUnsupported (spoken once per dialog) on a connected pre-4.0.8
        //     build — see SelectGsxGateAsync's own doc comment.
        // NOTE: deice areas (index 3) are explicitly excluded — gate.select prepares a
        // GSX parking stand, which has no deice-pad equivalent. DockingGuidanceManager
        // handles deice guidance via SetDestinationGate (spot.IsDeiceArea is true)
        // without any GSX Remote API interaction.
        _destinationSpotMap.TryGetValue(destLabel, out var selectSpot);
        if (ShouldSendGateSelect(cmbDestType.SelectedIndex, selectSpot)
            && SettingsManager.Current.GsxAutoSelectGateOnRoute && _gsxGateSelector != null)
        {
            // Do NOT await — route loading must not block on the GSX round trip.
            _ = SelectGsxGateAsync(selectSpot!);
        }

        // Form stays open so the user can read the summary box while
        // guidance is active. They close it manually with Escape / window-X
        // or by switching focus elsewhere; Stop Guidance button is also
        // available without re-opening.
    }

    /// <summary>
    /// Calculate for a leg with no destination (Progressive Taxi): the leg ends as the plan says
    /// (<see cref="ClearancePlan.End"/>, <see cref="ClearancePlan.EndStep"/>), resolved here to a node and the
    /// <see cref="ProgressiveTerminator"/> guidance announces it by, then routed by the same <c>LoadRoute</c> as a
    /// destination, with every stop, crossing and Turn pick the steps carry, and said in the same one utterance.
    /// <list type="bullet">
    /// <item>Hold short of a runway: at the step's "at", the runway's line there (a holding point's own node, or the
    /// line on that taxiway: <see cref="ClearanceLegEnd.HoldShortRunwayAt"/>), never the last Via taxiway's; else the
    /// hold point on the aircraft's side, anchored on the last Via taxiway
    /// (<see cref="ResolveHoldShortRunwayNode"/>); with none (a limit said alone), the first of the near side's hold
    /// lines the aircraft reaches (<see cref="HoldShortNodeResolver.NearSideHoldCandidates"/>, then
    /// <see cref="ClearanceLegEnd.NearestAhead"/>).</item>
    /// <item>Hold short of a taxiway: where the last Via taxiway meets it (with none, any node of it in the aircraft's
    /// component), never inside a runway's margin; narrowed to the meetings beyond a taxiway crossed after the last
    /// Via step (<see cref="ClearanceRouteChecks.BeyondTheCrossing"/>), then the nearest by taxi distance the
    /// aircraft need not turn round for (<see cref="ClearanceLegEnd.NearestAhead"/>). LoadRoute's own meet scan
    /// keeps the same crossing (<see cref="ClearancePlan.LegEndCrossedTaxiways"/>).</item>
    /// <item>Hold at a named holding point: its node, when the aircraft can reach it.</item>
    /// <item>Across a runway: the far side, at the step's "at" (a holding point the route is pinned through, as the
    /// "After crossing runway" leg did, or a taxiway); for a crossing already made, the far side of THAT crossing, never
    /// the far side from where the aircraft now is, which once across lies back over the runway
    /// (<see cref="ClearanceLegEnd.JudgeMadeCrossing"/>; a pin with no side is refused).</item>
    /// <item>End of the last taxiway: along its own edges, the way the aircraft travels on it
    /// (<see cref="ClearanceLegEnd.EntryOntoTaxiway"/>, <see cref="ClearanceLegEnd.EndOfTaxiway"/>). A Left onto or Right onto on the last Via step re-resolves this
    /// end, and a taxiway hold short's, inside LoadRoute (<c>TurnPickApplier</c>); the turn word plays no part here.</item>
    /// </list>
    /// An end that cannot be found is refused, never guessed: "Could not find … Check your entry.", or why an end
    /// exists only beside a runway (<see cref="ProgressiveLegEnd"/>).
    /// </summary>
    private void CalculateLegWithNoDestination(ClearancePlan plan)
    {
        var graph = _graph!;
        // The plan has an end: a leg with no destination and no end step is refused before this.
        var endStep = plan.EndStep!;
        // Canonical names for the lookups below: they match taxiway names exactly, and LoadRoute's alias
        // resolution only covers the route SEQUENCE. Without this, picking an alias label ("B (HAWKER)") from the
        // name box fails with "Could not find taxiway B (HAWKER)".
        var sequence = plan.TaxiwaySequence.Select(graph.ResolveTaxiwayName).ToList();
        string? lastVia = sequence.Count > 0 ? sequence[^1] : null;
        double headingTrue = AircraftHeadingTrue();

        // The aircraft's piece of network (never a bridge-only stand stub [RTE-3]), and the route's start as LoadRoute
        // picks one with no taxiway constraint: the nearest node ahead of the aircraft, else the nearest. Junction
        // distances are measured from it, and a junction the aircraft would have to turn round for ranks last.
        var nearest = graph.FindNearestNode(_aircraftLat, _aircraftLon, excludeBridgeOnlyStandStubs: true);
        if (nearest == null)
        {
            RefuseCalculate("Could not find your position on the taxi network.");
            return;
        }
        int component = nearest.ComponentId;
        var startNode = graph.FindNearestNodeInDirection(_aircraftLat, _aircraftLon, headingTrue,
            requiredComponentId: component, excludeBridgeOnlyStandStubs: true) ?? nearest;
        Dictionary<int, double>? distances = null;
        // A fresh router: its BlockedNodes is empty (TURN-1).
        IReadOnlyDictionary<int, double> Distances()
            => distances ??= new TaxiRouter(graph).ComputeGraphDistancesFrom(startNode.NodeId);
        // Each answer is a route search: asked once per node.
        var reversals = new Dictionary<int, bool>();
        bool NeedsReversal(int nodeId)
        {
            if (!reversals.TryGetValue(nodeId, out bool reversal))
                reversals[nodeId] = reversal = ClearanceLegEnd.NeedsReversal(graph, startNode.NodeId, nodeId, headingTrue);
            return reversal;
        }

        int destNode = -1;
        ProgressiveTerminator term;
        string runwayTarget = "", taxiwayTarget = "";
        // "Cross runway X at <holding point>": the painted point the route is pinned through on its way across.
        NamedHoldingPoint? crossAtHold = null;
        // "Cross runway X at <taxiway>": the far side is looked for on that taxiway.
        string? crossAtTaxiway = null;
        // "Hold short of runway X at <place>": the leg ends at X's line there.
        string? holdShortAt = null;
        // The leg end exists, but only inside a runway's clear margin: the entry is right, so say why, not "Check
        // your entry".
        string? besideRunway = null;

        switch (plan.End)
        {
            case ClearanceEndKind.HoldShortRunway:
            {
                // Route to the near-side hold-short node so guidance ENDS at the hold line (where ProgressiveHold
                // fires) — NOT past the runway. (ApplyUserRunwayHoldShorts only TAGS an intermediate hold-short; it
                // does not truncate the route, so routing to the far-side node would carry the leg across the runway
                // before the terminal announcement.)
                runwayTarget = endStep.Name;
                if (!string.IsNullOrWhiteSpace(endStep.At) || endStep.AtNodeId != 0)
                {
                    // "Hold short of runway X at Z": X's line at Z (a holding point's own node, or the line on taxiway
                    // Z), as a Cross step's place picks the crossing; never the last Via taxiway's line.
                    holdShortAt = string.IsNullOrWhiteSpace(endStep.At) ? null : endStep.At.Trim();
                    destNode = _crossRunwayMap.TryGetValue($"Runway {runwayTarget}", out var atRwy)
                        ? ClearanceLegEnd.HoldShortRunwayAt(graph, atRwy, endStep, component,
                            _aircraftLat, _aircraftLon, _aircraftHeading)
                        : -1;
                }
                else if (lastVia != null)
                {
                    destNode = ResolveHoldShortRunwayNode(runwayTarget, lastVia)?.NodeId ?? -1;
                }
                else if (_crossRunwayMap.TryGetValue($"Runway {runwayTarget}", out var holdRwy))
                {
                    // A limit said alone (spec 8.1, "limit only"): the first of the runway's hold lines on the near
                    // side the aircraft reaches, never the one nearest the runway anywhere along it.
                    destNode = ClearanceLegEnd.NearestAhead(
                        HoldShortNodeResolver.NearSideHoldCandidates(graph, holdRwy, _aircraftLat, _aircraftLon, _aircraftHeading),
                        Distances(), NeedsReversal);
                }
                term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, runwayTarget);
                break;
            }
            case ClearanceEndKind.HoldShortTaxiway:
            {
                taxiwayTarget = graph.ResolveTaxiwayName(endStep.Name);
                // Never inside a runway's clear margin (both lookups leave those out); the manager's
                // ApplyLegEndSetback then ends the leg at the first meeting along the route itself.
                IReadOnlyList<int> candidates = lastVia != null
                    ? graph.FindTaxiwayIntersectionNodes(lastVia, taxiwayTarget, component)
                    : ClearanceLegEnd.NodesOnTaxiway(graph, taxiwayTarget, component);
                // "Via A, cross B, hold short of C": the C beyond B (spec 8.1). Never leaves the leg with no end.
                if (plan.LegEndCrossedTaxiways.Count > 0 && candidates.Count > 1)
                {
                    var crossingNodes = new List<int>();
                    foreach (string crossed in plan.LegEndCrossedTaxiways)
                    {
                        string name = graph.ResolveTaxiwayName(crossed);
                        crossingNodes.AddRange(lastVia != null
                            ? graph.FindTaxiwayIntersectionNodes(lastVia, name, component)
                            : ClearanceLegEnd.NodesOnTaxiway(graph, name, component));
                    }
                    candidates = ClearanceRouteChecks.BeyondTheCrossing(candidates, Distances(), crossingNodes);
                }
                destNode = ClearanceLegEnd.NearestAhead(candidates, Distances(), NeedsReversal);
                if (destNode < 0 && lastVia != null
                    && graph.FindTaxiwayIntersectionNode(lastVia, taxiwayTarget, component) >= 0)
                    besideRunway = ProgressiveLegEnd.NoHoldShortClearOfARunway(taxiwayTarget, lastVia);
                term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortTaxiway, taxiwayTarget);
                break;
            }
            case ClearanceEndKind.HoldAtNamedPoint:
            {
                // A named holding point is resolved by NAME against the whole graph, so it can lie off the aircraft's
                // component. Disconnected taxiway islands are routine (LOWW/KJFK 6 components, EHAM 4, GCLP's 13-node
                // S5 island), and LoadRoute picks its start node in the DESTINATION's component with no distance
                // bound — routing to an island would silently start the route where the aircraft is not.
                if (!graph.Nodes.TryGetValue(endStep.NodeId, out var holdNode) || holdNode.ComponentId != component)
                {
                    RefuseCalculate($"Cannot taxi to {endStep.Name} from your position. Check your entry.");
                    return;
                }
                destNode = endStep.NodeId;
                term = new ProgressiveTerminator(ProgressiveTerminatorType.HoldAtNamedPoint, endStep.Name,
                    endStep.HoldingPointKind);
                break;
            }
            case ClearanceEndKind.AcrossRunway:
            {
                runwayTarget = endStep.Name;
                term = new ProgressiveTerminator(ProgressiveTerminatorType.AfterCrossingRunway, runwayTarget);
                if (!_crossRunwayMap.TryGetValue($"Runway {runwayTarget}", out var crossRwy)) break;
                if (endStep is { AtNodeIsPassedStop: true, AtNodeId: not 0 })
                {
                    // The crossing is already made (the step list kept it as the end, pinned to its stop): just across
                    // it is the far side of THAT stop. Never pinned back through the stop, which is behind the aircraft.
                    // The pin is its stop, or, for one made with no stop, the node before the runway (its near side); with
                    // none before it on the route, the reach (AtNodeIsReach), whose side is never trusted: refused.
                    var stop = graph.Nodes.TryGetValue(endStep.AtNodeId, out var stopNode) ? stopNode : null;
                    if (stop != null)
                    {
                        // Judged on the runway as the crossing classifier saw it (RunwayShape, [HLD-7]).
                        var centerline = RouteRunwayCrossings.FindCenterlineForDesignator(graph.RunwayCenterlines, runwayTarget);
                        bool aircraftClear = RouteRunwayCrossings.RunwayWithinClearMargin(
                            graph.RunwayCenterlines, _aircraftLat, _aircraftLon) == null;
                        var judged = ClearanceLegEnd.JudgeMadeCrossing(centerline == null ? null : RunwayShape.For(centerline),
                            stop.Latitude, stop.Longitude, _aircraftLat, _aircraftLon, aircraftClear,
                            pinIsReach: endStep.AtNodeIsReach);
                        if (judged == ClearanceLegEnd.MadeCrossingEnd.AlreadyAcross)
                        {
                            RefuseCalculate(ClearanceLegEnd.AlreadyAcross(runwayTarget));
                            return;
                        }
                        // A pin with no side (on the runway's pavement, a reach, no runway shape) is refused with what is
                        // true: the side cannot be told. Never "already across" (the aircraft may have backed off to the
                        // near side), never sent back over the runway. Else the far side of THAT crossing, away from the pin.
                        if (judged == ClearanceLegEnd.MadeCrossingEnd.Unknown)
                        {
                            RefuseCalculate(ClearanceLegEnd.NoSideToGoBy(runwayTarget));
                            return;
                        }
                        if (judged == ClearanceLegEnd.MadeCrossingEnd.FarSide)
                        {
                            int farSide = -Math.Sign(RunwayFrame.For(crossRwy, _aircraftLat).SignedCrossTrack(stop.Latitude, stop.Longitude));
                            destNode = RunwayCrossingResolver.FindAcross(graph, crossRwy, stop.NodeId)?.FarNodeId
                                       ?? FindFarSideRunwayNode(crossRwy, farSideSign: farSide)?.NodeId
                                       ?? -1;
                        }
                    }
                    break;
                }
                // A named HOLDING POINT as the crossing point ("cross 23R at P1"): the crossing from that point's own
                // node, which also finds the far side when the taxiway changes name on the way across. Re-resolved
                // here (not cached from the list) because the aircraft may have moved since the list was built.
                if (plan.CrossAtHoldingPointNodeId is int pin)
                {
                    crossAtHold = new NamedHoldingPoint { Name = plan.CrossAtHoldingPointName ?? "", NodeId = pin };
                    var match = GetHoldingPointsCrossingRunway(crossRwy).FirstOrDefault(c => c.Point.NodeId == pin);
                    if (match.Point != null) destNode = match.Crossing.FarNodeId;
                    break;
                }
                // An "at" taxiway pins the crossing point when ATC names one ("cross runway 27 at Tango"); no "at" =
                // the nearest crossing.
                crossAtTaxiway = string.IsNullOrWhiteSpace(endStep.At) ? null : graph.ResolveTaxiwayName(endStep.At);
                destNode = FindFarSideRunwayNode(crossRwy, crossAtTaxiway)?.NodeId ?? -1;
                break;
            }
            default:   // EndOfTaxiway: the last step is a Via step, so there is a last Via taxiway.
            {
                term = new ProgressiveTerminator(ProgressiveTerminatorType.EndOfTaxiway, "");
                if (lastVia == null) break;
                // Where the aircraft joins it and the way it goes on it: the junction with the taxiway before it, only
                // when the aircraft need not turn round for it and is not on the last taxiway already; else its node
                // nearest ahead, the way the aircraft is heading.
                var entry = ClearanceLegEnd.EntryOntoTaxiway(graph, lastVia, sequence.Count >= 2 ? sequence[^2] : null,
                    component, Distances(), NeedsReversal, _aircraftLat, _aircraftLon, headingTrue);
                if (entry.NodeId < 0) break;
                var end = ClearanceLegEnd.EndOfTaxiway(graph, entry.NodeId, lastVia, entry.TravelBearingTrue,
                    _aircraftLat, _aircraftLon, entry.AircraftOnTaxiway);
                destNode = end.EndNodeId;
                if (destNode < 0 && end.ArmEndNodeId >= 0)
                    besideRunway = ProgressiveLegEnd.NoEndClearOfARunway(lastVia);
                break;
            }
        }

        if (destNode < 0)
        {
            string what = term.Type switch
            {
                ProgressiveTerminatorType.HoldAtNamedPoint => $"holding point {term.Target}",
                ProgressiveTerminatorType.HoldShortTaxiway => $"taxiway {taxiwayTarget}",
                ProgressiveTerminatorType.EndOfTaxiway => $"the end of taxiway {lastVia}",
                _ when crossAtHold != null => $"a crossing of runway {runwayTarget} at holding point {crossAtHold.Name}",
                _ when crossAtTaxiway != null => $"taxiway {crossAtTaxiway} crossing runway {runwayTarget}",
                ProgressiveTerminatorType.HoldShortRunway when holdShortAt != null => $"runway {runwayTarget} at {holdShortAt}",
                _ => $"runway {runwayTarget}",
            };
            string from = lastVia ?? "your position";
            RefuseCalculate(besideRunway ?? $"Could not find {what} from {from}. Check your entry.");
            return;
        }

        string destName = term.Type switch
        {
            ProgressiveTerminatorType.HoldShortRunway => $"hold short of runway {runwayTarget}",
            ProgressiveTerminatorType.HoldShortTaxiway => $"hold short of taxiway {taxiwayTarget}",
            ProgressiveTerminatorType.AfterCrossingRunway => crossAtHold != null
                ? $"across runway {runwayTarget} at {crossAtHold.Name}"
                : $"across runway {runwayTarget}",
            ProgressiveTerminatorType.HoldAtNamedPoint => $"holding point {term.Target}",
            _ => $"end of taxiway {lastVia}",
        };

        // Sizes the setback of a taxiway stop; _aircraftWingspan is in feet, 0 when unknown.
        _guidanceManager.AircraftWingspanMetres = _aircraftWingspan * 0.3048;
        string? error = _guidanceManager.LoadRoute(
            _dataProvider, _currentIcao,
            _aircraftLat, _aircraftLon, headingTrue,
            destNode, destName,
            plan.TaxiwaySequence.Count > 0 ? plan.TaxiwaySequence.ToList() : null,
            userHoldShortIndices: null,
            destinationHeading: null,
            destinationThresholdLat: null, destinationThresholdLon: null,
            destinationHeadingTrue: null,
            isRunwayDestination: false,
            prebuiltGraph: graph,
            // The Hold short of steps on the way (not the one that ends the leg), keyed by the Via step each was
            // entered after (-1: none), a runway's with its place. A leg with no destination now takes taxiway and
            // holding-point stops too.
            userRunwayHoldShorts: plan.RunwayHoldPicks(),
            userTaxiwayHoldShorts: PlanStops(plan, ClearanceTargetKind.Taxiway, s => s.Name),
            progressiveTerminator: term,
            // Crossing at a named holding point to end the leg: pin the route THROUGH that painted line (the pin a
            // named-holding-point departure uses), so the pilot crosses where ATC said and not at a neighbouring
            // connector. Null for every other leg, and for a crossing already made (its stop is behind).
            holdingPointHoldNodeId: plan.CrossAtHoldingPointNodeId,
            holdingPointName: plan.CrossAtHoldingPointName,
            userHoldingPointHoldShorts: PlanStops(plan, ClearanceTargetKind.HoldingPoint,
                s => new HoldingPointHoldShort(s.Name, s.NodeId)),
            userTurnPicks: PlanTurnPicks(plan),
            // Every Cross runway step, the leg's end among them: cleared at its place and only there; the manager
            // clears its stop after placing every stop, and says one it cannot clear ([CLR-1]).
            clearedCrossings: plan.Crossings.Count > 0 ? plan.Crossings : null,
            // The leg-end meet scan and a Turn pick's end keep the meeting beyond the crossed taxiway chosen above.
            legEndCrossedTaxiways: plan.LegEndCrossedTaxiways.Count > 0 ? plan.LegEndCrossedTaxiways : null);

        if (error != null)
        {
            // A refused leg can leave the PREVIOUS route still live (RestoreLoadRouteRollback), so the summary box
            // keeps it (HasLiveRoute, the manager's live guidance STATE: PR #238 review, Important 3).
            RefuseCalculate(error);
            return;
        }

        // A progressive leg is never a gate/runway lineup — clear any prior
        // docking target so a stale gate doesn't engage near the terminator.
        _dockingManager?.SetDestinationGate(null);
        txtRouteSummary.Text = _guidanceManager.LastRouteSummary;
        lblStatus.Text = "Route loaded. Guidance active.";
        // Recorded just before guidance starts, so a leg that ends as it starts is never ignored (RecordCalculatedSteps).
        RecordCalculatedSteps(hasDestination: false);
        _guidanceManager.StartGuidance(SettingsManager.Current);
        SpeakCalculateUtterance(plan);
    }

    /// <summary>
    /// Everything said at Calculate once guidance has started: ONE utterance, warnings first ([HLD-17], [SI-22]), for
    /// a destination and a leg with no destination alike. It must come after StartGuidance (whose first-taxiway
    /// callout would otherwise stomp it: the reach warning was showing in the box but never heard at Calculate,
    /// in-sim 2026-06-13, when spoken before StartGuidance), and it must be a SINGLE AnnounceImmediate: consecutive
    /// calls stomp each other. In order: an imported route's summary, a ticked backtrack the airport could not
    /// honour, the route's own warnings (<c>LastRouteStartWarning</c>, #355's Turn pick notes among them), the
    /// clearance's checks (<c>CheckClearance</c>: a Cross taxiway step the route never meets), each crossing the route
    /// clears (<c>DescribeClearedCrossings</c>: "Cleared across runway 24L at P."), this departure's
    /// intersection or holding point, the unmapped-start warning, the reach warning or else the route-start turn
    /// cue, the backtrack advisory, and last the start-hold sentence, the instruction to act on. (The Calculate
    /// sentence itself, the route summary, is queued by LoadRoute.)
    /// </summary>
    private void SpeakCalculateUtterance(ClearancePlan plan, string? backtrackFallbackNote = null,
        string? intersectionLine = null, string? holdingPointLine = null, string? backtrackAdvisory = null)
    {
        var parts = new List<string>();
        // An imported (SayIntentions) route's summary rides at the FRONT of this same utterance —
        // see StartImportedRoute. Front, because the reach warning and the start-hold sentence stay
        // the last things said; the import summary leads with its own warnings for the same reason.
        string? imported = _importSummary?.Invoke(true);
        if (!string.IsNullOrEmpty(imported)) parts.Add(imported);
        // A ticked backtrack the airport could not honour — said before the route
        // details, because it changes what the pilot should expect to happen next.
        if (backtrackFallbackNote != null) parts.Add(backtrackFallbackNote);
        // Warnings the route itself carries (cannot be joined from here, a runway with no hold short point,
        // a hold-short pick that could not be set, a crossing that could not be cleared, the Turn pick notes).
        // They are in the spoken summary too, but StartGuidance's first callout cuts that off — the same
        // lesson as the reach warning below.
        if (!string.IsNullOrEmpty(_guidanceManager.LastRouteStartWarning))
            parts.Add(_guidanceManager.LastRouteStartWarning);
        // What the clearance asked that the route does not do: "The route does not cross taxiway B."
        parts.AddRange(_guidanceManager.CheckClearance(plan));
        // Each crossing a Cross step cleared, after the warnings: "Cleared across runway 24L at P." The summary names it
        // too, but StartGuidance's first callout cuts that, and this is how the pilot hears BA will not stop there.
        string clearedCrossings = _guidanceManager.DescribeClearedCrossings();
        if (clearedCrossings.Length > 0) parts.Add(clearedCrossings);
        if (intersectionLine != null) parts.Add(intersectionLine);
        if (holdingPointLine != null) parts.Add(holdingPointLine);
        // The unmapped-start warning -- "Your position isn't connected ..." when the aircraft
        // leaves a disconnected position, or "{name} isn't connected ..." when the destination
        // itself is on a piece of network the aircraft is not on -- rides in this same
        // utterance, ahead of the turn-cue / reach-warning slot below, so that slot stays last.
        // Consumed unconditionally, so the per-frame one-shot can never repeat it.
        string? unmappedStart = _guidanceManager.ConsumeUnmappedStartWarning();
        if (!string.IsNullOrEmpty(unmappedStart))
            parts.Add(unmappedStart);
        // The route-start turn cue rides INSIDE this one utterance rather than interrupting
        // it. Live KATL 2026-08-27: it fired as its own AnnounceImmediate 50 ms after this
        // block spoke, and cut the SayIntentions import summary off mid-word -- the fifth
        // time two announcements at Calculate have stomped each other here.
        //
        // The two are MUTUALLY EXCLUSIVE, and that is the design, not an accident of this
        // if/else: whichever applies is said last, before only a start-hold sentence. A reach warning means the route
        // never gets to the runway, so the pilot will reprogram and a turn cue is both moot
        // and extra words in front of the warning -- exactly the suppression the per-frame
        // one-shot has always applied. Never "fix" this into two Add calls: that reinstates
        // an utterance the design deliberately excludes. (LastRouteReachWarning is set only for
        // runway destinations.)
        //
        // The cue is consumed UNCONDITIONALLY, warning present or not, so the per-frame
        // one-shot can never repeat it behind this utterance's back.
        string? turnCue = _guidanceManager.ConsumeInitialTurnCue();
        if (!string.IsNullOrEmpty(_guidanceManager.LastRouteReachWarning))
        {
            parts.Add(_guidanceManager.LastRouteReachWarning);
        }
        else if (!string.IsNullOrEmpty(turnCue))
        {
            parts.Add(turnCue);
        }
        if (backtrackAdvisory != null) parts.Add(backtrackAdvisory);
        // A route that begins at a runway hold line started HELD. Its hold sentence is the LAST part of
        // this one utterance — it is what the pilot must act on — and is consumed here, because
        // MainForm feeds guidance no frames while it holds and nothing else would say it.
        string? startHoldCue = _guidanceManager.ConsumeStartHoldCue();
        if (!string.IsNullOrEmpty(startHoldCue))
            parts.Add(startHoldCue);
        if (parts.Count > 0)
            _announcer.AnnounceImmediate(string.Join(" ", parts));
        // (The old separate second AnnounceImmediate for the intersection
        // confirmation was removed: consecutive AnnounceImmediate calls stomp
        // each other — per the comment above — so it silently swallowed the
        // reach warning whenever an intersection was chosen.)
    }

    /// <summary>"Gate, stand or parking" always asks (an identifier-less spot's "could not prepare" is news the
    /// pilot should hear). A Place asks only for a stand GSX published — otherwise every FBO or hangar
    /// route ended in a false "could not prepare". Runway, progressive and de-ice never ask.</summary>
    internal static bool ShouldSendGateSelect(int destinationTypeIndex, ParkingSpot? spot) => spot != null && destinationTypeIndex switch
    {
        1 => true,
        4 => !string.IsNullOrEmpty(spot.GsxIdentifier),
        _ => false,
    };

    /// <summary>
    /// Sends <c>gate.select</c> for <paramref name="spot"/> via <see cref="_gsxGateSelector"/>
    /// and speaks the outcomes a blind pilot cannot learn any other way — see
    /// <see cref="Services.Gsx.Remote.GsxGateSelectAnnouncer"/> for exactly which outcomes
    /// that is and why. Uses the QUEUED announcer (<c>Announce</c>), never
    /// <c>AnnounceImmediate</c>: this is a background GSX decision arriving some time after
    /// the Calculate click, not a direct UI interaction, and it must not interrupt whatever
    /// the pilot is already hearing (the route summary, a taxi callout, …). Never throws —
    /// <see cref="Services.Gsx.Remote.GsxRemoteGateSelector.SelectGateAsync"/> itself never
    /// throws, and <see cref="Services.Gsx.Remote.GsxGateSelectAnnouncer.Describe"/> is pure
    /// string logic over an already-parsed result.
    /// </summary>
    private async Task SelectGsxGateAsync(ParkingSpot spot)
    {
        var result = await _gsxGateSelector!.SelectGateAsync(spot).ConfigureAwait(true);

        // GSX is connected and answering, but its capability list has no 'gate' token —
        // a 4.0.1-4.0.7 build, where gate.select simply does not exist. Say so ONCE per
        // form instance (see the latch field for why that is per app session): the first
        // Calculate is an explicit pilot action that deserves an answer (silence here means
        // taxiing to a stand believing GSX has prepared it, and finding no services on
        // arrival — the same reasoning that gives Prepared/NotFound/BadArgs their own
        // phrases in GsxGateSelectAnnouncer), but this path runs on EVERY
        // gate-destination route calculation, and on an older GSX that is every flight —
        // repeating it would be noise about something already said and unchanged.
        // Deliberately NOT routed through GsxGateSelectAnnouncer.Describe: that mapper is
        // pure and stateless so it stays unit-testable, so the latch belongs here.
        // Everything else about GSX still works on those builds, so this must never reach
        // GsxService.UnavailableReason or the Access GSX status text — announcing "GSX is
        // unavailable" while the GSX window works perfectly would be wrong.
        if (result.Outcome == Services.Gsx.Remote.GsxGateSelectOutcome.GateSelectUnsupported)
        {
            // No lock needed: SelectGsxGateAsync is started from the UI thread and awaits
            // with ConfigureAwait(true), so the test and the set run in one message-loop
            // turn — two overlapping Calculates cannot both pass the check.
            if (!_gsxUnsupportedAnnounced)
            {
                _gsxUnsupportedAnnounced = true;
                _announcer.Announce(Services.Gsx.Remote.GsxGateSelectAnnouncer.GateSelectUnsupportedMessage);
            }
            return;
        }

        string? phrase = Services.Gsx.Remote.GsxGateSelectAnnouncer.Describe(result);
        if (!string.IsNullOrEmpty(phrase))
        {
            _announcer.Announce(phrase);
            // Window route: this form's own announcer speaks it directly, so unlike the
            // GsxService path there is no background/no-listener ambiguity to record.
            Services.Gsx.Remote.GsxDiagnosticLog.Spoke(
                Services.Gsx.Remote.GsxSpeechSource.GateSelect, phrase, Services.Gsx.Remote.SpeechRoute.Background);
        }
        else
        {
            // Several outcomes are silent BY DESIGN (no_airport, a repeat services_active,
            // gsx_not_running/auth_required/Unavailable/TransportFailure). gsx-gate-select.log
            // records what GSX answered, but only this says we then chose to say nothing —
            // without it, "I picked a gate and heard nothing" cannot be told apart from a
            // phrase that was produced and lost. One line per selection, not per tick.
            Services.Gsx.Remote.GsxDiagnosticLog.Hushed(
                Services.Gsx.Remote.GsxSpeechSource.GateSelect, string.Empty, "outcome-map",
                $"outcome {result.Outcome} has no phrase (silent by design)");
        }
    }

    private void CheckGateOccupancy(bool isRunwayDest, double? gateLat, double? gateLon)
    {
        if (isRunwayDest || _tcasService == null || gateLat == null || gateLon == null) return;

        // Force a fresh SimConnect traffic request before reading the snapshot.
        // TcasService's own 3-second poll timer may have just ticked; without
        // PollNow() the snapshot could be up to ~3 s stale when the user clicks
        // Calculate, and an aircraft that just spawned at the gate would be missed.
        // The request is asynchronous so a brand-new occupant within the last
        // few hundred ms can still slip through, but the staleness window
        // shrinks from "up to 3 s" to "one SimConnect roundtrip ≈ 33 ms".
        _tcasService.PollNow();

        var occupying = SpotIsOccupied(gateLat.Value, gateLon.Value);

        if (occupying == null) return;

        string who = string.IsNullOrWhiteSpace(occupying.Callsign)
            ? "an aircraft"
            : occupying.Callsign;
        // AnnounceImmediate — the very next line in OnCalculateClicked is
        // StartGuidance, which announces the (long) route summary. A queued
        // gate warning would be drowned out behind it.
        _announcer.AnnounceImmediate($"Warning: {who} is at the destination gate.");
    }

    /// <summary>
    /// Resolves the hold-short node for Progressive Taxi's "hold short of runway"
    /// terminator: the graph node nearest the chosen runway's centerline on the
    /// aircraft's OWN side (the side it is currently on), restricted to the
    /// aircraft's connected component. Routing the leg to THIS node ends guidance
    /// at the hold line (where the manager's ProgressiveHold fires) rather than
    /// past the runway. Returns null if no near-side node is found (caller treats
    /// that as the "could not find" mismatch).
    ///
    /// A node that actually lies on <paramref name="lastTaxiway"/> (the last cleared
    /// taxiway, where "via …, hold short of RWY" should end) is PREFERRED over the
    /// global nearest-centerline node: without this, a complex/parallel airport can
    /// pin the hold to a node on a DIFFERENT taxiway far down the runway, forcing the
    /// constrained router to detour off the cleared sequence. The global nearest is
    /// kept as a fallback when the cleared taxiway has no qualifying node, so this is
    /// never worse than the prior unanchored scan.
    ///
    /// Geometry mirrors <see cref="FindFarSideRunwayNode"/> but selects the
    /// aircraft's side and minimises lateral distance to the centerline so the
    /// chosen node sits just before the runway.
    /// </summary>
    private TaxiNode? ResolveHoldShortRunwayNode(string runwayDesignator, string? lastTaxiway)
    {
        if (_graph == null) return null;
        if (!_crossRunwayMap.TryGetValue($"Runway {runwayDesignator}", out var runway))
            return null;

        // Delegated to the pure Navigation helper (probe-tested) so the scenery's
        // DESIGNATED hold-short nodes are preferred over the geometric scan.
        // KSFO 2026-07-01: the geometric scan held the pilot at a plain Q node
        // ~157 m from the 28L centerline while the scenery's HSND hold line sits
        // at ~97 m; the helper picks the designated node closest to the runway.
        return HoldShortNodeResolver.ResolveNearSide(
            _graph, runway, _aircraftLat, _aircraftLon, _aircraftHeading, lastTaxiway);
    }

    /// <summary>
    /// Finds the nearest graph node on the opposite side of <paramref name="runway"/>
    /// from the aircraft's current position. Used by the Progressive Taxi "After
    /// crossing runway" terminator to produce a routing target that forces A*
    /// across the runway; the automatic runway hold pass then places the hold
    /// (which LoadRoute strips for the cleared crossing).
    ///
    /// If the aircraft is ON the runway (within half-width of the centerline), the
    /// aircraft's heading is used to determine the intended exit side.
    ///
    /// When <paramref name="crossAtTaxiway"/> is non-null, only far-side nodes lying
    /// on that taxiway are considered — used when ATC names the crossing point
    /// ("cross runway 27 at Tango"). Null restores the default behaviour (nearest
    /// reachable far-side node).
    ///
    /// <paramref name="farSideSign"/>, when given, is the far side itself (the sign of
    /// its cross-track): the side away from a crossing already made, whose stop the
    /// aircraft has passed, so an aircraft already on the runway or across it is never
    /// sent back over it.
    /// </summary>
    private TaxiNode? FindFarSideRunwayNode(Runway runway, string? crossAtTaxiway = null, int? farSideSign = null)
    {
        if (_graph == null) return null;

        // Shared runway-aligned projection frame (positive cross-track = LEFT
        // of the heading) + the legacy feet-as-metres hold setback — both live
        // on the Navigation helpers so the near-side resolver and this far-side
        // finder can never diverge on the math or the deliberate setback (see
        // HoldShortNodeResolver.LegacySetbackMetres for the do-not-"fix" note).
        var frame = RunwayFrame.For(runway, _aircraftLat);
        double minLateralM = HoldShortNodeResolver.LegacySetbackMetres(runway);
        double halfWidthTrueM = HoldShortNodeResolver.TrueHalfWidthMetres(runway);

        double acSignedCT = frame.SignedCrossTrack(_aircraftLat, _aircraftLon);

        // Determine which side to target. The on-runway test uses the TRUE
        // pavement half-width, not the legacy setback: an aircraft stopped at
        // a hold line (routinely INSIDE the legacy floor — KSFO: line ~90 m,
        // floor 97 m) is off the pavement and physically on a side, so the far
        // side is simply the opposite sign regardless of heading.
        int targetSign;
        if (farSideSign is int given)
        {
            targetSign = given;
        }
        else if (Math.Abs(acSignedCT) >= halfWidthTrueM)
        {
            // Aircraft is off the runway: far side has opposite sign
            targetSign = -Math.Sign(acSignedCT);
        }
        else
        {
            // Aircraft is on the runway: use heading to determine intended exit
            // side — the SHARED heuristic (see HoldShortNodeResolver.
            // HeadingExitSideSign; both operands in the magnetic frame that
            // _aircraftHeading, PLANE HEADING DEGREES MAGNETIC, lives in).
            targetSign = HoldShortNodeResolver.HeadingExitSideSign(runway, _aircraftHeading);
        }

        // Search geometry bounds — shared with HoldShortNodeResolver so the
        // near-side and far-side finders scan the same corridor.
        const double MAX_LATERAL_M = HoldShortNodeResolver.MAX_LATERAL_M;
        const double MAX_ALONG_PAST_END_M = HoldShortNodeResolver.MAX_ALONG_PAST_END_M;

        // Restrict candidates to the aircraft's own connected component so the
        // chosen far node is actually reachable. Without this, the nearest
        // far-side node can land in an isolated navdata island (e.g. GCLP S5)
        // and LoadRoute then fails with the generic "Could not calculate a
        // route." When the far side is a genuinely separate component this
        // leaves bestNode null, so the caller surfaces the specific
        // "far side of runway X" message instead — a better diagnostic.
        int? aircraftComponentId = _graph.FindNearestNode(_aircraftLat, _aircraftLon)?.ComponentId;

        TaxiNode? bestNode = null;
        double bestDist = double.MaxValue;

        foreach (var node in _graph.Nodes.Values)
        {
            if (aircraftComponentId.HasValue && node.ComponentId != aircraftComponentId.Value) continue;

            // Pin the crossing to an ATC-named taxiway when requested.
            if (crossAtTaxiway != null && !node.TaxiwayNames.Contains(crossAtTaxiway)) continue;

            double nodeSignedCT = frame.SignedCrossTrack(node.Latitude, node.Longitude);

            if (Math.Sign(nodeSignedCT) != targetSign) continue;
            if (Math.Abs(nodeSignedCT) < minLateralM) continue;
            if (Math.Abs(nodeSignedCT) > MAX_LATERAL_M) continue;

            // Along-track: must be within the runway's length + buffer
            double along = frame.Along(node.Latitude, node.Longitude);
            if (along < -MAX_ALONG_PAST_END_M) continue;
            if (along > frame.LengthM + MAX_ALONG_PAST_END_M) continue;

            double dist = TaxiGraph.CalculateDistanceMeters(
                _aircraftLat, _aircraftLon, node.Latitude, node.Longitude);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestNode = node;
            }
        }

        // The named taxiway changes NAME on the way across (EGCC: P1 → centreline → DZ1),
        // so no far-side node carries it. Only then, follow the network across from where
        // that taxiway meets the runway on the near side. A name-matched far node is kept
        // whenever one exists, so every crossing that resolved before resolves identically.
        if (bestNode == null && crossAtTaxiway != null)
        {
            foreach (int startId in RunwayCrossingResolver.TaxiwayCrossingStarts(_graph, runway, crossAtTaxiway, -targetSign))
            {
                if (aircraftComponentId.HasValue
                    && _graph.Nodes[startId].ComponentId != aircraftComponentId.Value) continue;
                if (RunwayCrossingResolver.FindAcross(_graph, runway, startId) is not { } crossing) continue;
                if (!_graph.Nodes.TryGetValue(crossing.FarNodeId, out var far)) continue;
                double dist = TaxiGraph.CalculateDistanceMeters(
                    _aircraftLat, _aircraftLon, far.Latitude, far.Longitude);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestNode = far;
                }
            }
        }

        return bestNode;
    }

    /// <summary>
    /// Named holding points from which the aircraft can genuinely cross
    /// <paramref name="runway"/> ("cross runway 23R at P1"), each with its resolved
    /// crossing. A point qualifies only when ALL hold:
    /// <list type="bullet">
    /// <item>It is not an intermediate hold — those guard taxiway junctions, not runways.</item>
    /// <item>It is the last line before the runway on its crossing — no hold-short node or other
    /// runway holding point lies between it and the pavement.</item>
    /// <item>Its nearest runway centreline is THIS runway (a hold between close parallels
    /// belongs to whichever runway it is nearer).</item>
    /// <item>It is on the aircraft's side (either side when the aircraft is on the pavement) and
    /// in the aircraft's connected component — a point across the runway cannot be where you
    /// start a crossing, and an island cannot be reached.</item>
    /// <item><see cref="RunwayCrossingResolver.FindAcross"/> finds a real crossing from its node.</item>
    /// </list>
    /// </summary>
    private List<(NamedHoldingPoint Point, RunwayCrossingResolver.Crossing Crossing)> GetHoldingPointsCrossingRunway(Runway runway)
    {
        var result = new List<(NamedHoldingPoint, RunwayCrossingResolver.Crossing)>();
        if (_graph == null || _namedHoldingPoints.Count == 0) return result;

        var frame = RunwayFrame.For(runway, runway.StartLat);
        double halfWidth = HoldShortNodeResolver.TrueHalfWidthMetres(runway);
        double acCt = frame.SignedCrossTrack(_aircraftLat, _aircraftLon);
        int acSide = Math.Abs(acCt) >= halfWidth ? Math.Sign(acCt) : 0;
        int? acComponent = _graph.FindNearestNode(_aircraftLat, _aircraftLon)?.ComponentId;
        // Sign-sourced points are approximate and are never a runway crossing point.
        static bool NotACrossingPoint(NamedHoldingPoint p) =>
            string.Equals(p.Kind, "intermediate", StringComparison.OrdinalIgnoreCase)
            || p.IsFromSign;
        var runwayHoldNodes = _namedHoldingPoints
            .Where(p => !NotACrossingPoint(p))
            .Select(p => p.NodeId)
            .ToHashSet();

        foreach (var hp in _namedHoldingPoints)
        {
            if (NotACrossingPoint(hp)) continue;
            if (!_graph.Nodes.TryGetValue(hp.NodeId, out var node)) continue;
            if (acComponent.HasValue && node.ComponentId != acComponent.Value) continue;

            double ct = frame.SignedCrossTrack(node.Latitude, node.Longitude);
            if (acSide != 0 && Math.Sign(ct) != acSide) continue;
            if (Math.Abs(ct) > RunwayCrossingResolver.MaxStartLateralM) continue;
            if (IsNearerAnotherRunway(node, runway, Math.Abs(ct))) continue;

            if (RunwayCrossingResolver.FindAcross(_graph, runway, hp.NodeId) is not { } crossing) continue;

            // Only the LAST line before the runway is a crossing point. A hold further back
            // (EGCC B2 behind B1, an ILS hold behind its runway hold) reaches the runway only
            // THROUGH that nearer line — ATC names the nearer one, and offering both would put
            // two "crossings" in the list for one piece of pavement.
            if (RunwayCrossingResolver.PassesNearSideHold(_graph, crossing)) continue;
            if (crossing.NearSideNodeIds.Any(runwayHoldNodes.Contains)) continue;

            result.Add((hp, crossing));
        }
        return result;
    }

    /// <summary>True when some OTHER runway's centreline is nearer the node than this runway's axis.</summary>
    private bool IsNearerAnotherRunway(TaxiNode node, Runway runway, double lateralToRunwayM)
    {
        if (_graph == null) return false;
        var frame = RunwayFrame.For(runway, runway.StartLat);
        foreach (var cl in _graph.RunwayCenterlines)
        {
            bool ours = Math.Abs(frame.SignedCrossTrack(cl.Lat1, cl.Lon1)) <= 30.0
                     && Math.Abs(frame.SignedCrossTrack(cl.Lat2, cl.Lon2)) <= 30.0;
            if (ours) continue;
            double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                node.Latitude, node.Longitude, cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2);
            if (perp < lateralToRunwayM) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the distinct named taxiways that physically cross
    /// <paramref name="runway"/>, for a Cross step's optional "At" box (and the name lists'
    /// "runways your last taxiway crosses first"), so the pilot can only choose a crossing
    /// point that actually exists. Sorted alphanumerically. The rule lives in
    /// <see cref="RunwayCrossingResolver.TaxiwaysCrossing"/> — an edge spanning the
    /// centreline, or taxiways meeting at a node ON the runway from both sides (EGCC P/DZ).
    /// </summary>
    private List<string> GetTaxiwaysCrossingRunway(Runway runway)
        => RunwayCrossingResolver.TaxiwaysCrossing(_graph, runway);

    /// <summary>
    /// Computes the GSX <c>.py</c> per-aircraft stop offset for <paramref name="spot"/> and
    /// feeds it to docking, so the stop moves to where GSX's VDGS would stop this airframe.
    /// Applies to ALL non-deice gates — <c>.ini</c> gates INCLUDED: the <c>.py</c>
    /// <c>customOffset</c> is GSX's per-aircraft adjustment layered ON TOP of the static
    /// <c>.ini</c>/navdata base (EDDF A66: 777 = 5.3 m, A380 = 6.3 m, base = 1.65 m), so a
    /// <c>.ini</c> stop position is NOT aircraft-exact on its own. Deice pads stay
    /// datum-aligned (no offset). Resolves the aircraft id from SimConnect ICAO + wingspan.
    /// Any miss (no profile / unknown aircraft / parse fail) yields
    /// <see cref="Services.Gsx.GsxOffset.Zero"/> — the safe base position. Never throws.
    /// </summary>
    private void ApplyGsxStopOffset(Database.Models.ParkingSpot? spot)
    {
        if (_dockingManager == null) return;

        // Default to base position; only a successful resolution moves the stop.
        var offset = Services.Gsx.GsxOffset.Zero;
        // Diagnostic breadcrumbs for the stop-offset chain (why stopOffL was 0 at runtime).
        string dIcaoType = "", dAcId = "";
        int dNumber = spot?.Number ?? -1;
        string dSuffix = spot?.Suffix ?? "<null>";
        bool dStopLatSet = spot?.StopLatitude != null;
        try
        {
            // Apply for BOTH navdata/.py gates (StopLatitude == null, base = parking centre)
            // AND .ini gates (StopLatitude set, base = the .ini gate position). The .py
            // customOffset is GSX's PER-AIRCRAFT stop adjustment, which GSX adds on top of the
            // static gate base regardless of source — that's why the same gate yields different
            // offsets per airframe (EDDF A66: 777=5.3 m, A380=6.3 m, base=1.65 m). The earlier
            // `StopLatitude == null` guard wrongly assumed the .ini base was already aircraft-
            // exact, so at every .ini airport (EDDF, etc.) the 777 parked ~5 m short of GSX's
            // real VDGS stop. Deice pads stay datum-aligned (no per-aircraft offset).
            if (spot != null
                && !spot.IsDeiceArea
                && _simConnectManager != null)
            {
                // Snapshot the aircraft identity once, adjacently. This runs UI-thread on the
                // Calculate click (not per-frame); each field is an atomic read on x64. The only
                // possible inconsistency is reading the ICAO and wingspan from across the ~1-frame
                // window of an aircraft swap — and that would only mis-pick the wingspan-derived
                // ARC group, which is the last-resort fallback (after ICAO and idMajor) and stays
                // within the safe |offset| band, so no lock is warranted.
                string icaoType = _simConnectManager.CurrentAircraftIcaoType;
                dIcaoType = icaoType ?? "<null>";
                double wingspanM = _simConnectManager.AircraftWingSpan > 0
                    ? _simConnectManager.AircraftWingSpan * 0.3048 // feet -> metres
                    : 0.0;
                if (!string.IsNullOrWhiteSpace(icaoType))
                {
                    // TryResolve always yields a usable id even when it returns false (idMajor
                    // not derived) — the raw ICAO can still hit an ICAO-keyed table, so we
                    // evaluate with whatever id it produced regardless of the bool.
                    Services.Gsx.GsxAircraftIdMap.TryResolve(icaoType, wingspanM, out var acId);
                    dAcId = $"{acId.Icao}/maj{acId.IdMajor}/min{acId.IdMinor}";
                    offset = _stopOffsetResolver.Resolve(_currentIcao, spot.Number, spot.Suffix, acId);
                }
            }
        }
        catch (Exception ex) { offset = Services.Gsx.GsxOffset.Zero; dAcId += $" EX:{ex.GetType().Name}"; }

        // One-line diagnostic so a live dock reveals exactly why the offset resolved the way it
        // did (airport icao, gate number/suffix as parsed, whether a .ini stop was present, the
        // resolved aircraft id, and the final offset). Never throws.
        try
        {
            _dockingAircraftLog.Info(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "STOPOFFSET  icao='{0}' gate#={1} suffix='{2}' stopLatSet={3} ac='{4}' acId={5} -> long={6:F2} lat={7:F2}",
                _currentIcao, dNumber, dSuffix, dStopLatSet, dIcaoType, dAcId,
                offset.LongitudinalMetres, offset.LateralMetres));
        }
        catch { }

        _dockingManager.SetStopOffset(offset);

        // Cue 2: use the gate's GSX gatedistancethreshold as the engage range when present.
        // Null for navdata-only and .py-only gates (no threshold) → keeps the 50 m default.
        _dockingManager.SetEngageRangeMetres(spot?.GateDistanceThreshold);
    }

    private void OnStopClicked(object? sender, EventArgs e)
    {
        _guidanceManager.StopGuidance();
        // The leg is over (spec 8.2): its steps go, silently; "Taxi guidance stopped." says what happened.
        ClearSteps();
        _dockingManager?.SetDestinationGate(null);
        _announcer.Announce("Taxi guidance stopped.");
        lblStatus.Text = "Guidance stopped.";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            // A step being changed is kept as it was: the window reopens on the list as trimmed, never mid-change.
            CancelStepChange(false);
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
