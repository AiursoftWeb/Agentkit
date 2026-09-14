# Aiursoft.AgentKit.AgentRunner

Runtime package for AgentKit applications.

It depends on `Aiursoft.AgentKit` contracts and provides compatibility entry points for the bounded batch runner and ordered approval/checkpoint runner. Use the ordered runner for hosts that require a complete tool-result batch before model continuation, such as Kanban's per-operation approval workflow.

Model providers, MCP integration, authorization, queues, persistence, approval UI, and application-specific policy remain separate adapters.
