namespace lucidRESUME.Core.Models.Profile;

public sealed class WorkPreferences
{
    public bool OpenToRemote { get; set; } = true;
    public bool OpenToHybrid { get; set; } = true;
    public bool OpenToOnsite { get; set; } = true;
    public List<string> PreferredLocations { get; set; } = [];
    public decimal? MinSalary { get; set; }
    public string? PreferredCurrency { get; set; } = "GBP";
    public List<string> TargetRoles { get; set; } = [];
    public List<string> TargetIndustries { get; set; } = [];
    public List<string> BlockedIndustries { get; set; } = [];
    public int? MaxCommuteMinutes { get; set; }
    public string? Notes { get; set; }
}
