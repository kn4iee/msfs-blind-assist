using System.Buffers.Binary;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 EHSI sampler finds the Captain's nd in the gauge manager by its vptr, reads the screen's fields, the
/// route the top line uses (the pending modification while there is one) and the map's points through the layout, and
/// walks the route all or nothing: a torn vector, a pointer outside the module, a label that is too long, a position out
/// of range or more legs than the cap make one corrupt reading, never a partial route. A discontinuity is its own leg.</summary>
public class Bbs757EhsiReaderTests
{
    private const ulong Base = 0x20000000;
    private const int Size = 0x130000;
    private const uint Outside = 0x200000;

    // Where this test's memory puts each object; the statics (manager, registry, clock) are the sample layout's own.
    private const uint Nd = 0x10000, Efis = 0x11000, Source = 0x12000, Irs = 0x13000, Mcp = 0x14000, Route = 0x15000,
        ModRoute = 0x16000, Decoy = 0x17000, FoNd = 0x18000, Node = 0x19000, DecoyNode = 0x19100, Path = 0x1A000,
        Strings = 0x40000, Legs = 0x80000, ModLegs = 0xF0000, Flown = 0xF8000;
    private const uint NdSlot = 12 * 8, DecoySlot = 8 * 8, FoSlot = 14 * 8, DecoyVptr = 0x133d8, StringPitch = 0x100;
    private const int RouteId = 10;

    private static readonly Bbs757EhsiLayout L = Bbs757EhsiLayoutTests.Sample();

    /// <summary>One leg as BlueBird keeps it: kind, position, path terminator, constraint, ETA, label.</summary>
    private sealed record TLeg(string Label, int Kind, double Lat, double Lon, int Terminator, int CKind = 0, int C1 = 0,
        int C2 = 0, double Eta = -1);

    private static TLeg Kolm => new("KOLM", 4, 46.96941, -122.90254, 5, 1, 2300, 0, 1518.48);

    private static TLeg Discontinuity => new("", 3, 0, 0, 0);

    private static TLeg Olm => new("OLM", 1, 46.9, -122.9, 3);

    private static TLeg Btg => new("BTG", 5, 45.7, -122.5, 23, 2, 3000, 0);

    private static TLeg Kpdx => new("KPDX", 0, 45.5887, -122.5969, 1);

    /// <summary>The first live route: KOLM, a discontinuity, OLM, BTG, KPDX.</summary>
    private static TLeg[] Live() => new[] { Kolm, Discontinuity, Olm, Btg, Kpdx };

    /// <summary>One scene's memory: the sample layout's places, the objects at this test's addresses.</summary>
    private sealed class Scene
    {
        public bool Powered = true, Failed, Blanked, Centered, RangeChanging, Valid = true, Aligning, DataSwitch, TrackUp,
            TrueReference, RouteActive = true, Modified, ModificationPointer = true, HasIrs = true, HasRegistry = true,
            TopOfClimb = true, TopOfDescent = true;
        public int Mode = 2, ExcessCount = 3, EndOfDescent = -1;
        public double Range = 160, Number = 119.39, Latitude = 47.442946, Longitude = -122.303648, Bug = 147, GroundSpeed = 250,
            Clock = 1512.38;
        public string? Origin = "KSEA", Destination = "KPDX";
        public TLeg[] RouteLegs = Live(), FlownLegs = Array.Empty<TLeg>(), ModificationLegs = Array.Empty<TLeg>();
        public Action<byte[]>? Tweak;

        private uint _next = Strings;

