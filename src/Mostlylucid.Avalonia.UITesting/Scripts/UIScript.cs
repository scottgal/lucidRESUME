namespace Mostlylucid.Avalonia.UITesting.Scripts;

public enum ActionType
{
    Click,
    DoubleClick,
    RightClick,
    TypeText,
    PressKey,
    Hover,
    Scroll,
    Wait,
    Navigate,
    Screenshot,
    Assert,
    MouseMove,
    MouseDown,
    MouseUp,
    StartVideo,
    StopVideo,
    Svg
}

public class UIAction
{
    public ActionType Type { get; set; }
    public string? Target { get; set; }
    public string? Value { get; set; }
    public int DelayMs { get; set; } = 100;
    public string? Description { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public string? WindowId { get; set; }
}

public class UIScript
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int DefaultDelay { get; set; } = 200;
    public List<UIAction> Actions { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
}

public class UITestResult
{
    public string ScriptName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public bool Success { get; set; }
    public List<UIActionResult> ActionResults { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? VideoPath { get; set; }
}

public class UIActionResult
{
    public UIAction Action { get; set; } = null!;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}
