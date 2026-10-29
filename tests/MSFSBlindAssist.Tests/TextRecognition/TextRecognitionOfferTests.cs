using System.Reflection;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// When the app offers the text recognition download: on the BlueBird 757 profile, with the pilot's checkbox on,
/// while the models are not Ready and no download runs. The pilot's "Not now" is never remembered, so the answer
/// depends on these four inputs and nothing else.
/// </summary>
public sealed class TextRecognitionOfferTests
{
    /// <summary>The states in which the models cannot be used and the folder is worth a download.</summary>
    private static readonly TextRecognitionState[] StatesWithoutModels =
    {
        TextRecognitionState.Missing,
        TextRecognitionState.Incomplete,
        TextRecognitionState.Outdated,
    };

    private static bool Offer(bool is757, bool settingOn, TextRecognitionState state, bool running)
        => TextRecognitionOffer.ShouldOffer(is757, settingOn, state, running);

    /// <summary>Every combination of the four inputs: 2 x 2 x 4 x 2 = 32.</summary>
    private static List<(bool Is757, bool SettingOn, TextRecognitionState State, bool Running)> AllInputs()
    {
        var all = new List<(bool, bool, TextRecognitionState, bool)>();
        foreach (bool is757 in new[] { false, true })
        foreach (bool settingOn in new[] { false, true })
        foreach (TextRecognitionState state in Enum.GetValues<TextRecognitionState>())
        foreach (bool running in new[] { false, true })
            all.Add((is757, settingOn, state, running));
        return all;
    }

    [Fact]
    public void Models_that_are_missing_incomplete_or_outdated_are_offered_when_everything_else_allows()
    {
        foreach (TextRecognitionState state in StatesWithoutModels)
            Assert.True(Offer(is757: true, settingOn: true, state, running: false), state.ToString());
    }

    [Fact]
    public void Ready_models_are_never_offered()
    {
        foreach (var (is757, settingOn, state, running) in AllInputs().Where(input => input.State == TextRecognitionState.Ready))
            Assert.False(Offer(is757, settingOn, state, running), $"is757={is757} settingOn={settingOn} running={running}");
    }

    [Fact]
    public void Another_aircraft_is_not_offered_the_757_tablet_models()
    {
        foreach (TextRecognitionState state in StatesWithoutModels)
            Assert.False(Offer(is757: false, settingOn: true, state, running: false), state.ToString());
    }

    [Fact]
    public void The_pilots_checkbox_off_silences_the_offer()
    {
        foreach (TextRecognitionState state in StatesWithoutModels)
            Assert.False(Offer(is757: true, settingOn: false, state, running: false), state.ToString());
    }

    [Fact]
    public void A_running_download_is_never_offered_again()
    {
        foreach (TextRecognitionState state in StatesWithoutModels)
            Assert.False(Offer(is757: true, settingOn: true, state, running: true), state.ToString());
    }

    [Fact]
    public void Only_three_of_the_thirty_two_combinations_offer()
    {
        var offering = AllInputs().Where(input => Offer(input.Is757, input.SettingOn, input.State, input.Running)).ToList();

        Assert.Equal(
            new[]
            {
                (true, true, TextRecognitionState.Missing, false),
                (true, true, TextRecognitionState.Incomplete, false),
                (true, true, TextRecognitionState.Outdated, false),
            },
            offering);
    }

    [Fact]
    public void The_same_inputs_give_the_same_answer()
    {
        // "Not now" is never remembered: whatever was asked before, in whatever order and however often, the same four
        // inputs give the same answer. The first pass records it, then the list is asked again forward and in reverse.
        var inputs = AllInputs();
        var first = inputs.ToDictionary(input => input, input => Offer(input.Is757, input.SettingOn, input.State, input.Running));

        foreach (var input in inputs.Concat(Enumerable.Reverse(inputs)).Concat(inputs))
            Assert.Equal(first[input], Offer(input.Is757, input.SettingOn, input.State, input.Running));

        // Nothing for a dismissal to be written to: the class holds no field that can change after it is built.
        var mutable = typeof(TextRecognitionOffer)
            .GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Select(field => field.Name);
        Assert.Empty(mutable);
    }

    [Fact]
    public void Setting_defaults_to_on()
        => Assert.True(new UserSettings().OfferTextRecognitionFor757);
}