        public byte[] Build()
        {
            var b = new byte[Size];
            I32(b, L.ManagerBase + DecoySlot, Decoy);
            I32(b, Decoy, DecoyVptr);
            I32(b, L.ManagerBase + NdSlot, Nd);
            I32(b, Nd, L.NdVptr);
            I32(b, L.ManagerBase + FoSlot, FoNd);   // the First Officer's nd: also carries the vptr, found second
            I32(b, FoNd, L.NdVptr);

            Flag(b, Nd + L.Powered, Powered);
            Flag(b, Nd + L.Failed, Failed);
            Flag(b, Nd + L.Blanked, Blanked);
            I32(b, Nd + L.NdToEfis, Efis);
            I32(b, Nd + L.NdToSource, Source);
            I32(b, Nd + L.NdToMcp, Mcp);
            I32(b, Nd + L.NdToRoute, Route);
            I32(b, Source + L.SourceToIrs, HasIrs ? Irs : 0);
            I32(b, Efis + L.EfisMode, Mode);
            Flag(b, Efis + L.EfisCentered, Centered);
            F64(b, Nd + L.Range, Range);
            Flag(b, Nd + L.RangeChanging, RangeChanging);
            Flag(b, Nd + L.Valid, Valid);
            Flag(b, Nd + L.Aligning, Aligning);
            F32(b, Nd + L.Number, (float)Number);
            I32(b, Nd + L.TrackUp, TrackUp ? 1 : 0);
            I32(b, Nd + L.TrueReference, TrueReference ? 1 : 0);
            F32(b, Nd + L.Width, 704);
            F32(b, Nd + L.Height, 859);
            F64(b, Nd + L.Longitude, Longitude);
            F64(b, Nd + L.Latitude, Latitude);
            F64(b, Nd + L.CenterLatitude, Latitude);
            F64(b, Nd + L.CenterLongitude, Longitude);
            F64(b, Nd + L.Rotation, 134.43);
            F64(b, Nd + L.ScreenX, 352);
            F64(b, Nd + L.ScreenY, 702.2);
            Flag(b, Nd + L.DataSwitch, DataSwitch);
            I32(b, Nd + L.ExcessCount, ExcessCount);
            F64(b, Mcp + L.McpHeading, Bug);
            F64(b, Irs + L.IrsGroundSpeed, GroundSpeed);
            F64(b, L.Clock, Clock);
            if (Origin is not null) Airport(b, Nd + L.OriginAirport, Origin, 47.4499, -122.3118);
            if (Destination is not null) Airport(b, Nd + L.DestinationAirport, Destination, 45.5887, -122.5969);

            PlaceRoute(b, Route, RouteLegs, FlownLegs, RouteActive, Modified, ModificationPointer ? ModRoute : 0);
            PlaceRoute(b, ModRoute, ModificationLegs, Array.Empty<TLeg>(), true, false, 0, legsAt: ModLegs);

            // The path registry: a decoy entry for another route first, then this route's.
            if (HasRegistry)
            {
                I32(b, L.PathRegistry + L.PathRegistryFirst, DecoyNode);
                I32(b, DecoyNode, Node);
                I32(b, DecoyNode + L.PathNodeKey, 99);
                I32(b, DecoyNode + L.PathNodeValue, Decoy);
                I32(b, Node, 0);
                I32(b, Node + L.PathNodeKey, RouteId);
                I32(b, Node + L.PathNodeValue, Path);
            }
            Flag(b, Path + L.TopOfClimbFlag, TopOfClimb);
            F64(b, Path + L.TopOfClimbLatitude, 47.13824);
            F64(b, Path + L.TopOfClimbLongitude, -122.68684);
            Flag(b, Path + L.TopOfDescentFlagA, TopOfDescent);
            Flag(b, Path + L.TopOfDescentFlagB, TopOfDescent);
            F64(b, Path + L.TopOfDescentLatitude, 46.6855);
            F64(b, Path + L.TopOfDescentLongitude, -122.8291);
            I32(b, Path + L.EndOfDescentLeg, EndOfDescent);
            Tweak?.Invoke(b);
            return b;
        }

        private void PlaceRoute(byte[] b, long route, TLeg[] legs, TLeg[] flown, bool active, bool modified, uint modification,
            uint legsAt = Legs)
        {
            Flag(b, route + L.RouteActive, active);
            Flag(b, route + L.RouteModified, modified);
            I32(b, route + L.RouteModification, modification);
            I32(b, route + L.RouteId, RouteId);
            PlaceVector(b, route + L.RouteLegs, legsAt, legs);
            PlaceVector(b, route + L.RouteFlownLegs, Flown, flown);
        }

        private void PlaceVector(byte[] b, long header, uint at, TLeg[] legs)
        {
            uint end = at + (uint)(legs.Length * L.LegStride);
            I32(b, header, legs.Length == 0 ? 0 : at);
            I32(b, header + 4, legs.Length == 0 ? 0 : end);
            I32(b, header + 8, legs.Length == 0 ? 0 : end);
            for (int i = 0; i < legs.Length; i++) PlaceLeg(b, at + (uint)(i * L.LegStride), legs[i]);
        }

