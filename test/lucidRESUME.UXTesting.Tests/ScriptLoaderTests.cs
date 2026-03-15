using NUnit.Framework;
using lucidRESUME.UXTesting.Scripts;

namespace lucidRESUME.UXTesting.Tests;

[TestFixture]
public class ScriptLoaderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ux-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void Teardown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void LoadFromYaml_ValidYaml_ReturnsScript()
    {
        var yaml = """
            name: test-script
            description: A test script
            default_delay: 300
            
            actions:
              - type: Navigate
                value: Home
              - type: Click
                target: SubmitButton
              - type: Screenshot
                value: result
            """;
        
        var path = Path.Combine(_tempDir, "test.ux.yaml");
        File.WriteAllText(path, yaml);
        
        var result = ScriptLoader.LoadFromYaml(path);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("test-script"));
        Assert.That(result.Description, Is.EqualTo("A test script"));
        Assert.That(result.DefaultDelay, Is.EqualTo(300));
        Assert.That(result.Actions, Has.Count.EqualTo(3));
    }

    [Test]
    public void LoadFromYaml_WithDelayMs_ParsesCorrectly()
    {
        var yaml = """
            name: delay-test
            actions:
              - type: Wait
                value: "1000"
                delay_ms: 500
            """;
        
        var path = Path.Combine(_tempDir, "delay.yaml");
        File.WriteAllText(path, yaml);
        
        var result = ScriptLoader.LoadFromYaml(path);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Actions[0].DelayMs, Is.EqualTo(500));
    }

    [Test]
    public void LoadFromYaml_AllActionTypes_ParsesCorrectly()
    {
        var yaml = """
            name: all-actions
            actions:
              - type: Navigate
                value: Page1
              - type: Click
                target: Button1
              - type: DoubleClick
                target: Item1
              - type: TypeText
                target: TextBox1
                value: "Hello"
              - type: PressKey
                value: Enter
              - type: Wait
                value: "500"
              - type: Screenshot
                value: screen1
              - type: Assert
                target: CurrentPage
                value: visible:true
            """;
        
        var path = Path.Combine(_tempDir, "all.yaml");
        File.WriteAllText(path, yaml);
        
        var result = ScriptLoader.LoadFromYaml(path);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Actions, Has.Count.EqualTo(8));
        Assert.That(result.Actions[0].Type, Is.EqualTo(ActionType.Navigate));
        Assert.That(result.Actions[1].Type, Is.EqualTo(ActionType.Click));
        Assert.That(result.Actions[2].Type, Is.EqualTo(ActionType.DoubleClick));
        Assert.That(result.Actions[3].Type, Is.EqualTo(ActionType.TypeText));
        Assert.That(result.Actions[4].Type, Is.EqualTo(ActionType.PressKey));
        Assert.That(result.Actions[5].Type, Is.EqualTo(ActionType.Wait));
        Assert.That(result.Actions[6].Type, Is.EqualTo(ActionType.Screenshot));
        Assert.That(result.Actions[7].Type, Is.EqualTo(ActionType.Assert));
    }

    [Test]
    public void LoadFromYaml_NoNameField_SetsNameFromFile()
    {
        var yaml = """
            default_delay: 200
            actions:
              - type: Navigate
                value: Home
            """;
        
        var path = Path.Combine(_tempDir, "auto-named-script.yaml");
        File.WriteAllText(path, yaml);
        
        var result = ScriptLoader.LoadFromYaml(path);
        
        Assert.That(result.Name, Is.EqualTo("auto-named-script"));
    }

    [Test]
    public void LoadFromYaml_WithVariables_ParsesCorrectly()
    {
        var yaml = """
            name: vars-test
            variables:
              page_name: Home
              button: Submit
            
            actions:
              - type: Navigate
                value: ${page_name}
              - type: Click
                target: ${button}
            """;
        
        var path = Path.Combine(_tempDir, "vars.yaml");
        File.WriteAllText(path, yaml);
        
        var result = ScriptLoader.LoadFromYaml(path);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Variables, Contains.Key("page_name"));
        Assert.That(result.Variables["page_name"], Is.EqualTo("Home"));
    }

    [Test]
    public void LoadFromJson_ValidJson_ReturnsScript()
    {
        var json = """
            {
              "name": "json-test",
              "description": "A JSON test",
              "default_delay": 200,
              "actions": [
                { "type": "Navigate", "value": "Home" },
                { "type": "Click", "target": "Button1" }
              ]
            }
            """;
        
        var path = Path.Combine(_tempDir, "test.json");
        File.WriteAllText(path, json);
        
        var result = ScriptLoader.LoadFromJson(path);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Name, Is.EqualTo("json-test"));
        Assert.That(result.Actions, Has.Count.EqualTo(2));
    }

    [Test]
    public void SaveAsYaml_CreatesValidFile()
    {
        var script = new UXScript
        {
            Name = "save-test",
            DefaultDelay = 100,
            Actions = new List<UXAction>
            {
                new() { Type = ActionType.Navigate, Value = "Home" },
                new() { Type = ActionType.Click, Target = "Button1" }
            }
        };
        
        var path = Path.Combine(_tempDir, "saved.yaml");
        ScriptLoader.SaveAsYaml(script, path);
        
        Assert.That(File.Exists(path), Is.True);
        
        var loaded = ScriptLoader.LoadFromYaml(path);
        Assert.That(loaded.Name, Is.EqualTo("save-test"));
        Assert.That(loaded.Actions, Has.Count.EqualTo(2));
    }

    [Test]
    public void SaveAsJson_CreatesValidFile()
    {
        var script = new UXScript
        {
            Name = "save-json-test",
            Actions = new List<UXAction>
            {
                new() { Type = ActionType.Screenshot, Value = "screen1" }
            }
        };
        
        var path = Path.Combine(_tempDir, "saved.json");
        ScriptLoader.SaveAsJson(script, path);
        
        Assert.That(File.Exists(path), Is.True);
        var content = File.ReadAllText(path);
        Assert.That(content, Does.Contain("save-json-test"));
    }

    [Test]
    public void LoadFromDirectory_LoadsMultipleScripts()
    {
        var yaml1 = """
            name: script1
            actions:
              - type: Navigate
                value: Page1
            """;
        var yaml2 = """
            name: script2
            actions:
              - type: Navigate
                value: Page2
            """;
        
        File.WriteAllText(Path.Combine(_tempDir, "test1.ux.yaml"), yaml1);
        File.WriteAllText(Path.Combine(_tempDir, "test2.ux.yaml"), yaml2);
        
        var results = ScriptLoader.LoadFromDirectory(_tempDir).ToList();
        
        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.Select(s => s.Name), Contains.Item("script1").And.Contains("script2"));
    }
}
