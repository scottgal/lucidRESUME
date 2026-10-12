using lucidRESUME.Core.Models.Coverage;

namespace lucidRESUME.Core.Models.Jobs;

/// <summary>A source-authored competency and its intact supporting description.</summary>
public sealed record JobCompetency(string Name, string Description, RequirementPriority Priority);
