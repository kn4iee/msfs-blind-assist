using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Operating the MFD: typing through the KCCU's key L:vars (INI_MCDU1_* captain, INI_MCDU2_* first officer;
// live 2026-10-27: one calculator write of 1 pressed INIT), and clicking through the Coherent mouse route into
// the view whose wasm-instrument names the MFD gauge (the view number and guid change per session).
public class A350MfdInputTests
{
    // The point is DECIMAL: live 2026-10-28, in the MFD's ZFW field, INI_MCDU1_DECIMAL typed "167." and
    // INI_MCDU1_DOT (also a name in the module) typed nothing, so no weight or CG with a point could be entered.
    [Fact]
    public void Typed_text_maps_to_kccu_keys()
    {
        Assert.Equal(new[] { "V", "C", "B", "I", "SLASH", "1", "2", "DECIMAL", "5", "SPACE", "MINUS", "PLUS" },
            A350Kccu.KeysFor("vcbi/12.5 -+"));
        Assert.Null(A350Kccu.KeysFor("VC#BI"));   // no key for '#'
    }

    [Fact]
    public void A_key_is_its_sides_lvar()
    {
        Assert.Equal("INI_MCDU1_ENTER", A350Kccu.LVar(0, "ENTER"));
        Assert.Equal("INI_MCDU2_INIT", A350Kccu.LVar(1, "INIT"));
    }

    [Fact]
    public void The_mfd_view_is_found_by_its_gauge()
    {
        Assert.Equal(30, A350MfdInput.GuidIf("30|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=MFD"));
        Assert.Null(A350MfdInput.GuidIf("30|coui://html_ui/Pages/VCockpit/Instruments/WasmInstrument/WasmInstrument.html?wasm_module=inibuilds-A350.wasm&wasm_gauge=ISIS"));
        Assert.Null(A350MfdInput.GuidIf("31|coui://x?wasm_module=other.wasm&wasm_gauge=MFD"));
        Assert.Null(A350MfdInput.GuidIf(""));
    }

    [Fact]
    public void A_click_moves_presses_and_releases_at_the_sides_point()
    {
        var js = A350MfdInput.ClickJs(30, 1, 583.5f, 442f);
        Assert.Contains("Coherent.call('WASM_MOUSE_MOVE',30,1419.5,442,0)", js);
        Assert.Contains("'WASM_MOUSE_DOWN',30,1419.5,442,0", js);
        Assert.Contains("'WASM_MOUSE_UP',30,1419.5,442,0", js);
        Assert.Contains("'WASM_CLICK',30,1419.5,442,0", js);
    }

    [Fact]
    public void A_click_is_culture_safe()
    {
        var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains("30,216.5,247.25,0", A350MfdInput.ClickJs(30, 0, 216.5f, 247.25f));
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = saved; }
    }

    [Fact]
    public void Every_alt_letter_page_key_is_a_kccu_key()
    {
        Assert.All(MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys.Values, k => Assert.Contains(k, A350Kccu.FunctionKeys));
        Assert.Equal("INIT", MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys[System.Windows.Forms.Keys.I]);   // A380 letters
        Assert.Equal("FPLAN", MSFSBlindAssist.Forms.A350.A350MfdWindow.PageKeys[System.Windows.Forms.Keys.F]);
    }
}
