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
/// scroll, its content offset in tablet pixels, as a float at <see cref="ScrollboxOffset"/> and draws its controls that much
/// higher (never the scrollbar thumb, which stops short of the content's end: B757-26). A <c>slider</c> keeps its knob's
/// rectangle at <see cref="SliderKnob"/> (x, y, width, height: four floats, in the slider rectangle's own coordinates, which
/// a scroll does not move). The reader tells a control's kind from its vtable (<see cref="ClassVptrs"/>). Equality compares
/// the byte and class lists by content, ignoring <see cref="SourceFile"/>.
/// <para>The phase 2 fields (measured live 2026-11-07, docs/b757/evidence/efb-memory-classes.md) follow <see cref="SliderKnob"/>:
/// a <c>textbox</c>'s text label and a <c>multiline</c> box's text; a <c>pulldown</c>'s face, list, entries and open byte (it has
/// no index: its current entry is the one whose text is its face's); a button's two bytes that mark the current side-menu entry
/// or tab; BBOS's dialog byte; and BBOS's vector of notification records with each record's fields. Every std::string is
/// libc++'s, every vector (begin, end, capacity).</para>
/// <para>Then the top bar app's notification panel (measured live 2026-11-07, the evidence's "The top bar"): BlueBird draws its
/// notification pop-up and its notification list from the overlay app's own fields, not as controls, over the top right of the page:
/// the byte that says one shows, the byte that says the full list is open, and the panel's rectangle.</para>
/// </summary>
/// <param name="TextboxLabel">A text box's pointer to its <c>text_label</c>, a label outside every children vector whose
/// <see cref="CtrlLabelText"/> is the box's text (0x128 in this build).</param>
/// <param name="MultilineText">A multi-line text box's whole text, a std::string with '\n' between lines (0x128); the Operating
/// FP's runs to about 92,000 characters.</param>
/// <param name="PulldownFace">A drop-down's pointer to its face, a button whose <see cref="CtrlButtonText"/> is the current
/// choice and whose rectangle is the drop-down's (0x128).</param>
/// <param name="PulldownList">A drop-down's pointer to its list, a scrollbox holding a stack of one button per entry (0x130);
/// open, those buttons are laid out 38 px apart from the scrollbox's top.</param>
/// <param name="PulldownEntries">A drop-down's std::vector of entry-record pointers in 8-byte slots, as
/// <see cref="CtrlChildren"/> (0x138), in the list's drawn order.</param>
/// <param name="PulldownEntryText">An entry record's text, a std::string (0x0c).</param>
/// <param name="PulldownOpen">A drop-down's byte, 1 while its list is open (0x154).</param>
/// <param name="ButtonSelectable">A button's byte, 1 on the entries of a side menu or tab row (0x140); 0 on a tile that takes
/// no tap and on a hidden button.</param>
/// <param name="ButtonUnselected">A button's byte, 0 on the current side-menu entry or tab, 1 on the others (0x143); the
/// current one is the button with <see cref="ButtonSelectable"/> 1 and this 0.</param>
/// <param name="BbosModalOpen">BBOS's byte, 1 while a modal dialog (a question) is up (0x1d8); the dialog app's own front flag
/// and page stay set after the dialog is closed.</param>
/// <param name="BbosNotices">BBOS's std::vector of notification records (begin, end, capacity), an offset into BBOS like
/// <see cref="BbosToAppMap"/> (0x9334): a member of the BBOS heap object, not a static address.</param>
/// <param name="NoticeStride">A notification record's size (0x3c).</param>
/// <param name="NoticeId">A record's id, an int rising with each new notification (0x00).</param>
/// <param name="NoticeTitle">A record's title, a std::string, empty but for a few (0x04).</param>
/// <param name="NoticeText">A record's text, a std::string (0x10).</param>
/// <param name="NoticeTime">A record's time, a std::string such as "21:37Z" (0x1c).</param>
/// <param name="NoticeUnread">A record's byte, 1 until the pilot has opened the notification list (0x34).</param>
/// <param name="OverlayPanelShown">The top bar app's byte, 1 while BlueBird's notification pop-up or its notification list shows
/// (0x1fc): a new notification's pop-up sets it for about 10 s, and opening the list sets it too.</param>
/// <param name="OverlayListOpen">The top bar app's byte, 1 while the full notification list is open (0x1fd); 0 under a pop-up.</param>
/// <param name="OverlayPanelRect">The top bar app's panel rectangle, the x, y and width floats (0x234), in tablet pixels: [1039.75,38
/// 349.25 wide] for both the pop-up and the list.</param>
/// <param name="OverlayPanelHeight">The panel's height, a float (0x240, the rectangle's fourth float in this build): 118 under the
/// pop-up, 480 with the list open, 0 closed.</param>
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
    int SliderKnob,
    int TextboxLabel,
    int MultilineText,
    int PulldownFace,
    int PulldownList,
    int PulldownEntries,
    int PulldownEntryText,
    int PulldownOpen,
    int ButtonSelectable,
    int ButtonUnselected,
    int BbosModalOpen,
    int BbosNotices,
    int NoticeStride,
    int NoticeId,
    int NoticeTitle,
    int NoticeText,
    int NoticeTime,
    int NoticeUnread,
    int OverlayPanelShown,
    int OverlayListOpen,
    int OverlayPanelRect,
    int OverlayPanelHeight,
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
        && ScrollboxOffset == other.ScrollboxOffset && SliderKnob == other.SliderKnob
        && TextboxLabel == other.TextboxLabel && MultilineText == other.MultilineText
        && PulldownFace == other.PulldownFace && PulldownList == other.PulldownList
        && PulldownEntries == other.PulldownEntries && PulldownEntryText == other.PulldownEntryText
        && PulldownOpen == other.PulldownOpen
        && ButtonSelectable == other.ButtonSelectable && ButtonUnselected == other.ButtonUnselected
        && BbosModalOpen == other.BbosModalOpen && BbosNotices == other.BbosNotices
        && NoticeStride == other.NoticeStride && NoticeId == other.NoticeId && NoticeTitle == other.NoticeTitle
        && NoticeText == other.NoticeText && NoticeTime == other.NoticeTime && NoticeUnread == other.NoticeUnread
        && OverlayPanelShown == other.OverlayPanelShown && OverlayListOpen == other.OverlayListOpen
        && OverlayPanelRect == other.OverlayPanelRect && OverlayPanelHeight == other.OverlayPanelHeight
        && ClassVptrs.SequenceEqual(other.ClassVptrs);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(ManagerBase); h.Add(ManagerSpan); h.Add(EfbVptr); h.Add(EfbToBbos); h.Add(BbosToAppMap);
        h.Add(AppFront); h.Add(AppPage); h.Add(AppRoots); h.Add(PageChildren);
        h.Add(CtrlName); h.Add(CtrlRect); h.Add(CtrlVisible); h.Add(CtrlChildren); h.Add(CtrlParent);
        h.Add(CtrlLabelText); h.Add(CtrlButtonText); h.Add(ScrollboxOffset); h.Add(SliderKnob);
        h.Add(TextboxLabel); h.Add(MultilineText);
        h.Add(PulldownFace); h.Add(PulldownList); h.Add(PulldownEntries); h.Add(PulldownEntryText); h.Add(PulldownOpen);
        h.Add(ButtonSelectable); h.Add(ButtonUnselected); h.Add(BbosModalOpen); h.Add(BbosNotices);
        h.Add(NoticeStride); h.Add(NoticeId); h.Add(NoticeTitle); h.Add(NoticeText); h.Add(NoticeTime); h.Add(NoticeUnread);
        h.Add(OverlayPanelShown); h.Add(OverlayListOpen); h.Add(OverlayPanelRect); h.Add(OverlayPanelHeight);
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