        private void PlaceLeg(byte[] b, long at, TLeg t)
        {
            I32(b, at + L.LegKind, t.Kind);
            var special = L.LegPositions.Where(p => p.Kind == t.Kind).Select(p => (Bbs757EhsiLegPosition?)p).FirstOrDefault();
            if (special is { } p)
            {
                F64(b, at + L.LegLatitude, 500);
                F64(b, at + L.LegLongitude, 500);
                F64(b, at + p.Latitude, t.Lat);
                F64(b, at + p.Longitude, t.Lon);
            }
            else
            {
                // A discontinuity's own position fields hold whatever BlueBird left: never range-checked.
                bool disc = t.Kind == L.DiscontinuityKind;
                F64(b, at + L.LegLatitude, disc ? 999 : t.Lat);
                F64(b, at + L.LegLongitude, disc ? 999 : t.Lon);
            }
            I32(b, at + L.LegTerminator, t.Terminator);
            I32(b, at + L.LegConstraintKind, t.CKind);
            I32(b, at + L.LegConstraintFirst, t.C1);
            I32(b, at + L.LegConstraintSecond, t.C2);
            F64(b, at + L.LegEta, t.Eta);
            PutString(b, at + L.LegLabel, t.Label);
        }

        private void Airport(byte[] b, long at, string name, double lat, double lon)
        {
            PutString(b, at, name);
            F64(b, at + L.AirportLatitude, lat);
            F64(b, at + L.AirportLongitude, lon);
        }

        /// <summary>A libc++ std::u32string (wasm32): the short form holds at most one code point inline after its size
        /// byte; the long form is (capacity with the long bit, size, data pointer).</summary>
        private void PutString(byte[] b, long at, string text)
        {
            var runes = text.EnumerateRunes().ToArray();
            if (runes.Length <= 1)
            {
                b[at] = (byte)(runes.Length << 1);
                if (runes.Length == 1) I32(b, at + 4, runes[0].Value);
                return;
            }
            uint data = _next;
            _next += StringPitch;
            for (int k = 0; k < runes.Length; k++) I32(b, data + 4 * (uint)k, runes[k].Value);
            I32(b, at, ((runes.Length + 1) << 1) | 1);
            I32(b, at + 4, runes.Length);
            I32(b, at + 8, data);
        }
    }

    // ---- memory ----

    private static void I32(byte[] b, long at, long v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(checked((int)at)), unchecked((int)v));

    private static void F64(byte[] b, long at, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan(checked((int)at)), v);

    private static void F32(byte[] b, long at, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(checked((int)at)), v);

    private static void Flag(byte[] b, long at, bool v) => b[checked((int)at)] = v ? (byte)1 : (byte)0;

