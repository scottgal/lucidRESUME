namespace lucidRESUME.EmailTracker;

public sealed class EmailScannerOptions
{
    public bool Enabled { get; set; }
    public string? ImapHost { get; set; }
    public int ImapPort { get; set; } = 993;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; } // Use user-secrets or env var - never commit
    public int ScanDaysBack { get; set; } = 30;
    public List<string> FoldersToScan { get; set; } = ["INBOX"];

    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrEmpty(ImapHost) &&
        !string.IsNullOrEmpty(Username) &&
        !string.IsNullOrEmpty(Password);
}