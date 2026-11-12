using System.Buffers.Binary;
using System.Text;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>The VOR and ILS picture (plan 28's <see cref="Bbs757EhsiApproach"/>), read in every reading: the nd's own
/// fields, the PFD's glideslope, the DME, the localizer the selected receiver has, and the two bearing pointers. A null
/// object pointer is "none" (no glideslope, no DME, no pointer); one outside the module, a station ident that is not text
/// or a position not finite and in range is torn.</summary>
public sealed partial class Bbs757EhsiReader
{
    /// <summary>An ADF bearing pointer's source, the app's own name for it: the pointer is a symbol, and the screen
    /// prints no word for it.</summary>
    private const string AdfLeftName = "ADF L", AdfRightName = "ADF R";

    /// <summary>A navaid record's ident: this many UTF-32 code points, the unused ones 0.</summary>
    private const int IdentLength = 4;

    private bool TryApproach(uint nd, uint source, Bbs757EhsiApproach a, out Bbs757EhsiApproachReading? reading)
    {
        reading = null;
        if (!F64(nd, a.VorCourse, out double vorCourse) || !F64(nd, a.VorDeviation, out double vorDeviation)
            || !Flag(nd, a.VorDeviationValid, out bool vorBar) || !Int(nd, a.ToFrom, out int toFrom)
            || !F64(nd, a.VorDme, out double vorDme) || !Flag(nd, a.VorDmeValid, out bool vorDmeValid)
            || !F64(nd, a.IlsCourse, out double ilsCourse) || !F64(nd, a.LocalizerDeviation, out double localizer)
            || !Flag(nd, a.LocalizerBar, out bool localizerBar) || !Flag(nd, a.IlsDots, out bool dots)
            || !Flag(nd, a.IlsTest, out bool ilsTest) || !F64(nd, a.IlsFrequency, out double frequency)
            || !Flag(nd, a.LocFlag, out bool locFlag) || !Flag(nd, a.GlideslopeFlag, out bool glideslopeFlag)
            || !Int(nd, a.DisplaySide, out int side))
            return false;

        // The glideslope: the PFD's ILS block, inside the PFD. No PFD: no scale, no pointer.
        bool scale = false, pointer = false, glideslopeTest = false;
        double glideslope = 0;
        if (!Link(nd, a.NdToPfd, out uint pfd)) return false;
        if (pfd != 0)
        {
            if (!Good(pfd) || !At(pfd, a.PfdToIls, out uint ils) || !F64(ils, a.GlideslopeDeviation, out glideslope)
                || !Flag(ils, a.GlideslopeScale, out scale) || !Flag(ils, a.GlideslopePointer, out pointer)
                || !Flag(ils, a.GlideslopeTest, out glideslopeTest))
                return false;
        }

        if (!TryIlsDme(nd, a, out bool ilsDmeValid, out double ilsDme)) return false;

        // The frequency is printed only while one is set and the receiver the instrument source selects has the
        // localizer; otherwise the screen prints the dashes.
        double? shownFrequency = null;
        if (double.IsFinite(frequency) && frequency != a.NoIlsFrequency)
        {
            if (!TryLocalizerReceived(source, a, out bool received)) return false;
            if (received) shownFrequency = frequency;
        }

        if (!TryBearingPointer(nd, a, a.RmiLeftHandle, a.NdToVorLeft, a.AdfLeft, a.VorLeftWord, AdfLeftName, out var pointer1)
            || !TryBearingPointer(nd, a, a.RmiRightHandle, a.NdToVorRight, a.AdfRight, a.VorRightWord, AdfRightName, out var pointer2))
            return false;

        var toFromValue = toFrom == a.ToValue ? Bbs757EhsiToFrom.To : toFrom == a.FromValue ? Bbs757EhsiToFrom.From : Bbs757EhsiToFrom.None;
        reading = new Bbs757EhsiApproachReading(
            VorCourse: vorCourse, VorBar: vorBar, VorDeviation: vorDeviation, ToFrom: toFromValue, VorDmeValid: vorDmeValid,
            VorDme: vorDme, IlsCourse: ilsCourse, LocalizerBar: localizerBar, LocalizerDeviation: localizer, IlsDots: dots,
            IlsTest: ilsTest, IlsFrequency: shownFrequency, LocFlag: locFlag, GlideslopeFlag: glideslopeFlag,
            CaptainSide: side == a.CaptainSide, GlideslopeScale: scale, GlideslopePointer: pointer,
            GlideslopeDeviation: glideslope, GlideslopeTest: glideslopeTest, IlsDmeValid: ilsDmeValid, IlsDme: ilsDme,
            Pointer1: pointer1, Pointer2: pointer2,
            Words: new Bbs757EhsiApproachWords(a.VorLeftWord, a.VorRightWord, a.IlsWord, a.ToWord, a.FromWord, a.DmeWord,
                a.NoFrequencyWord, a.NoDmeWord, a.LocWord, a.GlideslopeWord),
            DeviationFullScale: a.DeviationFullScale, DotsPerUnit: a.DotsPerUnit, GlideslopePin: a.GlideslopePin);
        return true;
    }

    /// <summary>ILS mode's DME, valid as BlueBird's draw tests it: the DME not failed and powered, its station an ILS's
    /// (the layout's kind), the distance finite and not negative. No DME or no station: not valid.</summary>
    private bool TryIlsDme(uint nd, Bbs757EhsiApproach a, out bool valid, out double distance)
    {
        valid = false;
        distance = 0;
        if (!Link(nd, a.NdToDme, out uint dme)) return false;
        if (dme == 0) return true;
        if (!Good(dme) || !Flag(dme, a.DmePowered, out bool powered) || !Flag(dme, a.DmeFailed, out bool failed)
            || !Link(dme, a.DmeStation, out uint station) || !F64(dme, a.DmeDistance, out distance))
            return false;
        if (station == 0) return true;
        if (!Good(station) || !U8(station, a.StationKind, out byte kind)) return false;
        valid = powered && !failed && kind == a.IlsStationKind && double.IsFinite(distance) && distance >= 0;
        return true;
    }

    /// <summary>Whether the receiver the instrument source selects has the localizer: <c>[receiver + ReceiverToSignal] +
    /// SignalBlock</c>, then the received byte. No receiver or no signal: not received.</summary>
    private bool TryLocalizerReceived(uint source, Bbs757EhsiApproach a, out bool received)
    {
        received = false;
        if (!Link(source, a.SourceToIlsReceiver, out uint receiver)) return false;
        if (receiver == 0) return true;
        if (!Good(receiver) || !Link(receiver, a.ReceiverToSignal, out uint signal)) return false;
        if (signal == 0) return true;
        return Good(signal) && At(signal, a.SignalBlock, out uint block) && Flag(block, a.LocalizerReceived, out received);
    }

    /// <summary>One bearing pointer, by its selector's L:var (the cached value in its handle): on VOR the VOR panel's
    /// station (none without one), on ADF the ADF, placed only while the layout gives its receivers' positions (none
    /// without a receiver then); any other value draws none.</summary>
    private bool TryBearingPointer(uint nd, Bbs757EhsiApproach a, uint handle, int ndToVor, int adfReceiver, string vorWord,
        string adfName, out Bbs757EhsiBearingPointer? pointer)
    {
        pointer = null;
        if (!F64(handle, a.HandleValue, out double selector)) return false;
        if (selector == a.RmiVor)
        {
            if (!Link(nd, ndToVor, out uint panel)) return false;
            if (panel == 0) return true;
            if (!Good(panel) || !Link(panel, a.PanelStation, out uint station)) return false;
            if (station == 0) return true;
            if (!Good(station) || !TryIdent(station, a.StationIdent, out string ident)
                || !Position(station, a.StationLatitude, a.StationLongitude, out double lat, out double lon))
                return false;
            pointer = new Bbs757EhsiBearingPointer(vorWord, ident, lat, lon);
            return true;
        }
        if (selector != a.RmiAdf) return true;
        if (!a.AdfPositions)
        {
            pointer = new Bbs757EhsiBearingPointer(adfName, null, null, null);
            return true;
        }
        // The ADF panel's receiver: its station's position, the longitude then the latitude, as every position the
        // layout names is stored.
        if (!Link(nd, a.NdToAdf, out uint adf)) return false;
        if (adf == 0) return true;
        if (!Good(adf) || !Link(adf, adfReceiver, out uint receiver)) return false;
        if (receiver == 0) return true;
        if (!Good(receiver) || !Position(receiver, a.AdfPosition + 2 * Word, a.AdfPosition, out double adfLat, out double adfLon))
            return false;
        pointer = new Bbs757EhsiBearingPointer(adfName, null, adfLat, adfLon);
        return true;
    }

    /// <summary>A navaid record's ident: up to <see cref="IdentLength"/> UTF-32 code points, ended by the first 0; each a
    /// Unicode scalar value. False when torn.</summary>
    private bool TryIdent(uint station, int offset, out string ident)
    {
        ident = "";
        Span<byte> bytes = stackalloc byte[IdentLength * Word];
        if (!At(station, offset, out uint at) || !_memory.TryRead(at, bytes)) return false;
        var sb = new StringBuilder(IdentLength);
        Span<char> utf16 = stackalloc char[2];
        for (int i = 0; i < IdentLength; i++)
        {
            uint codePoint = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(i * Word)..]);
            if (codePoint == 0) break;
            if (!Rune.TryCreate(codePoint, out Rune r)) return false;
            sb.Append(utf16[..r.EncodeToUtf16(utf16)]);
        }
        ident = sb.ToString();
        return true;
    }
}
