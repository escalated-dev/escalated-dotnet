using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class WorkflowEngineTests
{
    private readonly WorkflowEngine _engine = new();

    [Fact]
    public void ApplyOperator_Equals_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.ApplyOperator("equals", "open", "open"));
    }

    [Fact]
    public void ApplyOperator_NotEquals_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.ApplyOperator("not_equals", "open", "closed"));
    }

    [Fact]
    public void ApplyOperator_Contains_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.ApplyOperator("contains", "billing issue", "billing"));
    }

    [Fact]
    public void ApplyOperator_IsEmpty_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.ApplyOperator("is_empty", "", ""));
    }

    [Fact]
    public void ApplyOperator_IsNotEmpty_ReturnsTrue()
    {
        Assert.True(WorkflowEngine.ApplyOperator("is_not_empty", "value", ""));
    }

    [Fact]
    public void InterpolateVariables_ReplacesPlaceholders()
    {
        var ticket = new Dictionary<string, string> { { "reference", "ESC-001" }, { "status", "open" } };
        var result = WorkflowEngine.InterpolateVariables("Ticket {{reference}} is {{status}}", ticket);
        Assert.Equal("Ticket ESC-001 is open", result);
    }

    [Fact]
    public void EvaluateSingle_MatchesCondition()
    {
        var condition = new Dictionary<string, string> { { "field", "status" }, { "operator", "equals" }, { "value", "open" } };
        var ticket = new Dictionary<string, string> { { "status", "open" } };
        Assert.True(_engine.EvaluateSingle(condition, ticket));
    }
}
