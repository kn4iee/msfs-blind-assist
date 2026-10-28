using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One light's change, for <see cref="A350LampCallouts"/>.</summary>
public readonly record struct A350LampChange(string Name, bool On);

/// <summary>
/// The lights changed since the last sentence, as one sentence, so a power-up reads as a few sentences rather than a
/// dozen queued lines: one change as its own phrase ("Pack 1 fault light on"), several the same way counted ("2 lights
/// on: GEN 1A fault, GEN 1B fault"), lights coming on before lights going out. Pure.
/// </summary>
public static class A350LampCallouts
{
    public static string Compose(IReadOnlyList<A350LampChange> changes)
    {
        var parts = new List<string>(2);
        Add(parts, changes.Where(c => c.On).Select(c => c.Name).ToList(), "on");
        Add(parts, changes.Where(c => !c.On).Select(c => c.Name).ToList(), "off");
        return string.Join(". ", parts);
    }

    private static void Add(List<string> parts, List<string> names, string word)
    {
        if (names.Count == 1)
            parts.Add($"{names[0]} {word}");
        else if (names.Count > 1)
            parts.Add($"{names.Count.ToString(CultureInfo.InvariantCulture)} lights {word}: {string.Join(", ", names.Select(Short))}");
    }

    private static string Short(string name) => name.EndsWith(" light", StringComparison.Ordinal) ? name[..^6] : name;
}

/// <summary>
/// When the A350's light changes are spoken: as one sentence (<see cref="A350LampCallouts"/>) at the first batch end
/// once they are due, netted per light, so a light that went on and back off since the last sentence says nothing.
/// The timings are the iniBuilds A300's, measured on the same light power variables (<c>INI_AC_LIGHTS_FAILURE</c>,
/// <c>INI_DC_LIGHTS_FAILURE</c>) that reach MSFSBA on their own once-a-second subscriptions, out of the aircraft's order:
/// a light's first change since the last sentence waits <see cref="GatherMs"/> from its own arrival, a light power
/// change holds everything <see cref="SettleMs"/>, and a light going dark is spoken only once it has stayed dark
/// <see cref="OffHoldMs"/> (a flashing light reads as one "on", then "off" once it stops). A light added with a silent
/// off (a master light: its going out is the pilot's own press) has its off held the same way and then dropped, so a
/// flash neither cancels its pending "on" nor says it again. A change the definition does not keep tells this class to
/// <see cref="Forget"/> the light, so an earlier opposite change is not spoken stale. Pure.
/// </summary>
public sealed class A350LampSpeech
{
    public const int SettleMs = 2500;
    public const int GatherMs = 1500;
    public const int OffHoldMs = 3000;

    private readonly List<A350LampChange> _pending = new();
    private readonly Dictionary<string, long> _heldOff = new(StringComparer.Ordinal);
    private readonly HashSet<string> _silentOff = new(StringComparer.Ordinal);
    private long _holdUntil = long.MinValue;

    /// <summary>A power change's sentence is still to come: until it is spoken, an off nets in it unheld.</summary>
    private bool _powerSettling;

    public void Add(A350LampChange change, long now, bool silentOff = false)
    {
        if (change.On && _heldOff.Remove(change.Name))
        {
            _silentOff.Remove(change.Name);
            return;   // lit again while its off was held: a flash
        }
        if (!change.On && (silentOff || !_powerSettling))
        {
            _heldOff[change.Name] = now;
            if (silentOff)
                _silentOff.Add(change.Name);
            return;
        }
        Queue(change, now);
    }

    /// <summary>A bus's light power changed: hold the pending changes until it settles.</summary>
    public void NotePowerChange(long now)
    {
        _holdUntil = Math.Max(_holdUntil, now + SettleMs);
        _powerSettling = true;
    }

    /// <summary>The sentence to speak now, or null. Clears what it returns.</summary>
    public string? Flush(long now)
    {
        foreach (var (name, _) in _heldOff.Where(h => now - h.Value >= OffHoldMs).OrderBy(h => h.Value).ToList())
        {
            _heldOff.Remove(name);
            if (!_silentOff.Remove(name))
                _pending.Add(new A350LampChange(name, false));
        }
        if (now < _holdUntil)
            return null;
        _powerSettling = false;
        if (_pending.Count == 0)
            return null;
        var net = Net(_pending);
        _pending.Clear();
        return net.Count == 0 ? null : A350LampCallouts.Compose(net);
    }

