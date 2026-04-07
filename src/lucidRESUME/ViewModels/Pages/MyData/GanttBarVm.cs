namespace lucidRESUME.ViewModels.Pages.MyData;

public sealed class GanttBarVm
{
    public string Title { get; init; } = "";
    public string Company { get; init; } = "";
    public string DateRange { get; init; } = "";
    public double LeftPercent { get; init; }
    public double WidthPercent { get; init; }
    public string Color { get; init; } = "#1E88E5";
    public bool IsCurrent { get; init; }
}
