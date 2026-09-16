namespace Aiursoft.AgentKit.AgentRunner;

/// <summary>Runtime entry point for bounded and ordered AgentKit execution.</summary>
public static class AgentRunnerFactory
{
    public static IAgentRunner CreateBounded(IAgentModelClient model) => new BoundedAgentRunner(model);

    public static OrderedAgentRunner CreateOrdered(IAgentModelClient model) => new(model);
}
