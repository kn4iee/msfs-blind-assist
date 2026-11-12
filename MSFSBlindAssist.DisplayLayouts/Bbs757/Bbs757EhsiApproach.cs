namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI's VOR and ILS picture, an optional part of <see cref="Bbs757EhsiLayout"/> (plan 28), all from the layout
/// file. The VOR and ILS fields (<see cref="VorCourse"/> to <see cref="GlideslopeFlag"/>) and <see cref="DisplaySide"/>
/// are offsets in the nd; the side is the Captain's while it holds <see cref="CaptainSide"/>. The glideslope is in the
/// PFD's ILS block (nd at <see cref="NdToPfd"/>, block at <see cref="PfdToIls"/>); the DME object is at
/// <see cref="NdToDme"/>, its station record's kind at <see cref="StationKind"/> (an ILS DME while
/// <see cref="IlsStationKind"/>). The ILS frequency is shown while the receiver the instrument source selects
/// (<see cref="SourceToIlsReceiver"/>) has the localizer: <c>[receiver + ReceiverToSignal] + SignalBlock</c>, then
/// <see cref="LocalizerReceived"/>. The bearing pointers' selectors are the L:var handles at the static
/// <see cref="RmiLeftHandle"/> and <see cref="RmiRightHandle"/>, the value an f64 at <see cref="HandleValue"/>; the VOR
/// panels' station at <see cref="PanelStation"/> is a record with its ident (four UTF-32 characters), longitude and
/// latitude; the ADF stations' positions are read only while <see cref="AdfPositions"/>. The words are the screen's own
/// and the last three values the draw's constants. Records compare by content.
/// </summary>
public sealed record Bbs757EhsiApproach(
    int VorCourse,
    int VorDeviation,
    int VorDeviationValid,
    int ToFrom,
    int ToValue,
    int FromValue,
    int VorDme,
    int VorDmeValid,
    int IlsCourse,
    int LocalizerDeviation,
    int LocalizerBar,
    int IlsDots,
    int IlsTest,
    int IlsFrequency,
    double NoIlsFrequency,
    int LocFlag,
    int GlideslopeFlag,
    int DisplaySide,
    int CaptainSide,
    int NdToPfd,
    int PfdToIls,
    int GlideslopeDeviation,
    int GlideslopeScale,
    int GlideslopePointer,
    int GlideslopeTest,
    int NdToDme,
    int DmePowered,
    int DmeFailed,
    int DmeStation,
    int DmeDistance,
    int StationKind,
    int IlsStationKind,
    int SourceToIlsReceiver,
    int ReceiverToSignal,
    int SignalBlock,
    int LocalizerReceived,
    uint RmiLeftHandle,
    uint RmiRightHandle,
    int HandleValue,
    int RmiVor,
    int RmiAdf,
    int NdToVorLeft,
    int NdToVorRight,
    int PanelStation,
    int StationIdent,
    int StationLongitude,
    int StationLatitude,
    int NdToAdf,
    bool AdfPositions,
    int AdfLeft,
    int AdfRight,
    int AdfPosition,
    string VorLeftWord,
    string VorRightWord,
    string IlsWord,
    string ToWord,
    string FromWord,
    string DmeWord,
    string NoFrequencyWord,
    string NoDmeWord,
    string LocWord,
    string GlideslopeWord,
    double DeviationFullScale,
    double DotsPerUnit,
    double GlideslopePin);
