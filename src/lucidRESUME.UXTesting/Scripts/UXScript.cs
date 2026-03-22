namespace lucidRESUME.UXTesting.Scripts;

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
    Svg
}

public class UXAction
{
    public ActionType Type { get; set; }
    public string? Target { get; set; }
    public string? Value { get; set; }
    public int DelayMs { get; set; } = 100;
    public string? Description { get; set; }
}

public class UXScript
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int DefaultDelay { get; set; } = 200;
    public List<UXAction> Actions { get; set; } = new();
    public Dictionary<string, string> Variables { get; set; } = new();
}

public class UXTestResult
{
    public string ScriptName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public bool Success { get; set; }
    public List<UXActionResult> ActionResults { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class UXActionResult
{
    public UXAction Action { get; set; } = null!;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
}
