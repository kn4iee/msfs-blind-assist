namespace MSFSBlindAssist.Services.BBS757;

/// <summary>A control class the EFB reader recognises, with the runtime vtable pointer its objects carry.</summary>
public sealed record Bbs757EfbClassVptr(string Kind, uint Vptr);

/// <summary>
/// Where one installed BBS752.wasm keeps the EFB tablet's control tree, all from the layout file (BBS757-D2).
/// The EFB object is the gauge-manager pointer carrying <see cref="EfbVptr"/>; it holds the BBOS root at
/// <see cref="EfbToBbos"/>; BBOS holds the app map at <see cref="BbosToAppMap"/> (a <c>std::map&lt;int, vector&gt;</c>
/// whose node and vector layout are libc++ ABI, known to the reader). Each app has a front flag, a current page
/// and a roots vector; each control a name, rectangle, visible byte, children vector, parent, and the label or
/// button text. A control's rectangle never moves when its page scrolls: a <c>scrollbox</c> control keeps the
/// scroll as a float at <see cref="ScrollboxOffset"/> and draws its controls that much higher. The reader tells a control's kind from its vtable (<see cref="ClassVptrs"/>). Equality compares
/// the byte and class lists by content, ignoring <see cref="SourceFile"/>.
/// </summary>
public sealed record Bbs757EfbMemLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint ManagerBase,
    uint ManagerSpan,
    uint EfbVptr,
    int EfbToBbos,
    int BbosToAppMap,
    int AppFront,
    int AppPage,
    int AppRoots,
    int PageChildren,
    int CtrlName,
    int CtrlRect,
    int CtrlVisible,
    int CtrlChildren,
    int CtrlParent,
    int CtrlLabelText,
    int CtrlButtonText,
    int ScrollboxOffset,
    IReadOnlyList<Bbs757EfbClassVptr> ClassVptrs)
{
    public bool Equals(Bbs757EfbMemLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && ManagerBase == other.ManagerBase && ManagerSpan == other.ManagerSpan
        && EfbVptr == other.EfbVptr && EfbToBbos == other.EfbToBbos && BbosToAppMap == other.BbosToAppMap
        && AppFront == other.AppFront && AppPage == other.AppPage && AppRoots == other.AppRoots
        && PageChildren == other.PageChildren
        && CtrlName == other.CtrlName && CtrlRect == other.CtrlRect && CtrlVisible == other.CtrlVisible
        && CtrlChildren == other.CtrlChildren && CtrlParent == other.CtrlParent
        && CtrlLabelText == other.CtrlLabelText && CtrlButtonText == other.CtrlButtonText
        && ScrollboxOffset == other.ScrollboxOffset
        && ClassVptrs.SequenceEqual(other.ClassVptrs);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase); h.Add(ManagerSpan); h.Add(EfbVptr); h.Add(EfbToBbos); h.Add(BbosToAppMap);
        h.Add(AppFront); h.Add(AppPage); h.Add(AppRoots); h.Add(PageChildren);
        h.Add(CtrlName); h.Add(CtrlRect); h.Add(CtrlVisible); h.Add(CtrlChildren); h.Add(CtrlParent);
        h.Add(CtrlLabelText); h.Add(CtrlButtonText); h.Add(ScrollboxOffset);
        foreach (var c in ClassVptrs) h.Add(c);
        return h.ToHashCode();
    }

    /// <summary>The kind for a control whose first word is <paramref name="vptr"/>; null when unknown.</summary>
    public string? KindFor(uint vptr)
    {
        foreach (var c in ClassVptrs) if (c.Vptr == vptr) return c.Kind;
        return null;
    }
}

/// <summary>The EFB reader's result in a layout file: a layout, or the detailed reason there is none.</summary>
public sealed record Bbs757EfbMemDerivation(Bbs757EfbMemLayout? Layout, string? Failure);
