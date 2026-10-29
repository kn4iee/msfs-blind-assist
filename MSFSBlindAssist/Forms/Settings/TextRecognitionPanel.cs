using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Text recognition section of the unified Settings dialog: the download of the PaddleOCR models the
/// BlueBird 757 tablet reader runs on, and the checkbox that governs the offer to download them. The download
/// job belongs to MainForm, so it keeps running after Settings closes; this panel only shows its status (a
/// read-only TextBox, VAT-13) and starts or cancels it. Only the checkbox is saved.</summary>
public class TextRecognitionPanel : UserControl, ISettingsPanel
{
    private readonly TextRecognitionDownloader? _downloader;
    private TextBox _statusTextBox = null!;
    private Button _downloadButton = null!;
    private Button _cancelDownloadButton = null!;
    private CheckBox _offerCheckBox = null!;

    public string TabTitle => "Text recognition";

    internal TextRecognitionPanel(TextRecognitionDownloader? downloader = null)
    {
        _downloader = downloader;
        InitializeComponent();
        if (_downloader != null) _downloader.StatusChanged += OnDownloadStatusChanged;
    }

    private void InitializeComponent()
    {
        int yPos = 20;
        int labelHeight = 23;

        // Status first, so tabbing in reads what is installed or running.
        var introLabel = new Label
        {
            Text = "Text recognition for the BlueBird 757 tablet reader (downloaded once, then used offline):",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, labelHeight),
        };
        Controls.Add(introLabel);

        yPos += labelHeight + 5;
        _statusTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, 54),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Text recognition status",
            Text = "Text recognition status unavailable.",
        };
        Controls.Add(_statusTextBox);

        yPos += 54 + 8;
        _downloadButton = new Button
        {
            Text = "Download text recognition",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(200, 30),
            AccessibleName = "Download text recognition",
            Enabled = _downloader != null,
        };
        _downloadButton.Click += (_, _) => _downloader?.Start();
        Controls.Add(_downloadButton);

        _cancelDownloadButton = new Button
        {
            Text = "Cancel download",
            Location = new System.Drawing.Point(230, yPos),
            Size = new System.Drawing.Size(150, 30),
            AccessibleName = "Cancel download",
            Enabled = false,
        };
        _cancelDownloadButton.Click += (_, _) => _downloader?.Cancel();
        Controls.Add(_cancelDownloadButton);

        // The ONE thing that silences the offer (a dismissal is never remembered): no AccessibleName, so the
        // screen reader reads this text.
        yPos += 30 + 12;
        _offerCheckBox = new CheckBox
        {
            Text = "Offer this download when the BlueBird 757 is selected",
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, labelHeight),
        };
        Controls.Add(_offerCheckBox);

        yPos += labelHeight + 12;
        var creditTextBox = new TextBox
        {
            Location = new System.Drawing.Point(20, yPos),
            Size = new System.Drawing.Size(600, 38),
            Multiline = true,
            ReadOnly = true,
            AccessibleName = "Text recognition credits",
            Text = "Text recognition: PaddleOCR PP-OCRv6 small (PaddlePaddle, Apache 2.0), run with RapidOcrNet and ONNX Runtime.",
        };
        Controls.Add(creditTextBox);
    }

    public void LoadFrom(UserSettings settings)
    {
        ApplyDownloadStatus();
        _offerCheckBox.Checked = settings.OfferTextRecognitionFor757;
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.OfferTextRecognitionFor757 = _offerCheckBox.Checked;
    }

    public void OnLeaving()
    {
    }

    /// <summary>Status posts made before this tab was first shown were dropped: an unselected
    /// TabPage's controls have no handle yet. Show the latest whenever the tab appears, as
    /// GeoNamesPanel does; it only rewrites the box, never speaks.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) ApplyDownloadStatus();
    }

    /// <summary>Raised on the download worker's thread; never <c>Invoke</c> (it could deadlock
    /// against a UI thread waiting on the job), and a closing dialog may have no handle left
    /// (A380C-6).</summary>
    private void OnDownloadStatusChanged(object? sender, TextRecognitionStatus status)
    {
        try
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(ApplyDownloadStatus);
        }
        catch (InvalidOperationException)
        {
            // Handle destroyed between the check and the post: nothing to update.
        }
    }

    /// <summary>Shows the job's LATEST status (never the posted one, so a late progress post can
    /// never overwrite the final line), rewriting only what changed so the screen reader keeps
    /// its place in the box. The status comes from <see cref="TextRecognitionDownloader.DisplayStatus"/>,
    /// which reads the folder when no job ran, so a Ready that outlived its files is not shown.
    /// Download stays enabled when the models are Ready: a press checks the files again.</summary>
    private void ApplyDownloadStatus()
    {
        if (IsDisposed) return;
        if (_downloader == null)
        {
            DisplayText.SetPreserveCaret(_statusTextBox, "Text recognition status unavailable.");
            return;
        }
        var status = _downloader.DisplayStatus();
        DisplayText.SetPreserveCaret(_statusTextBox, status.Text);

        // WinForms moves focus off a control it disables, to the next tab stop: enable the button
        // that takes over first, so a Download press lands on Cancel, never on the checkbox
        // behind it, and hand focus back to Download before Cancel goes when the job ends.
        if (status.IsRunning)
        {
            _cancelDownloadButton.Enabled = true;
            _downloadButton.Enabled = false;
        }
        else
        {
            _downloadButton.Enabled = true;
            if (_cancelDownloadButton.Focused) _downloadButton.Focus();
            _cancelDownloadButton.Enabled = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Unsubscribe here, never in OnLeaving: that runs on every tab switch.
        if (disposing && _downloader != null) _downloader.StatusChanged -= OnDownloadStatusChanged;
        base.Dispose(disposing);
    }
}
