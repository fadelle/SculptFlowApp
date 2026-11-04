namespace Omni.App.Tests.Helpers;

public class TraceTextTests
{
    [Fact]
    public void An_error_names_the_innermost_cause_and_stays_within_the_cap()
    {
        // QA t4-ai-03: the trace said only "DbUpdateException: … see the inner exception".
        var wrapped = new InvalidOperationException("An error occurred while saving the entity changes. See the inner exception for details.",
            new ArgumentException("Cannot write DateTimeOffset with Offset=02:00:00"));
        Assert.Equal("InvalidOperationException: An error occurred while saving the entity changes. See the inner exception for details. | ArgumentException: Cannot write DateTimeOffset with Offset=02:00:00",
            TraceText.Error(wrapped));
        Assert.Equal("TimeoutException: slow", TraceText.Error(new TimeoutException("slow")));
        Assert.Equal(20, TraceText.Error(wrapped, 20).Length);
    }
}
