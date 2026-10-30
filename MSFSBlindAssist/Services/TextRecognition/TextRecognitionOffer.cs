namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>
/// When the app offers the text recognition download to a pilot who did not ask for it: when the BlueBird 757 is
/// the loaded aircraft (at startup on that profile, and when it is picked in the Aircraft menu), the pilot's
/// checkbox on the Text recognition tab is on, the models cannot be used, and no download is already running.
///
/// <para>Pure and stateless on purpose. A dismissal is never remembered: "Not now" asks again at the next startup
/// on the 757 and at the next pick of it, and the one thing that silences the offer is the pilot's own checkbox
/// (<c>UserSettings.OfferTextRecognitionFor757</c>). Shift+T on the 757 while a simulator runs is not an offer: while
/// the models are not Ready it shows the offer window in place of the tablet window, whatever the checkbox says.</para>
/// </summary>
internal static class TextRecognitionOffer
{
    /// <param name="is757">The loaded aircraft is the BlueBird 757 profile.</param>
    /// <param name="settingOn">The pilot's checkbox, <c>UserSettings.OfferTextRecognitionFor757</c>.</param>
    /// <param name="state">What the models folder holds now; only Ready needs no download.</param>
    /// <param name="running">A download is running (started from the tab or from the offer window): a second offer
    /// would only repeat it.</param>
    internal static bool ShouldOffer(bool is757, bool settingOn, TextRecognitionState state, bool running)
        => is757 && settingOn && state != TextRecognitionState.Ready && !running;
}