    /// <summary>Drops the light's held off and every pending change of it (a change not kept: muted, a pressed button's
    /// own light), so a stale change is never spoken and a later one's parity is not flipped.</summary>
    public void Forget(string name)
    {
        _heldOff.Remove(name);
        _silentOff.Remove(name);
        _pending.RemoveAll(change => string.Equals(change.Name, name, StringComparison.Ordinal));
    }

    public void Clear()
    {
        _pending.Clear();
        _heldOff.Clear();
        _silentOff.Clear();
        _holdUntil = long.MinValue;
        _powerSettling = false;
    }

    private void Queue(A350LampChange change, long now)
    {
        // Only a light's first pending change starts a gather period: a light flashing through a power settle must not
        // keep moving the hold on.
        if (!_pending.Exists(p => string.Equals(p.Name, change.Name, StringComparison.Ordinal)))
            _holdUntil = Math.Max(_holdUntil, now + GatherMs);
        _pending.Add(change);
    }

    /// <summary>Each light's last change, kept only when it changed an odd number of times, in the order of those
    /// last changes.</summary>
    public static IReadOnlyList<A350LampChange> Net(IReadOnlyList<A350LampChange> changes)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < changes.Count; i++)
        {
            count[changes[i].Name] = count.GetValueOrDefault(changes[i].Name) + 1;
            last[changes[i].Name] = i;
        }
        return last.Where(l => count[l.Key] % 2 == 1).OrderBy(l => l.Value).Select(l => changes[l.Value]).ToList();
    }
}

/// <summary>A light that went on or off in the cockpit.</summary>
public readonly record struct A350LampTurn(A350Lamp Lamp, bool On);

/// <summary>
/// The A350's lights as the cockpit shows them (<see cref="A350Lamp.IsLit"/>: rule AND bus light power), decided from
/// the deliveries themselves in their arrival order, not the cache. A lamp's first value with every input read is a
/// silent baseline; an unread input leaves it undecided. Pure.
/// </summary>
public sealed class A350LampBoard
{
    private readonly ILookup<string, A350Lamp> _byInput;
    private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _lit = new(StringComparer.Ordinal);

    public A350LampBoard(IEnumerable<A350Lamp> lamps)
    {
        _byInput = lamps.SelectMany(l => l.InputKeys.Distinct().Select(k => (Key: k, Lamp: l))).ToLookup(p => p.Key, p => p.Lamp, StringComparer.Ordinal);
        SharedInputKeys = _byInput.Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The variables that change more than one light (the light power flags, a shared rule input): a delivery
    /// of one checks each light's own Ctrl+M row.</summary>
    public IReadOnlySet<string> SharedInputKeys { get; }

    public bool Handles(string key) => _byInput.Contains(key);

    /// <summary>Whether this delivery turns a bus's light power on or off (a first value is a baseline). Ask before
    /// <see cref="Update"/>.</summary>
    public bool PowerFlips(string key, double value) =>
        key is A350PanelLamps.AcPowerKey or A350PanelLamps.DcPowerKey
        && _values.TryGetValue(key, out var was) && was >= 0.5 != value >= 0.5;

    /// <summary>Takes one delivery and returns the lights it turned on or off.</summary>
    public IReadOnlyList<A350LampTurn> Update(string key, double value)
    {
        if (!_byInput.Contains(key))
            return Array.Empty<A350LampTurn>();
        _values[key] = value;
        List<A350LampTurn>? turns = null;
        foreach (var lamp in _byInput[key])
        {
            if (lamp.IsLit(Read) is not bool lit)
                continue;
            if (_lit.TryGetValue(lamp.Key, out var was) && was != lit)
                (turns ??= new List<A350LampTurn>()).Add(new A350LampTurn(lamp, lit));
            _lit[lamp.Key] = lit;
        }
        return turns ?? (IReadOnlyList<A350LampTurn>)Array.Empty<A350LampTurn>();
    }

    /// <summary>Records a value only where none has been read yet, and baselines the lights it completes, silently;
    /// true when the value was taken.</summary>
    public bool Seed(string key, double value)
    {
        if (!_byInput.Contains(key) || _values.ContainsKey(key))
            return false;
        _values[key] = value;
        foreach (var lamp in _byInput[key])
            if (!_lit.ContainsKey(lamp.Key) && lamp.IsLit(Read) is bool lit)
                _lit[lamp.Key] = lit;
        return true;
    }

    /// <summary>Every variable a light is made of.</summary>
    public IEnumerable<string> InputKeys => _byInput.Select(g => g.Key);

    public void Reset()
    {
        _values.Clear();
        _lit.Clear();
    }

    private double? Read(string key) => _values.TryGetValue(key, out var v) ? v : null;
}
