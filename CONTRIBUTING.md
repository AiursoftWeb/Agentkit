# Contributing

Keep the core provider-neutral and dependency-light. Add deterministic MSTest coverage for behavior changes.

Before submitting a change, run only the focused test project and filter that cover the changed behavior. The GitLab pipeline performs solution-wide testing, coverage aggregation, linting, packaging, and release validation.

Use the repository lint gate before submitting formatting- or analyzer-sensitive changes:

```bash
./lint.sh
```

For package changes, verify the affected library can be packed in Release configuration. CI packages all four libraries from `Aiursoft.AgentKit.sln`; master pipelines publish them only after the protected NuGet API-key variables are configured.
