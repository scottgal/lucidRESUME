using NUnit.Framework;
using System.Reflection;

namespace lucidRESUME.UXTesting.Tests;

[TestFixture]
public class UXContextTests
{
    private UXContext _context = null!;
    private MockViewModel _mockViewModel = null!;

    [SetUp]
    public void Setup()
    {
        _mockViewModel = new MockViewModel 
        { 
            Name = "TestVM", 
            Count = 42,
            Nested = new NestedModel { Value = "NestedValue" }
        };
        
        _context = new UXContext
        {
            MainWindow = null,
            Navigate = _ => { }
        };
    }

    [Test]
    public void GetProperty_SimpleProperty_ReturnsValue()
    {
        var result = _context.GetProperty(_mockViewModel, "Name");
        
        Assert.That(result, Is.EqualTo("TestVM"));
    }

    [Test]
    public void GetProperty_NumericProperty_ReturnsValue()
    {
        var result = _context.GetProperty(_mockViewModel, "Count");
        
        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public void GetProperty_NestedProperty_ReturnsValue()
    {
        var result = _context.GetProperty(_mockViewModel, "Nested.Value");
        
        Assert.That(result, Is.EqualTo("NestedValue"));
    }

    [Test]
    public void GetProperty_NonExistentProperty_ReturnsErrorMessage()
    {
        var result = _context.GetProperty(_mockViewModel, "NonExistent");
        
        Assert.That(result, Does.StartWith("Property not found"));
    }

    [Test]
    public void GetProperty_NullTarget_ReturnsNull()
    {
        var result = _context.GetProperty(null!, "Name");
        
        Assert.That(result, Is.Null);
    }

    [Test]
    public void SetProperty_SimpleProperty_UpdatesValue()
    {
        var result = _context.SetProperty(_mockViewModel, "Name", "NewName");
        
        Assert.That(result, Is.True);
        Assert.That(_mockViewModel.Name, Is.EqualTo("NewName"));
    }

    [Test]
    public void SetProperty_NumericProperty_UpdatesValue()
    {
        var result = _context.SetProperty(_mockViewModel, "Count", 100);
        
        Assert.That(result, Is.True);
        Assert.That(_mockViewModel.Count, Is.EqualTo(100));
    }

    [Test]
    public void SetProperty_StringToNumeric_ConvertsCorrectly()
    {
        var result = _context.SetProperty(_mockViewModel, "Count", "200");
        
        Assert.That(result, Is.True);
        Assert.That(_mockViewModel.Count, Is.EqualTo(200));
    }

    [Test]
    public void SetProperty_ReadOnlyProperty_ReturnsFalse()
    {
        var result = _context.SetProperty(_mockViewModel, "ReadOnly", "value");
        
        Assert.That(result, Is.False);
    }

    [Test]
    public void SetProperty_NonExistentProperty_ReturnsFalse()
    {
        var result = _context.SetProperty(_mockViewModel, "NonExistent", "value");
        
        Assert.That(result, Is.False);
    }

    [Test]
    public void SetProperty_NestedProperty_UpdatesValue()
    {
        var result = _context.SetProperty(_mockViewModel, "Nested.Value", "NewNestedValue");
        
        Assert.That(result, Is.True);
        Assert.That(_mockViewModel.Nested.Value, Is.EqualTo("NewNestedValue"));
    }

    [Test]
    public void GetProperty_DeeplyNested_ReturnsValue()
    {
        _mockViewModel.DeepNested = new DeepNestedModel 
        { 
            Level = new NestedModel { Value = "DeepValue" } 
        };
        
        var result = _context.GetProperty(_mockViewModel, "DeepNested.Level.Value");
        
        Assert.That(result, Is.EqualTo("DeepValue"));
    }

    [Test]
    public void SetProperty_DeeplyNested_UpdatesValue()
    {
        _mockViewModel.DeepNested = new DeepNestedModel 
        { 
            Level = new NestedModel { Value = "Old" } 
        };
        
        var result = _context.SetProperty(_mockViewModel, "DeepNested.Level.Value", "New");
        
        Assert.That(result, Is.True);
        Assert.That(_mockViewModel.DeepNested.Level.Value, Is.EqualTo("New"));
    }
}

internal class MockViewModel
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public NestedModel Nested { get; set; } = new();
    public DeepNestedModel? DeepNested { get; set; }
    public string ReadOnly => "readonly";
}

internal class NestedModel
{
    public string Value { get; set; } = "";
}

internal class DeepNestedModel
{
    public NestedModel Level { get; set; } = new();
}
