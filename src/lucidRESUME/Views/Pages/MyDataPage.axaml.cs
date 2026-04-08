using Avalonia.Controls;
using Avalonia.Interactivity;
using lucidRESUME.Matching.Graph;
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
                if (args.PropertyName is nameof(vm.SkillPositions) or nameof(vm.Communities))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => BuildCommunityChart(vm));
            };
            if (vm.GanttBars.Count > 0)
                BuildGanttChart(vm);
            if (vm.HasCommunities)
                BuildCommunityChart(vm);
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

        // Only show entries that have dates AND both title and company
        var expList = vm.Experience
            .Where(e => e.StartDate.HasValue
                        && !string.IsNullOrWhiteSpace(e.Company)
                        && !string.IsNullOrWhiteSpace(e.Title))
            .OrderBy(e => e.StartDate)
            .ToList();

        for (var i = 0; i < expList.Count; i++)
        {
            var exp = expList[i];
            var start = exp.StartDate!.Value.ToDateTime(TimeOnly.MinValue);
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
            // Short label: just company name, truncated
            var company = exp.Company ?? "?";
            if (company.Length > 20) company = company[..20] + "…";
            tickLabels.Add(company);
        }

        if (bars.Count == 0) return;

        // Scale chart height to number of entries
        chart.Height = Math.Max(200, expList.Count * 28 + 60);

        var barPlot = plot.Add.Bars(bars.ToArray());
        barPlot.Horizontal = true;

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Left.SetTicks(tickPositions.ToArray(), tickLabels.ToArray());
        plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#CDD6F4");
        plot.Axes.Left.TickLabelStyle.FontSize = 10;
        plot.Axes.Bottom.TickLabelStyle.ForeColor = ScottPlot.Color.FromHex("#A6ADC8");
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        plot.Axes.Margins(left: 0);
        plot.HideGrid();

        chart.Refresh();
    }

    private void BuildCommunityChart(MyDataPageViewModel vm)
    {
        var chart = this.FindControl<ScottPlot.Avalonia.AvaPlot>("CommunityChart");
        if (chart is null || vm.SkillPositions.Count == 0) return;

        var plot = chart.Plot;
        plot.Clear();

        // Dark theme
        plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E1E2E");
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#181825");
        plot.Axes.Color(ScottPlot.Color.FromHex("#A6ADC8"));

        var communityColors = new[]
        {
            ScottPlot.Color.FromHex("#1E88E5"),
            ScottPlot.Color.FromHex("#E53935"),
            ScottPlot.Color.FromHex("#43A047"),
            ScottPlot.Color.FromHex("#FB8C00"),
            ScottPlot.Color.FromHex("#8E24AA"),
            ScottPlot.Color.FromHex("#00ACC1"),
            ScottPlot.Color.FromHex("#F4511E"),
            ScottPlot.Color.FromHex("#5E35B1"),
            ScottPlot.Color.FromHex("#FFB300"),
            ScottPlot.Color.FromHex("#00897B"),
        };

        // Group skills by community and plot each as a separate scatter series
        var allSkills = vm.AllSkills.ToList();
        var byCommunity = allSkills
            .Where(s => vm.SkillPositions.ContainsKey(s.SkillName))
            .GroupBy(s =>
            {
                var comm = vm.Communities.FirstOrDefault(c =>
                    c.DiscriminativeSkills.Any(d => d.SkillName.Equals(s.SkillName, StringComparison.OrdinalIgnoreCase)));
                return comm?.CommunityId ?? -1;
            });

        foreach (var group in byCommunity)
        {
            var xs = new List<double>();
            var ys = new List<double>();

            foreach (var skill in group)
            {
                if (vm.SkillPositions.TryGetValue(skill.SkillName, out var pos))
                {
                    xs.Add(pos.X);
                    ys.Add(pos.Y);
                }
            }

            if (xs.Count == 0) continue;

            var color = communityColors[Math.Abs(group.Key) % communityColors.Length];
            var label = vm.Communities.FirstOrDefault(c => c.CommunityId == group.Key)?.Label ?? "Other";

            var scatter = plot.Add.ScatterPoints(xs.ToArray(), ys.ToArray());
            scatter.Color = color;
            scatter.MarkerSize = 8;
            scatter.LegendText = label;
        }

        // Hide axes (UMAP dimensions are abstract)
        plot.Axes.Bottom.IsVisible = false;
        plot.Axes.Left.IsVisible = false;
        plot.HideGrid();
        plot.ShowLegend(ScottPlot.Alignment.LowerRight);
        plot.Legend.FontColor = ScottPlot.Color.FromHex("#A6ADC8");
        plot.Legend.FontSize = 9;
        plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#1E1E2E");

        chart.Refresh();
    }
}
