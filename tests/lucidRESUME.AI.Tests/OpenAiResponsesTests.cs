using lucidRESUME.AI;

namespace lucidRESUME.AI.Tests;

public sealed class OpenAiResponsesTests
{
    [Fact]
    public void ExtractOutputText_ReadsResponsesApiMessageContent()
    {
        const string response = """
            {
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "{\"markdown\":\"# Jane\",\"evidence_links\":[],\"warnings\":[]}" }
                  ]
                }
              ]
            }
            """;

        var text = OpenAiTailoringService.ExtractOutputText(response);

        Assert.Contains("# Jane", text);
    }
}
