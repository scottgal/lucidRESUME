using lucidRESUME.Core.Models.Profile;

namespace lucidRESUME.Core.Tests.Models;

public class UserProfileTests
{
    [Fact]
    public void BlockCompany_AddsToBlocklist()
    {
        var profile = new UserProfile();
        profile.BlockCompany("Amazon");
        Assert.Contains("Amazon", profile.BlockedCompanies);
    }

    [Fact]
    public void AddAvoidSkill_AppendsToList()
    {
        var profile = new UserProfile();
        profile.AvoidSkill("PHP", "Not interested in legacy web");
        Assert.Single(profile.SkillsToAvoid);
        Assert.Equal("PHP", profile.SkillsToAvoid[0].SkillName);
    }
}
