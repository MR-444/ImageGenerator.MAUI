using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public class ComfyUiWsEventTests
{
    [Fact]
    public void Progress_ParsesValueMaxAndPromptId()
    {
        var evt = ComfyUiWsEvent.TryParse(
            """{ "type": "progress", "data": { "value": 14, "max": 20, "prompt_id": "abc", "node": "5" } }""");

        evt.Should().Be(new ComfyUiWsEvent.Progress("abc", 14, 20));
    }

    [Fact]
    public void ExecutionStart_ParsesPromptId()
    {
        ComfyUiWsEvent.TryParse("""{ "type": "execution_start", "data": { "prompt_id": "abc" } }""")
            .Should().Be(new ComfyUiWsEvent.ExecutionStart("abc"));
    }

    [Fact]
    public void ExecutingWithNullNode_IsCompleted()
    {
        // ComfyUI's original completion signal.
        ComfyUiWsEvent.TryParse("""{ "type": "executing", "data": { "node": null, "prompt_id": "abc" } }""")
            .Should().Be(new ComfyUiWsEvent.Completed("abc"));
    }

    [Fact]
    public void ExecutingWithNode_IsIgnored()
    {
        ComfyUiWsEvent.TryParse("""{ "type": "executing", "data": { "node": "98:12", "prompt_id": "abc" } }""")
            .Should().BeNull("a node starting execution is not a signal the app reacts to");
    }

    [Fact]
    public void ExecutionSuccess_IsCompleted()
    {
        ComfyUiWsEvent.TryParse("""{ "type": "execution_success", "data": { "prompt_id": "abc" } }""")
            .Should().Be(new ComfyUiWsEvent.Completed("abc"));
    }

    [Theory]
    [InlineData("execution_error")]
    [InlineData("execution_interrupted")]
    public void ExecutionErrorAndInterrupted_AreFailed(string type)
    {
        ComfyUiWsEvent.TryParse($$"""{ "type": "{{type}}", "data": { "prompt_id": "abc" } }""")
            .Should().Be(new ComfyUiWsEvent.Failed("abc"));
    }

    [Fact]
    public void StatusAndUnknownTypes_AreIgnored()
    {
        ComfyUiWsEvent.TryParse(
            """{ "type": "status", "data": { "status": { "exec_info": { "queue_remaining": 2 } } } }""")
            .Should().BeNull();
        ComfyUiWsEvent.TryParse("""{ "type": "crystools.monitor", "data": { "cpu": 12 } }""")
            .Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"type\": \"progress\" }")]
    [InlineData("{ \"type\": 42, \"data\": {} }")]
    [InlineData("[]")]
    public void GarbageOrWrongShapes_ReturnNullWithoutThrowing(string json)
    {
        ComfyUiWsEvent.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void Progress_MissingNumbers_IsIgnored()
    {
        ComfyUiWsEvent.TryParse("""{ "type": "progress", "data": { "prompt_id": "abc" } }""")
            .Should().BeNull();
    }
}
