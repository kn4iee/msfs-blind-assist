using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757EfbPageListingTests
{
    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-efb-memory");
    }

    /// <summary>Every committed tree, under en-US, de-DE and sv-SE (the final review's M6): one golden listing per page, which no
    /// Windows culture may change by a byte (CI runs under en-US only).</summary>
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(FixtureDir(), "*.tree.json").OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(p => new[] { "en-US", "de-DE", "sv-SE" }.Select(culture => new object[] { Path.GetFileName(p)[..^".tree.json".Length], culture }));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_page_reads_as_its_listing(string name, string culture)
    {
        string actual = TestCulture.Run(culture, () =>
        {
            var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(name));
            return Bbs757EfbListing.Of(Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>()));
        });
        string path = Path.Combine(FixtureDir(), name + ".listing.txt");
        if (Environment.GetEnvironmentVariable("BBS_EFB_UPDATE_LISTINGS") == "1")
        {
            // Written from en-US only, so a culture that read otherwise fails against the file the next run.
            if (culture == "en-US") File.WriteAllText(path, actual, new System.Text.UTF8Encoding(false));
            return;
        }
        Assert.True(File.Exists(path), $"{name}.listing.txt is missing: run the tests once with BBS_EFB_UPDATE_LISTINGS=1 to write it.");
        // A checkout with core.autocrlf turns the file's line ends into CRLF; the listing's own are LF.
        Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n"), actual);
    }

    public static IEnumerable<object[]> FixtureNames() =>
        Directory.GetFiles(FixtureDir(), "*.tree.json").OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p)[..^".tree.json".Length] });

    /// <summary>The model reads BlueBird's image class as it read "other" (plan 21, Task 5), and passes the top bar's and the dialog's
    /// apps by until Task 6 reads them: every committed tree, with each "other" control read as an image and those two apps added, reads
    /// as its listing.</summary>
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_page_reads_as_its_listing_with_its_others_as_images_and_the_top_bar_and_dialog_apps(string name)
    {
        string expected = File.ReadAllText(Path.Combine(FixtureDir(), name + ".listing.txt")).Replace("\r\n", "\n");
        var apps = Bbs757EfbTreeJson.Load(name);
        var retyped = new[] { TopBar, Dialog }.Concat(apps.Select(a => a with { Controls = a.Controls.Select(AsImage).ToList() })).ToList();

        var model = Bbs757EfbModelReader.Read(retyped);

        Assert.Equal(expected, Bbs757EfbListing.Of(Bbs757EfbElements.Build(model, null, Array.Empty<Bbs757EfbExtraElement>())));

        static Bbs757EfbMemory.Node AsImage(Bbs757EfbMemory.Node n) =>
            n with { Kind = n.Kind == "other" ? "image" : n.Kind, Children = n.Children.Select(AsImage).ToList() };
    }

    private static Bbs757EfbMemory.Node Leaf(string kind, string name, string text, float x, float y, float w, float h) =>
        new(kind, name, text, x, y, w, h, 0, Array.Empty<Bbs757EfbMemory.Node>());

    /// <summary>The top bar's app as measured on 2026-11-07: overlay_grid holding the clock line.</summary>
    private static readonly Bbs757EfbMemory.App TopBar = new(1, Bbs757EfbMemory.TopBarClass, new[]
    {
        new Bbs757EfbMemory.Node("grid", "overlay_grid", "", 0, 0, 1397, 32, 0, new[]
        {
            Leaf("label", "time_label", "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C", 15, 0, 1382, 32),
            Leaf("label", "keyboard_locked_label", "", 0, 0, 100, 100),
        }),
    });

    /// <summary>An open question as the defuel one was laid out: its words and YES and NO.</summary>
    private static readonly Bbs757EfbMemory.App Dialog = new(3, Bbs757EfbMemory.DialogClass, new[]
    {
        new Bbs757EfbMemory.Node("grid", "dialog_grid", "", 334.5f, 431, 728, 218, 0, new[]
        {
            Leaf("label", "text_label", "Current fuel exceeds the flight plan fuel.\nAre you sure you want to defuel?", 435.5f, 436, 622, 148),
            Leaf("button", "yes_btn", "YES", 334.5f, 589, 180, 60),
            Leaf("button", "no_btn", "NO", 882.5f, 589, 180, 60),
        }),
    });

    [Fact]
    public void The_listing_names_each_role()
    {
        var data = new Dictionary<string, string> { ["page"] = "Test page" };
        void Add(int i, string text, string type = "", string kind = "", int level = 0, bool clickable = false, string value = "", string? options = null)
        {
            data[$"items.{i}.text"] = text; data[$"items.{i}.type"] = type; data[$"items.{i}.kind"] = kind;
            data[$"items.{i}.level"] = level.ToString(); data[$"items.{i}.clickable"] = clickable ? "true" : "false";
            data[$"items.{i}.value"] = value;
            if (options != null) data[$"items.{i}.options"] = options;
        }
        Add(0, "Careful", kind: "alert");
        Add(1, "Section", level: 2);
        Add(2, "Sub", level: 3);
        Add(3, "Go", kind: "button", clickable: true);
        Add(4, "Lights", type: "checkbox", clickable: true, value: "true");
        Add(5, "Fans", type: "checkbox", clickable: true, value: "false");
        Add(6, "Mode", type: "select", clickable: true, value: "B", options: "A\u001fB\u001fC");
        Add(7, "Volume", type: "range", clickable: true, value: "40");
        Add(8, "Name", type: "text", value: "Bob");
        Add(9, "one\ntwo", type: "pre");
        Add(10, "Plain");
        var built = new Bbs757EfbElements.Built(data, new Dictionary<int, Bbs757EfbElements.Action>(), 11);
        Assert.Equal(
            "title: Test page\n" +
            "alert  Careful\nheading 2  Section\nheading 3  Sub\nbutton  Go\n" +
            "checkbox checked  Lights\ncheckbox not checked  Fans\n" +
            "combo [B] {A|B|C}  Mode\nslider 40  Volume\nedit [Bob]  Name\n" +
            "pre\n    one\n    two\ntext  Plain\n",
            Bbs757EfbListing.Of(built));
    }
}
