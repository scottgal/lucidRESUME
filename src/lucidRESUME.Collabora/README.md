# lucidRESUME.Collabora

WOPI-based document viewer plugin using Collabora Online (CODE).

## Features

- Replaces image preview with full document editing
- Supports .docx, .xlsx, .pptx, .pdf, .odt, .ods, .odp
- Auto-starts CODE Docker container
- WOPI host for secure file access

## Setup

### 1. Add to appsettings.json

```json
{
  "Collabora": {
    "Enabled": true,
    "CodeUrl": "http://localhost:9980",
    "WopiPort": 9981,
    "HostUrl": "http://localhost:9981"
  }
}
```

### 2. Register in App.axaml.cs

```csharp
using lucidRESUME.Collabora;

private static void ConfigureServices(IServiceCollection services)
{
    // ... existing services ...
    services.AddCollabora(config);
}
```

### 3. Add project reference to lucidRESUME.csproj

```xml
<ProjectReference Include="..\lucidRESUME.Collabora\lucidRESUME.Collabora.csproj" />
```

## Docker Setup

The plugin auto-starts the CODE container. Ensure Docker is running.

Manual start:
```bash
docker run -t -p 9980:9980 -e "extra_params=--o:ssl.enable=false" collabora/code
```

## Usage in ResumePage

Replace the image viewer with CollaboraEditorControl when the plugin is enabled:

```csharp
public partial class ResumePageViewModel : ViewModelBase
{
    private readonly CollaboraService? _collaboraService;
    
    [ObservableProperty] private bool _useCollaboraEditor;
    
    public ResumePageViewModel(
        IResumeParser parser, 
        IDocumentImageCache imageCache, 
        IAppStore store,
        CollaboraService? collaboraService = null)
    {
        _parser = parser;
        _imageCache = imageCache;
        _store = store;
        _collaboraService = collaboraService;
        UseCollaboraEditor = collaboraService != null;
    }
}
```

## Requirements

- Docker Desktop (Windows/macOS) or Docker Engine (Linux)
- .NET 10.0
- Avalonia 11.3+
