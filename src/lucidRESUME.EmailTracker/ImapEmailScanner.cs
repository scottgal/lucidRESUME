using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace lucidRESUME.EmailTracker;

public sealed class ImapEmailScanner : IEmailScanner
{
    private readonly EmailScannerOptions _opts;
    private readonly ILogger<ImapEmailScanner> _logger;

    public ImapEmailScanner(IOptions<EmailScannerOptions> opts, ILogger<ImapEmailScanner> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public bool IsConfigured => _opts.IsConfigured;

    public async Task<IReadOnlyList<ScannedEmail>> ScanAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return [];

        var results = new List<ScannedEmail>();

        using var client = new ImapClient();
        try
        {
            await client.ConnectAsync(_opts.ImapHost!, _opts.ImapPort, _opts.UseSsl, ct);
            await client.AuthenticateAsync(_opts.Username!, _opts.Password!, ct);

            foreach (var folderName in _opts.FoldersToScan)
            {
                IMailFolder folder;
                try
                {
                    folder = folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
                        ? client.Inbox
                        : await client.GetFolderAsync(folderName, ct);
                }
                catch (FolderNotFoundException)
                {
                    _logger.LogWarning("Folder not found: {Folder}", folderName);
                    continue;
                }

                await folder.OpenAsync(FolderAccess.ReadOnly, ct);

                var query = SearchQuery.DeliveredAfter(since.DateTime);
                var uids = await folder.SearchAsync(query, ct);

                _logger.LogInformation("Found {Count} messages in {Folder} since {Since}",
                    uids.Count, folderName, since);

                foreach (var uid in uids)
                {
                    try
                    {
                        var message = await folder.GetMessageAsync(uid, ct);
                        results.Add(ToScannedEmail(message));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch message {Uid} from {Folder}", uid, folderName);
                    }
                }

                await folder.CloseAsync(false, ct);
            }

            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IMAP scan failed for {Host}", _opts.ImapHost);
        }

        return results;
    }

    private static ScannedEmail ToScannedEmail(MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault();
        var bodyText = message.TextBody ?? message.HtmlBody ?? "";
        if (bodyText.Length > 500)
            bodyText = bodyText[..500];

        return new ScannedEmail
        {
            MessageId = message.MessageId ?? Guid.NewGuid().ToString(),
            Subject = message.Subject ?? "",
            From = from?.Address ?? "",
            FromName = from?.Name,
            Date = message.Date,
            BodyPreview = bodyText
        };
    }
}