    private static FakeProcessMemory Backing(byte[] bytes) =>
        new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);

    private static Bbs757EhsiReader Reader(byte[] bytes, bool full, FakeProcessMemory? src = null)
    {
        src ??= Backing(bytes);
        return new Bbs757EhsiReader(new ModuleMemory(src, Base), src, L) { FullWanted = () => full };
    }

    private static Bbs757EhsiReading Read(Scene scene, bool full = true)
    {
        using var r = Reader(scene.Build(), full);
        Assert.True(r.TryRead(out var reading));
        return reading;
    }

    private static Bbs757EhsiReading Corrupt(Scene scene, bool full = true)
    {
        var reading = Read(scene, full);
        Assert.True(reading.Corrupt);
        // All or nothing: never a partial route, modification or point list.
        Assert.Null(reading.Active);
        Assert.Equal(full, reading.Route is not null && reading.Route.Count == 0);
        Assert.Null(reading.Modification);
        Assert.Equal(full, reading.Points is not null && reading.Points.Count == 0);
        return reading;
    }

    // ---- the light set ----

    [Fact]
    public void Reads_the_light_set_every_time()
    {
        var reading = Read(new Scene(), full: false);
        Assert.False(reading.Corrupt);
        Assert.True(reading.Powered);
        Assert.Equal("MAP", reading.Mode);
        Assert.False(reading.Centered);
        Assert.Equal(160, reading.Range);
        Assert.False(reading.RangeChanging);
        Assert.True(reading.Valid);
        Assert.False(reading.Aligning);
        Assert.Equal((float)119.39, reading.Number);
        Assert.False(reading.TrackUp);
        Assert.False(reading.TrueReference);
        Assert.Equal(147, reading.HeadingBug);
        Assert.Equal(47.442946, reading.Latitude);
        Assert.Equal(-122.303648, reading.Longitude);
        Assert.Equal(250, reading.GroundSpeed);
        Assert.Equal(1512.38, reading.Clock);
        Assert.Equal(new Bbs757EhsiMap(47.442946, -122.303648, 134.43, 352, 702.2, 704, 859), reading.Map);
        Assert.True(reading.RouteActive);
        Assert.False(reading.DataSwitch);
        Assert.Equal(3, reading.ExcessCount);

        // The active leg is in every reading, with its constraint and ETA as stored.
        Assert.Equal(new Bbs757EhsiLeg("KOLM", 46.96941, -122.90254, false, 5, false, true, 1, 2300, 0, 1518.48), reading.Active);

        // The full set only when wanted.
        Assert.Null(reading.Route);
        Assert.Null(reading.Modification);
        Assert.Null(reading.Points);
    }

    [Fact]
    public void Takes_the_first_nd_in_the_manager()
    {
        // The First Officer's nd (later in the manager) is dark; the Captain's is the one read.
        var scene = new Scene { Tweak = b => Flag(b, FoNd + L.Powered, false) };
        Assert.True(Read(scene, full: false).Powered);
    }

    [Fact]
    public void Reads_the_route_only_when_full_is_wanted()
    {
        var scene = new Scene();
        var bytes = scene.Build();
        bool full = false;
        var src = Backing(bytes);
        using var r = new Bbs757EhsiReader(new ModuleMemory(src, Base), src, L) { FullWanted = () => full };

        Assert.True(r.TryRead(out var light));
        Assert.Null(light.Route);
        Assert.NotNull(light.Active);

        full = true;
        Assert.True(r.TryRead(out var all));
        Assert.False(all.Corrupt);
        Assert.Equal(new[] { "KOLM", "", "OLM", "BTG", "KPDX" }, all.Route!.Select(l => l.Label));
        Assert.Null(all.Modification);
        Assert.NotNull(all.Points);
        Assert.Equal(light with { Route = all.Route, Points = all.Points }, all);
    }

    [Fact]
    public void Reads_each_leg_as_drawn()
    {
        var route = Read(new Scene()).Route!;
        Assert.Equal(new Bbs757EhsiLeg("KOLM", 46.96941, -122.90254, false, 5, false, true, 1, 2300, 0, 1518.48), route[0]);
        // A navaid (kind 1) is at its alternate offsets, kind 5 at its own; the terminator 23 draws a symbol.
        Assert.Equal(new Bbs757EhsiLeg("OLM", 46.9, -122.9, false, 3, false, true, 0, 0, 0, -1), route[2]);
        Assert.Equal(new Bbs757EhsiLeg("BTG", 45.7, -122.5, false, 23, false, true, 2, 3000, 0, -1), route[3]);
        Assert.Equal(-122.5969, route[4].Longitude);
    }

    [Fact]
    public void A_leg_kind_with_its_own_position_reads_the_alternate_offsets()
    {
        var scene = new Scene { RouteLegs = new[] { new TLeg("ABC", 6, 12.5, 100.25, 2) } };
        var leg = Read(scene).Route!.Single();
        Assert.Equal((12.5, 100.25), (leg.Latitude, leg.Longitude));
        Assert.Equal(leg, Read(scene, full: false).Active);
    }

    [Fact]
    public void A_discontinuity_is_read_as_drawn()
    {
        var route = Read(new Scene()).Route!;
        // Its own leg between KOLM and OLM: no label, no symbol, position 0, 0 (its stored fields are never used), and
        // no leg invented across it.
        Assert.Equal(5, route.Count);
        Assert.Equal(new Bbs757EhsiLeg("", 0, 0, true, 0, false, false, 0, 0, 0, -1), route[1]);
        Assert.False(route[0].Discontinuity);
        Assert.False(route[2].Discontinuity);
        Assert.Equal(1, route.Count(l => l.Discontinuity));

        // A route starting on one has the discontinuity as leg 0: it is the active leg, nothing drawn for it.
        var first = Read(new Scene { RouteLegs = new[] { Discontinuity, Kolm } });
        Assert.True(first.Active!.Value.Discontinuity);
        Assert.False(first.Active.Value.Symbol);
    }

    [Fact]
    public void Terminators_say_manual_and_symbol()
    {
        TLeg Leg(int term) => new("X", 0, 1, 2, term);
        var scene = new Scene { RouteLegs = new[] { Leg(0), Leg(9), Leg(19), Leg(7), Leg(5) } };
        var route = Read(scene).Route!;
        Assert.Equal(new[] { false, true, true, false, false }, route.Select(l => l.Manual));
        Assert.Equal(new[] { false, false, false, true, true }, route.Select(l => l.Symbol));
    }

    [Fact]
    public void An_empty_route_has_no_active_leg()
    {
        var scene = new Scene { RouteLegs = Array.Empty<TLeg>(), HasRegistry = false };
        var light = Read(scene, full: false);
        Assert.False(light.Corrupt);
        Assert.Null(light.Active);
        var all = Read(scene);
        Assert.False(all.Corrupt);
        Assert.Empty(all.Route!);
    }

    [Fact]
    public void The_active_leg_is_the_routes_own_first_leg_not_a_flown_one()
    {
        var scene = new Scene { FlownLegs = new[] { Btg }, RouteLegs = new[] { Kolm, Kpdx } };
        Assert.Equal("KOLM", Read(scene, full: false).Active!.Value.Label);
        // The full route is the flown legs, then the route's own.
        Assert.Equal(new[] { "BTG", "KOLM", "KPDX" }, Read(scene).Route!.Select(l => l.Label));
    }

    // ---- a pending modification ----

    [Fact]
    public void A_pending_modification_is_the_active_route()
    {
        var scene = new Scene { Modified = true, ModificationLegs = new[] { Btg, Kpdx } };
        var light = Read(scene, full: false);
        Assert.Equal("BTG", light.Active!.Value.Label);
        Assert.Null(light.Modification);

        var all = Read(scene);
        Assert.Equal("BTG", all.Active!.Value.Label);
        Assert.Equal(new[] { "BTG", "KPDX" }, all.Modification!.Select(l => l.Label));
        Assert.Equal(5, all.Route!.Count);
    }

    [Fact]
    public void A_modification_flag_without_a_usable_route_is_no_modification()
    {
        var scene = new Scene { Modified = true, ModificationPointer = false, ModificationLegs = new[] { Btg } };
        var all = Read(scene);
        Assert.False(all.Corrupt);
        Assert.Equal("KOLM", all.Active!.Value.Label);
        Assert.Null(all.Modification);

        scene = new Scene { Modified = true, ModificationLegs = new[] { Btg }, Tweak = b => I32(b, Route + L.RouteModification, Outside) };
        all = Read(scene);
        Assert.False(all.Corrupt);
        Assert.Equal("KOLM", all.Active!.Value.Label);
        Assert.Null(all.Modification);
    }

    [Fact]
    public void A_set_pointer_with_the_flag_clear_is_no_modification()
    {
        var all = Read(new Scene { Modified = false, ModificationLegs = new[] { Btg } });
        Assert.Equal("KOLM", all.Active!.Value.Label);
        Assert.Null(all.Modification);
    }

    // ---- the points ----

    [Fact]
    public void Reads_the_vnav_points_and_the_airports()
    {
        var points = Read(new Scene { EndOfDescent = 2 }).Points!;
        Assert.Equal(new[]
        {
            new Bbs757EhsiPoint("T/C", false, 47.13824, -122.68684),
            new Bbs757EhsiPoint("T/D", false, 46.6855, -122.8291),
            new Bbs757EhsiPoint("E/D", false, 46.9, -122.9),          // leg 2 of the route: OLM
            new Bbs757EhsiPoint("KSEA", true, 47.4499, -122.3118),
            new Bbs757EhsiPoint("KPDX", true, 45.5887, -122.5969),
        }, points);
    }

    [Fact]
    public void Each_vnav_point_needs_its_own_flags()
    {
        var points = Read(new Scene { TopOfClimb = false }).Points!;
        Assert.Equal(new[] { "T/D", "KSEA", "KPDX" }, points.Select(p => p.Word));

        // T/D needs both of its flags.
        points = Read(new Scene { Tweak = b => Flag(b, Path + L.TopOfDescentFlagB, false) }).Points!;
        Assert.Equal(new[] { "T/C", "KSEA", "KPDX" }, points.Select(p => p.Word));
    }

    [Fact]
    public void No_path_entry_means_no_vnav_points_and_is_not_corrupt()
    {
        var all = Read(new Scene { HasRegistry = false });
        Assert.False(all.Corrupt);
        Assert.Equal(new[] { "KSEA", "KPDX" }, all.Points!.Select(p => p.Word));
        Assert.Equal(5, all.Route!.Count);

        // An empty registry (no first node) is the same.
        all = Read(new Scene { Tweak = b => I32(b, L.PathRegistry + L.PathRegistryFirst, 0) });
        Assert.False(all.Corrupt);
        Assert.Equal(new[] { "KSEA", "KPDX" }, all.Points!.Select(p => p.Word));
    }

    [Fact]
    public void An_airport_with_no_name_is_skipped()
    {
        var points = Read(new Scene { Origin = "", HasRegistry = false }).Points!;
        Assert.Equal(new[] { "KPDX" }, points.Select(p => p.Word));
    }

    [Fact]
    public void An_end_of_descent_beyond_the_legs_is_corrupt()
    {
        Corrupt(new Scene { EndOfDescent = 5 });
        Corrupt(new Scene { EndOfDescent = 200 });
    }

    [Fact]
    public void A_path_registry_that_never_ends_is_corrupt()
    {
        // A node that points at itself: the walk stops at its cap and the reading is corrupt, never a hang.
        Corrupt(new Scene { Tweak = b => { I32(b, Node, Node); I32(b, Node + L.PathNodeKey, 98); I32(b, DecoyNode, Node); } });
    }

    [Fact]
    public void A_path_node_outside_the_module_is_corrupt()
    {
        Corrupt(new Scene { Tweak = b => I32(b, DecoyNode, Outside) });
        Corrupt(new Scene { Tweak = b => I32(b, L.PathRegistry + L.PathRegistryFirst, Outside) });
        Corrupt(new Scene { Tweak = b => I32(b, Node + L.PathNodeValue, Outside) });
    }

    [Fact]
    public void The_light_reading_never_walks_the_path()
    {
        var scene = new Scene { Tweak = b => I32(b, L.PathRegistry + L.PathRegistryFirst, Outside) };
        Assert.False(Read(scene, full: false).Corrupt);
    }

    // ---- the screen ----

    [Fact]
    public void A_display_that_is_not_powered_reads_as_defaults_and_is_not_corrupt()
    {
        foreach (var scene in new[]
        {
            new Scene { Powered = false }, new Scene { Failed = true }, new Scene { Blanked = true },
        })
        {
            // Whatever a dark screen holds: an unknown mode, a route pointer outside the module.
            scene.Mode = 77;
            scene.Tweak = b => I32(b, Nd + L.NdToRoute, Outside);
            foreach (bool full in new[] { false, true })
            {
                var reading = Read(scene, full);
                Assert.False(reading.Corrupt);
                Assert.False(reading.Powered);
                Assert.Null(reading.Mode);
                Assert.Null(reading.Active);
                Assert.Null(reading.Route);
                Assert.Null(reading.Points);
                Assert.Equal(0, reading.Range);
                Assert.Equal(default, reading.Map);
            }
        }
    }

    [Fact]
    public void A_mode_with_no_word_is_corrupt()
    {
        Corrupt(new Scene { Mode = 77 }, full: false);
        Corrupt(new Scene { Mode = 77 });
    }

    [Fact]
    public void Each_mode_reads_its_word()
    {
        var words = new[] { 0, 1, 2, 3 }.Select(m => Read(new Scene { Mode = m }, full: false).Mode).ToArray();
        Assert.Equal(new[] { "VOR", "ILS", "MAP", "PLAN" }, words);
    }

    [Fact]
    public void Reads_the_flags_the_screen_draws()
    {
        var reading = Read(new Scene
        {
            Centered = true, RangeChanging = true, Valid = false, Aligning = true, DataSwitch = true, TrackUp = true,
            TrueReference = true, RouteActive = false, ExcessCount = 120,
        }, full: false);
        Assert.True(reading is
        {
            Centered: true, RangeChanging: true, Valid: false, Aligning: true, DataSwitch: true, TrackUp: true,
            TrueReference: true, RouteActive: false, ExcessCount: 120,
        });
    }

    [Fact]
    public void No_selected_irs_is_zero_ground_speed_and_not_valid_and_not_corrupt()
    {
        var reading = Read(new Scene { HasIrs = false }, full: false);
        Assert.False(reading.Corrupt);
        Assert.Equal(0, reading.GroundSpeed);
        Assert.False(reading.Valid);
    }

    [Fact]
    public void A_non_finite_value_the_screen_uses_is_corrupt()
    {
        Corrupt(new Scene { Range = double.NaN }, full: false);
        Corrupt(new Scene { Latitude = double.PositiveInfinity }, full: false);
        Corrupt(new Scene { Bug = double.NaN }, full: false);
    }

    // ---- the route walk ----

    [Fact]
    public void A_route_pointer_outside_the_module_is_corrupt()
    {
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToRoute, Outside) }, full: false);
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToRoute, Outside) });
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToRoute, 0) });
    }

    [Fact]
    public void An_object_pointer_outside_the_module_is_corrupt()
    {
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToEfis, Outside) }, full: false);
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToMcp, Outside) }, full: false);
        Corrupt(new Scene { Tweak = b => I32(b, Nd + L.NdToSource, Outside) }, full: false);
        Corrupt(new Scene { Tweak = b => I32(b, Source + L.SourceToIrs, Outside) }, full: false);
    }

    [Fact]
    public void A_leg_vector_outside_the_module_is_corrupt()
    {
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs, Outside) }, full: false);
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, Outside + 5 * L.LegStride) });
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteFlownLegs, Outside) });
    }

    [Fact]
    public void A_torn_leg_vector_is_corrupt()
    {
        // The end before the begin, an end that is not a whole number of legs, and a begin with no storage.
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, Legs - 4) });
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, Legs + 2 * L.LegStride + 4) });
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs, 0) });
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteFlownLegs + 4, Flown + 7) });
        // The light reading validates the vector it takes leg 0 from.
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, Legs + 2 * L.LegStride + 4) }, full: false);
        // A pending modification's vectors are walked the same way.
        Corrupt(new Scene
        {
            Modified = true, ModificationLegs = new[] { Btg },
            Tweak = b => I32(b, ModRoute + L.RouteLegs + 4, ModLegs + 100),
        });
    }

    [Fact]
    public void More_legs_than_the_cap_is_corrupt()
    {
        // The cap is the route's own and counts the flown legs too.
        uint end = Legs + (uint)((L.MaxLegs + 1) * L.LegStride);
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, end) });
        Corrupt(new Scene { Tweak = b => I32(b, Route + L.RouteLegs + 4, end) }, full: false);

        // Exactly at the cap is fine.
        var scene = new Scene { RouteLegs = Enumerable.Repeat(Kpdx, L.MaxLegs).ToArray(), HasRegistry = false };
        Assert.Equal(L.MaxLegs, Read(scene).Route!.Count);
        scene = new Scene { RouteLegs = Enumerable.Repeat(Kpdx, L.MaxLegs).ToArray(), FlownLegs = new[] { Kolm }, HasRegistry = false };
        Corrupt(scene);
    }

    [Fact]
    public void A_leg_label_that_is_too_long_or_unreadable_is_corrupt()
    {
        Assert.Equal(new string('A', 32), Read(new Scene { RouteLegs = new[] { Kolm with { Label = new string('A', 32) } } }).Route!.Single().Label);
        Corrupt(new Scene { RouteLegs = new[] { Kolm with { Label = new string('A', 33) } } });
        Corrupt(new Scene { RouteLegs = new[] { Kolm with { Label = new string('A', 33) } } }, full: false);

        // A long label whose data pointer is null or outside the module, or holds a surrogate.
        Corrupt(new Scene { Tweak = b => I32(b, Legs + L.LegLabel + 8, Outside) });
        Corrupt(new Scene { Tweak = b => I32(b, Legs + L.LegLabel + 8, 0) });
        // (The airports' names come first in the string area, so leg 0's label data is the third.)
        Corrupt(new Scene { Tweak = b => I32(b, Strings + 2 * StringPitch, 0xD800) });
    }

    [Fact]
    public void A_label_outside_the_first_leg_is_corrupt_in_the_full_reading_only()
    {
        void Break(byte[] b) => I32(b, Legs + 3 * L.LegStride + L.LegLabel + 8, Outside);
        Corrupt(new Scene { Tweak = Break });
        // The light reading reads only leg 0: the screen's top line does not touch the others.
        Assert.False(Read(new Scene { Tweak = Break }, full: false).Corrupt);
    }

    [Fact]
    public void A_short_and_a_long_label_both_read()
    {
        var scene = new Scene { RouteLegs = new[] { Kolm with { Label = "A" }, Kolm with { Label = "" }, Kolm with { Label = "KOLM" } } };
        Assert.Equal(new[] { "A", "", "KOLM" }, Read(scene).Route!.Select(l => l.Label));
    }

    [Fact]
    public void A_label_beyond_the_basic_plane_reads_whole()
    {
        var scene = new Scene { RouteLegs = new[] { Kolm with { Label = "A\U0001F600B" } } };
        Assert.Equal("A\U0001F600B", Read(scene).Route!.Single().Label);
    }

    [Fact]
    public void A_non_finite_or_out_of_range_position_is_corrupt()
    {
        foreach (var (lat, lon) in new[] { (double.NaN, 0.0), (0.0, double.PositiveInfinity), (91.0, 0.0), (-90.5, 0.0), (0.0, 180.5), (0.0, -181.0) })
            Corrupt(new Scene { RouteLegs = new[] { Kolm, Kolm with { Lat = lat, Lon = lon } } });

        // The edge itself is a position; the alternate offsets are checked too.
        var edge = Read(new Scene { RouteLegs = new[] { Kolm with { Lat = 90, Lon = -180 } } }).Route!.Single();
        Assert.Equal((90.0, -180.0), (edge.Latitude, edge.Longitude));
        Corrupt(new Scene { RouteLegs = new[] { Olm with { Lat = 95 } } });
        // Light: leg 0 only.
        Corrupt(new Scene { RouteLegs = new[] { Kolm with { Lat = 95 } } }, full: false);
        Assert.False(Read(new Scene { RouteLegs = new[] { Kolm, Kolm with { Lat = 95 } } }, full: false).Corrupt);
    }

    [Fact]
    public void A_non_finite_eta_is_corrupt()
    {
        Corrupt(new Scene { RouteLegs = new[] { Kolm with { Eta = double.NaN } } });
    }

    [Fact]
    public void A_point_out_of_range_is_corrupt()
    {
        Corrupt(new Scene { Tweak = b => F64(b, Path + L.TopOfClimbLatitude, 100) });
        Corrupt(new Scene { Tweak = b => F64(b, Nd + L.OriginAirport + L.AirportLongitude, 200) });
    }

    // ---- how it reads ----

    [Fact]
    public void Asks_about_each_region_once_per_reading()
    {
        var bytes = new Scene().Build();
        var src = Backing(bytes);
        using var r = new Bbs757EhsiReader(new ModuleMemory(src, Base), src, L) { FullWanted = () => true };
        Assert.True(r.TryRead(out _));
        int first = src.RegionQueries;
        Assert.InRange(first, 1, 2);
        Assert.True(r.TryRead(out _));
        Assert.Equal(first * 2, src.RegionQueries);
    }

    [Fact]
    public void An_nd_that_is_gone_is_a_failed_read()
    {
        // (Both nds' vptrs gone: the First Officer's would otherwise be found.)
        using var r = Reader(new Scene { Tweak = b => { I32(b, Nd, 1234); I32(b, FoNd, 1234); } }.Build(), full: false);
        Assert.False(r.TryRead(out _));
        using var none = Reader(new Scene { Tweak = b => { I32(b, L.ManagerBase + NdSlot, 0); I32(b, L.ManagerBase + FoSlot, 0); } }.Build(), full: false);
        Assert.False(none.TryRead(out _));
    }

    [Fact]
    public void Disposing_releases_the_lease()
    {
        var owned = new Disposed();
        using (var r = new Bbs757EhsiReader(new ModuleMemory(Backing(new byte[Size]), Base), owned, L)) { }
        Assert.Equal(1, owned.Count);
    }

    private sealed class Disposed : IDisposable
    {
        public int Count;

        public void Dispose() => Count++;
    }

    [Fact]
    public void Reads_nothing_from_the_simulator()
    {
        // The position comes from the nd: the default read with no simulator values is the monitor's read.
        var scene = new Scene();
        using var reader = Reader(scene.Build(), full: false);
        IBbs757DisplaySampler<Bbs757EhsiReading> r = reader;
        Assert.True(r.TryRead(new Bbs757SimValues(new Dictionary<string, double>()), out var withSim));
        Assert.True(r.TryRead(out var without));
        Assert.Equal(without, withSim);
    }
}
