using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Cleanup review 2026-10-30: numbers the displays speak were formatted in the PC's culture, where sv-SE writes a
/// minus as U+2212 and a decimal with a comma. Every capture reads the same whatever the culture.</summary>
public class A350CultureTests
{
    [Fact]
    public void Every_display_reads_the_same_in_a_comma_culture()
    {
        var captures = Directory.GetFiles(A350Fixtures.Dir, "*.json")
            .Select(f => A350Fixtures.Capture(Path.GetFileName(f)))
            .Where(c => c.Texts != null)
            .ToList();
        string ReadAll()
        {
            var o = new System.Text.StringBuilder();
            foreach (var cap in captures)
            {
                foreach (var d in IniBuildsA350Definition.DisplayPanels.Where(d => d.Gauge == cap.Gauge))
                    o.AppendLine(string.Join("|", d.Read(cap)));
                if (cap.Gauge == "CTR")
                {
                    o.AppendLine(A350EwdText.Build(cap));
                    for (int page = 1; page <= 16; page++) o.AppendLine(A350SdText.Build(cap, page));
                }
            }
            return o.ToString();
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = ReadAll();
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(invariant, ReadAll());
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
