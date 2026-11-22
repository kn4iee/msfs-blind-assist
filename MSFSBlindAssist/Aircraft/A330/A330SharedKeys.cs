namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>Variable keys more than one plan reads; a key lives here only when two plans need it.
/// A key literal that two of the definition's area files both spell belongs here (a test guards it).</summary>
internal static class A330SharedKeys
{
    internal const string Altitude = "A330_ALTITUDE";
    internal const string Autobrake = "A330_AUTOBRAKE";
    internal const string FcuAlt = "A330_FCU_ALT";
    internal const string FcuFpa = "A330_FCU_FPA";
    internal const string FcuHdg = "A330_FCU_HDG";
    internal const string FcuSpd = "A330_FCU_SPD";
    internal const string FcuTrk = "A330_FCU_TRK";
    internal const string FcuVs = "A330_FCU_VS";
    internal const string FlapAngle = "A330_FLAP_ANGLE";
    internal const string Flaps = "A330_FLAPS";
    internal const string FmaAp = "A330_FMA_AP";
    internal const string Gear = "A330_GEAR";
    internal const string MMach = "A330_M_MACH";
    internal const string MSpdManaged = "A330_M_SPD_MANAGED";
    internal const string MTrkfpa = "A330_M_TRKFPA";
    internal const string SlatAngle = "A330_SLAT_ANGLE";
    internal const string Trim = "A330_TRIM";
}
