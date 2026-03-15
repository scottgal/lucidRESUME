using NUnit.Framework;
using lucidRESUME.UXTesting.Scripts;

namespace lucidRESUME.UXTesting.Tests;

[TestFixture]
public class ScriptModelTests
{
    [Test]
    public void UXScript_DefaultValues_AreCorrect()
    {
        var script = new UXScript();
        
        Assert.That(script.Name, Is.Empty);
        Assert.That(script.Description, Is.Empty);
        Assert.That(script.DefaultDelay, Is.EqualTo(200));
        Assert.That(script.Actions, Is.Empty);
        Assert.That(script.Variables, Is.Empty);
    }

    [Test]
    public void UXAction_DefaultValues_AreCorrect()
    {
        var action = new UXAction();
        
        Assert.That(action.Target, Is.Null);
        Assert.That(action.Value, Is.Null);
        Assert.That(action.DelayMs, Is.EqualTo(100));
        Assert.That(action.Description, Is.Null);
    }

    [Test]
    public void UXAction_Type_CanBeSet()
    {
        var action = new UXAction { Type = ActionType.Click };
        
        Assert.That(action.Type, Is.EqualTo(ActionType.Click));
    }

    [Test]
    public void ActionType_AllValues_AreDefined()
    {
        var expectedTypes = new[]
        {
            ActionType.Click,
            ActionType.DoubleClick,
            ActionType.RightClick,
            ActionType.TypeText,
            ActionType.PressKey,
            ActionType.Hover,
            ActionType.Scroll,
            ActionType.Wait,
            ActionType.Navigate,
            ActionType.Screenshot,
            ActionType.Assert
        };
        
        Assert.That(Enum.GetValues<ActionType>(), Is.EquivalentTo(expectedTypes));
    }

    [Test]
    public void UXTestResult_DefaultValues_AreCorrect()
    {
        var result = new UXTestResult();
        
        Assert.That(result.ScriptName, Is.Empty);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ActionResults, Is.Empty);
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public void UXTestResult_Duration_CalculatesCorrectly()
    {
        var result = new UXTestResult
        {
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow
        };
        
        Assert.That(result.Duration.TotalSeconds, Is.GreaterThanOrEqualTo(5).And.LessThan(6));
    }

    [Test]
    public void UXActionResult_DefaultValues_AreCorrect()
    {
        var result = new UXActionResult();
        
        Assert.That(result.Action, Is.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ScreenshotPath, Is.Null);
        Assert.That(result.ErrorMessage, Is.Null);
        Assert.That(result.Metrics, Is.Empty);
    }
}
