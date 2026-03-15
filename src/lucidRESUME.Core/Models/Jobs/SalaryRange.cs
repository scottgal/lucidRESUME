namespace lucidRESUME.Core.Models.Jobs;

public sealed record SalaryRange(decimal? Min, decimal? Max, string Currency = "GBP", string Period = "annual");
