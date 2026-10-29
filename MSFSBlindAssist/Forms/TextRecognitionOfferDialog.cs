using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// "Download text recognition": what the BlueBird 757 tablet reader offers while its text recognition models are
/// not installed. MainForm shows it at startup on the 757 profile and when the 757 is picked in the Aircraft menu
/// (both governed by <see cref="TextRecognitionOffer"/>), and whenever the pilot asks the reader to read the tablet
/// without them. It opens on its read-only status box, which the screen reader reads, and once a download runs the
/// box follows the job's own status, as the Text recognition tab shows it. Download starts the job and the window
/// stays open with focus on that button, whose label follows the job
/// (<see cref="TextRecognitionText.OfferButton"/>). Escape is Not now, which reads Close once a download started;
/// closing never stops the download, which MainForm owns.
/// </summary>
internal sealed class TextRecognitionOfferDialog : Form
{
    private const string NotNowLabel = "&Not now";
    private const string CloseLabel = "&Close";

    private readonly TextRecognitionDownloader _downloader;
    private TextBox _statusTextBox = null!;
    private Button _downloadButton = null!;
    private Button _closeButton = null!;

    internal TextRecognitionOfferDialog(TextRecognitionDownloader downloader)
    {
        _downloader = downloader;
        InitializeComponent();
        _downloader.StatusChanged += OnDownloadStatusChanged;
        Shown += (_, _) => _statusTextBox.Focus();
    }

    private void InitializeComponent()
    {
        Text = "Download text recognition";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(560, 190);

        // One snapshot of the job feeds the box and the buttons, so they never disagree.
        TextRecognitionStatus status = _downloader.DisplayStatus();

        // A read-only TextBox, never a Label: focused first, it is what the screen reader reads.
        _statusTextBox = new TextBox
        {
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(536, 110),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Text recognition status",
            Text = StatusText(status),
            TabIndex = 0,
        };
        Controls.Add(_statusTextBox);

        var buttonRow = new FlowLayoutPanel
        {
            Location = new System.Drawing.Point(12, 134),
            Size = new System.Drawing.Size(536, 44),
            FlowDirection = FlowDirection.LeftToRight,
            TabIndex = 1,
        };
        // Buttons carry no AccessibleName: the screen reader reads their Text, so a label that follows the job
        // can never leave a stale name behind, and a mnemonic's ampersand is not read out.
        _downloadButton = new Button { AutoSize = true };
        _downloadButton.Click += OnDownloadClicked;
        buttonRow.Controls.Add(_downloadButton);
        _closeButton = new Button { Text = NotNowLabel, AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonRow.Controls.Add(_closeButton);
        Controls.Add(buttonRow);
        ApplyButtons(status);

        // Escape is Not now. No AcceptButton: Enter must never start a download (the update dialog's rule,
        // UpdateAvailableForm).
        CancelButton = _closeButton;
    }

    /// <summary>The box: why the reader asks while no job ran, otherwise the job's own line (running, failed,
    /// cancelled, ready). Always from <see cref="TextRecognitionDownloader.DisplayStatus"/>, never <c>Status</c>:
    /// that can still say Ready after the models were removed, which is the very case that opens this window.</summary>
    private static string StatusText(TextRecognitionStatus status)
        => status.Phase == TextRecognitionPhase.Idle ? TextRecognitionText.OfferStatus : status.Text;

    private (string Text, bool PressStarts) OfferButton(TextRecognitionStatus status)
        => TextRecognitionText.OfferButton(status.Phase, _downloader.Installed().State);

    /// <summary>Focus stays on the pressed button, never dimmed: no focus change is spoken over the job's
    /// confirmation. While the label says the job runs, or that the models are ready, a press starts nothing;
    /// after a failure or a cancel it retries.</summary>
    private void OnDownloadClicked(object? sender, EventArgs e)
    {
        if (!OfferButton(_downloader.DisplayStatus()).PressStarts) return;
        _downloader.Start();
        ApplyDownloadStatus();
    }

    /// <summary>The Download button's label for the job's phase, and Not now becoming Close once a download
    /// started (or was already under way or over when the window opened); each set only when it changes.</summary>
    private void ApplyButtons(TextRecognitionStatus status)
    {
        string text = OfferButton(status).Text;
        if (_downloadButton.Text != text) _downloadButton.Text = text;
        if (status.Phase != TextRecognitionPhase.Idle && _closeButton.Text != CloseLabel) _closeButton.Text = CloseLabel;
    }

    /// <summary>Raised on the download worker's thread; never <c>Invoke</c>, and a closing dialog
    /// may have no handle left (A380C-6).</summary>
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

    /// <summary>The job's LATEST status (never the posted one), rewritten in place so the screen
    /// reader keeps its line in the box, and the buttons to match.</summary>
    private void ApplyDownloadStatus()
    {
        if (IsDisposed) return;
        TextRecognitionStatus status = _downloader.DisplayStatus();
        DisplayText.SetPreserveCaret(_statusTextBox, StatusText(status));
        ApplyButtons(status);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _downloader.StatusChanged -= OnDownloadStatusChanged;
        base.Dispose(disposing);
    }
}
