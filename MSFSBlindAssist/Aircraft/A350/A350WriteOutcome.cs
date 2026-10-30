namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>What a write of a panel row came to. To MainForm any row is claimed, its refusal spoken; to the First Officer only
/// <see cref="Sent"/> and <see cref="AlreadyThere"/> are a step done (FOA-15).</summary>
internal enum A350WriteOutcome
{
    NoSuchRow,
    Sent,
    AlreadyThere,
    Refused,
    Unavailable,
    NotFitted,
}
