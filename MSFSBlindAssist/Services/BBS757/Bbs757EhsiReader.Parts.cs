namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Plan 28's parts: each read from its optional layout part, null with the layout's own reason when the part is
/// missing. A part's walk is all or nothing (B757-36): one torn read makes the whole reading corrupt.</summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>The reason a part whose layout part is present is not read: the parts this build does not read yet. The
    /// map extras' and map guides' tasks delete it as each reads its part.</summary>
    public const string NotReadYetInThisBuild = "not read yet in this build";

    /// <summary>The parts of one reading and their reasons.</summary>
    private sealed record PartsRead(
        Bbs757EhsiApproachReading? Approach,
        string? ApproachMissing,
        Bbs757EhsiTcasReading? Tcas,
        string? TcasMissing,
        Bbs757EhsiMapOptionsReading? MapOptions);

    /// <summary>Every part the layout holds; false when one is torn (the reading is then corrupt, B757-36).
    /// <paramref name="map"/> is what the map options' loop measures against, given in a full reading only; the map options
    /// are read only then, and only in MAP and PLAN.</summary>
    private bool TryParts(uint nd, uint source, int modeValue, bool full, Bbs757EhsiLoopScene? map, out PartsRead parts)
    {
        parts = null!;
        var l = Layout;
        Bbs757EhsiApproachReading? approach = null;
        Bbs757EhsiTcasReading? tcas = null;
        Bbs757EhsiMapOptionsReading? options = null;
        // ILS mode as the rows know it (Bbs757EhsiText's mode values): the VOR bearing pointers' station follows it.
        bool ilsMode = modeValue == (int)Bbs757EhsiText.EhsiMode.Ils;
        bool mapOrPlan = modeValue is (int)Bbs757EhsiText.EhsiMode.Map or (int)Bbs757EhsiText.EhsiMode.Plan;
        if (l.Approach is { } a && !TryApproach(nd, source, a, ilsMode, out approach)) return false;
        if (l.Tcas is { } t && !TryTcas(nd, t, full, out tcas)) return false;
        if (l.MapOptions is { } o && full && map is not null && mapOrPlan && !TryMapOptions(nd, o, map, out options)) return false;
        parts = new PartsRead(approach, l.Approach is null ? l.ApproachMissing : null, tcas, l.Tcas is null ? l.TcasMissing : null,
            options);
        return true;
    }

    /// <summary>The reading with its parts and their reasons; a part missing from the layout carries the layout's reason,
    /// and the parts not read yet carry <see cref="NotReadYetInThisBuild"/> while their layout part is present.</summary>
    private Bbs757EhsiReading WithParts(Bbs757EhsiReading reading, PartsRead parts)
    {
        var l = Layout;
        return reading with
        {
            Approach = parts.Approach,
            ApproachMissing = parts.ApproachMissing,
            Tcas = parts.Tcas,
            TcasMissing = parts.TcasMissing,
            MapOptions = parts.MapOptions,
            MapOptionsMissing = l.MapOptions is null ? l.MapOptionsMissing : null,
            MapExtrasMissing = l.MapExtras is null ? l.MapExtrasMissing : NotReadYetInThisBuild,
            MapGuidesMissing = l.MapGuides is null ? l.MapGuidesMissing : NotReadYetInThisBuild,
        };
    }

    /// <summary>A byte as stored (<see cref="Flag"/> reads one as set or clear).</summary>
    private bool U8(uint b, int offset, out byte v)
    {
        v = 0;
        return At(b, offset, out uint a) && _memory.TryReadByte(a, out v);
    }
}
