## Summary
<!-- Briefly explain the purpose of this PR and the specific problem it solves. -->

## Related Issues
<!-- Link related GitHub issues (e.g., "Closes #42" or "Resolves #42"), or write "N/A". -->
Closes #

## Type of Change
- [ ] 🐛 Bugfix (`fix:`)
- [ ] ✨ New Feature (`feat:`)
- [ ] 🔒 Security Hardening (`fix:` / `security:`)
- [ ] ⚡ Performance Improvement (`perf:`)
- [ ] ♻️ Refactoring (`refactor:` — no functional changes)
- [ ] 📝 Documentation (`docs:`)
- [ ] 🔧 Chore / CI / Dependencies (`chore:` / `build:` / `ci:`)

## Changes Made
<!-- List the key technical changes across Presentation, Application (Services), and Gateway layers. -->
* 
* 

## How to Test
<!-- Provide clear steps or commands to verify the changes. -->
1. 
2. 

## Screenshots / UI Preview (if applicable)
<!-- Add before/after screenshots or recordings for MudBlazor UI or theme changes. -->

## Pre-Merge Checklist
- [ ] I have performed a self-review of my code
- [ ] The project builds (`dotnet build`) and all unit tests pass (`dotnet test`) locally
- [ ] New or modified business/gateway logic is covered by unit tests in `tests/SynapseAdmin.Tests/` (if applicable)
- [ ] Architecture conventions are followed (N-Tier separation, `.razor.cs` code-behind, `ViewModels`, no `LibMatrix` submodule modifications)
- [ ] Error handling uses `OperationResult` / `OperationResult.Cancelled()` without leaking raw exception details (`ex.Message`) to the UI
- [ ] User-facing strings are localized across `EN`, `DE`, and `FR` resource files (if applicable)
- [ ] No sensitive data (passwords, access tokens, keys) has been committed
