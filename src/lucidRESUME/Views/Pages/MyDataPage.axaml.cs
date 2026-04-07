using Avalonia.Controls;
using Avalonia.Interactivity;
using lucidRESUME.ViewModels.Pages;

namespace lucidRESUME.Views.Pages;

public partial class MyDataPage : UserControl
{
    public MyDataPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MyDataPageViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(vm.GanttBars) or nameof(vm.Experience))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => BuildGanttChart(vm));
            };
            // Build immediately if data already loaded
            if (vm.GanttBars.Count > 0)
                BuildGanttChart(vm);
        }
    }

    private void BuildGanttChart(MyDataPageViewModel vm)
    {
        var chart = this.FindControl<ScottPlot.Avalonia.AvaPlot>("GanttChart");
        if (chart is null || vm.Experience.Count == 0) return;

        var plot = chart.Plot;
        plot.Clear();

        // Dark theme
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E2E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        plot.Axes.Color(ScottPlot.Color.FromHex("#A6ADC8"));

        var colors = new[]
        {
            ScottPlot.Color.FromHex("#1E88E5"),
            ScottPlot.Color.FromHex("#8E24AA"),
            ScottPlot.Color.FromHex("#43A047"),
            ScottPlot.Color.FromHex("#FB8C00"),
            ScottPlot.Color.FromHex("#E53935"),
            ScottPlot.Color.FromHex("#00ACC1"),
            ScottPlot.Color.FromHex("#5E35B1"),
            ScottPlot.Color.FromHex("#F4511E"),
        };

        var bars = new List<ScottPlot.Bar>();
        var tickPositions = new List<double>();
        var tickLabels = new List<string>();

        var expList = vm.Experience.OrderBy(e => e.StartDate).ToList();

        for (var i = 0; i < expList.Count; i++)
        {
            var exp = expList[i];
            var start = exp.StartDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
            var end = exp.IsCurrent
                ? DateTime.Today
                : exp.EndDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;

            var pos = expList.Count - i;
            bars.Add(new ScottPlot.Bar
            {
                Position = pos,
                ValueBase = start.ToOADate(),
                Value = end.ToOADate(),
                FillColor = colors[i % colors.Length],
            });

            tickPositions.Add(pos);
            tickLabels.Add($"{exp.Title ?? ""} — {exp.Company ?? ""}");
        }

        if (bars.Count == 0) return;

        var barPlot = plot.Add.Bars(bars.ToArray());
        barPlot.Horizontal = true;

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Left.SetTicks(tickPositions.ToArray(), tickLabels.ToArray());
        plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#CDD6F4");
        plot.Axes.Left.TickLabelStyle.FontSize = 9;
        plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#A6ADC8");
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        plot.Axes.Margins(left: 0);
        plot.HideGrid();

        chart.Refresh();
    }
}
