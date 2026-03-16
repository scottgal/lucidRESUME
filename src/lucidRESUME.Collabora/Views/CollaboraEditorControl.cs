using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using lucidRESUME.Collabora.Services;

namespace lucidRESUME.Collabora.Views;

public sealed class CollaboraEditorControl : UserControl
{
    private readonly CollaboraService _collaboraService;
    private readonly TextBlock _statusText;
    private readonly Button _openButton;
    private string? _currentFileId;
    private string? _currentFilePath;

    public CollaboraEditorControl(CollaboraService collaboraService)
    {
        _collaboraService = collaboraService;
        
        _statusText = new TextBlock
        {
            Text = "No document loaded",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#6C7086")),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center
        };
        
        _openButton = new Button
        {
            Content = "Open in Editor",
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            Padding = new Avalonia.Thickness(16, 8)
        };
        
        _openButton.Click += async (_, _) => await OpenInEditorAsync();
        
        Content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Children = { _statusText, _openButton }
        };
    }

    public async Task<bool> LoadDocumentAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _statusText.Text = "File not found";
            _openButton.IsVisible = false;
            return false;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var supportedExtensions = new[] { ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pdf", ".odt", ".ods", ".odp" };
        
        if (!supportedExtensions.Contains(extension))
        {
            _statusText.Text = $"Unsupported format: {extension}\nSupported: .docx, .xlsx, .pptx, .pdf";
            _openButton.IsVisible = false;
            return false;
        }

        try
        {
            _statusText.Text = "Starting Collabora...";
            
            var started = await _collaboraService.EnsureStartedAsync();
            if (!started)
            {
                _statusText.Text = "Failed to start Collabora.\nMake sure Docker is running.";
                _openButton.IsVisible = false;
                return false;
            }
            
            _currentFilePath = filePath;
            _currentFileId = _collaboraService.RegisterFile(filePath);
            
            _statusText.Text = $"Ready: {Path.GetFileName(filePath)}\n\nClick below to open in Collabora editor";
            _openButton.IsVisible = true;
            
            _ = OpenInEditorAsync();
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Error: {ex.Message}";
            _openButton.IsVisible = false;
            return false;
        }
    }

    private async Task OpenInEditorAsync()
    {
        if (_currentFileId == null) return;
        
        var editorUrl = _collaboraService.GetEditorUrl(_currentFileId);
        
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = editorUrl,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", editorUrl);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", editorUrl);
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Failed to open browser: {ex.Message}";
        }
        
        await Task.CompletedTask;
    }

    public void CloseDocument()
    {
        if (_currentFileId != null)
        {
            _collaboraService.UnregisterFile(_currentFileId);
            _currentFileId = null;
        }
        
        _currentFilePath = null;
        _statusText.Text = "No document loaded";
        _openButton.IsVisible = false;
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        CloseDocument();
        base.OnDetachedFromLogicalTree(e);
    }
}
