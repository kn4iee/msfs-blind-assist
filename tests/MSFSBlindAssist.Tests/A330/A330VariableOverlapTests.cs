using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The generated controls are added by <c>AddPanelVariables</c>, which runs before the other owners' hooks, so
/// its dedup (a taken key, a taken Continuous Type:Name) cannot see the names they add. These tests do not
/// depend on that order: they compare what the definition defines on its own, with an empty map, against
/// everything the embedded map would register, so a map regeneration that overlaps fails here at once.
/// </summary>
public class A330VariableOverlapTests
{
    private static A330ControlMap EmptyMap() => new();

    private static SimVarType TypeOf(A330ControlMap.ReadKind k) => k switch
    {
        A330ControlMap.ReadKind.SimVar => SimVarType.SimVar,
        A330ControlMap.ReadKind.InputEvent => SimVarType.InputEvent,
        _ => SimVarType.LVar,
    };

    /// <summary>Keys the definition defines itself, plus its Continuous Type:Name pairs, with no map.</summary>
    private static (HashSet<string> keys, HashSet<string> names) Own()
    {
        var vars = new IniA330Definition(EmptyMap()).GetVariables();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in vars.Values)
            if (d.UpdateFrequency == UpdateFrequency.Continuous) names.Add(d.Type + ":" + d.Name);
        return (vars.Keys.ToHashSet(), names);
    }

    private static List<string> KeyOverlaps(A330ControlMap map)
    {
        var (own, _) = Own();
        var clashes = new List<string>();
        foreach (var c in map.Controls)
        {
            if (own.Contains(c.Key)) clashes.Add(c.Key);
            for (int n = 1; n <= (c.Lamps?.Count ?? 0); n++)
                if (own.Contains($"{c.Key}_LAMP{n}")) clashes.Add($"{c.Key}_LAMP{n}");
        }
        return clashes;
    }

    private static List<string> NameOverlaps(A330ControlMap map)
    {
        var (_, own) = Own();
        var reads = new List<(string who, string read)>();
        foreach (var c in map.Controls)
        {
            if (!string.IsNullOrEmpty(c.Read)) reads.Add((c.Key, c.Read!));
            foreach (var lamp in c.Lamps ?? new())
                if (!string.IsNullOrWhiteSpace(lamp.Read)) reads.Add((c.Key + " lamp", lamp.Read));
        }
        var clashes = new List<string>();
        foreach (var (who, read) in reads)
        {
            var r = A330ControlMap.ParseReadKind(read);
            string pair = TypeOf(r.kind) + ":" + r.name;
            if (own.Contains(pair)) clashes.Add($"{who} reads {pair}");
        }
        return clashes;
    }

    [Fact]
    public void No_map_control_reuses_a_key_the_definition_defines_itself()
    {
        var clashes = KeyOverlaps(A330ControlMap.Embedded);
        Assert.True(clashes.Count == 0, "map keys the definition also defines: " + string.Join(", ", clashes));
    }

    [Fact]
    public void No_map_read_shares_a_continuous_name_with_the_definitions_own_variables()
    {
        var clashes = NameOverlaps(A330ControlMap.Embedded);
        Assert.True(clashes.Count == 0, "map reads a name the definition already watches: " + string.Join(", ", clashes));
    }

    private static A330ControlMap Overlapping() => new()
    {
        Controls =
        {
            new A330ControlMap.Control { Key = "A330_SPD_MACH", Kind = "select", Panel = "x", Label = "x", Read = "L:INI_SOMETHING_ELSE" },
            new A330ControlMap.Control { Key = "MAP_ONLY_KEY", Kind = "status", Panel = "x", Label = "y", Read = "L:INI_AIRSPEED_IS_MACH" },
            new A330ControlMap.Control
            {
                Key = "MAP_LAMP_KEY", Kind = "button", Panel = "x", Label = "z",
                Lamps = new() { new A330ControlMap.Lamp { Read = "L:INI_AT_MODE", Legend = "a" } },
            },
        },
    };

    // The same two checks, run on a map that does overlap: they must see it (each guard fails on such a map).
    [Fact]
    public void The_key_check_sees_an_overlap()
    {
        Assert.Contains("A330_SPD_MACH", KeyOverlaps(Overlapping()));
    }

    [Fact]
    public void The_name_check_sees_an_overlap_in_a_control_and_in_a_lamp()
    {
        var clashes = NameOverlaps(Overlapping());
        Assert.Contains(clashes, c => c.StartsWith("MAP_ONLY_KEY reads"));
        Assert.Contains(clashes, c => c.StartsWith("MAP_LAMP_KEY lamp reads"));
    }
}
