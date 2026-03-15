using lucidRESUME.Core.Models.Filters;
using lucidRESUME.Core.Models.Profile;

namespace lucidRESUME.Core.Tests.Models;

public class FilterNodeTests
{
    [Fact]
    public void IsLeaf_TrueForLeafNode()
    {
        var node = FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" });
        Assert.True(node.IsLeaf);
    }

    [Fact]
    public void All_IsNotLeaf()
    {
        var node = FilterNode.All(
            FilterNode.Leaf("skills", FilterOp.In, new[] { ".NET" })
        );
        Assert.False(node.IsLeaf);
        Assert.Equal(FilterLogic.All, node.Logic);
    }
}

public class UserProfileVotingTests
{
    [Fact]
    public void VoteUp_IncrementsScore()
    {
        var profile = new UserProfile();
        profile.VoteUp(AspectType.Skill, ".NET");
        profile.VoteUp(AspectType.Skill, ".NET");
        Assert.Equal(2, profile.GetVoteScore(AspectType.Skill, ".NET"));
    }

    [Fact]
    public void VoteDown_ClampsAtMinusFive()
    {
        var profile = new UserProfile();
        for (int i = 0; i < 10; i++) profile.VoteDown(AspectType.Skill, "PHP");
        Assert.Equal(-5, profile.GetVoteScore(AspectType.Skill, "PHP"));
    }
}
