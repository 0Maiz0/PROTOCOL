# Bug Fix Workflow

When Maiz reports a bug:

1. **Read the report** — usually a short bullet list or screenshot description
2. **Find the root cause** — check the specific file, don't assume
3. **Minimum-change fix** — touch only the file(s) that cause the bug
4. **Don't "improve" surrounding code** while fixing a bug
5. **Don't remove working features** to simplify a fix
6. **Test edge cases mentally** — what if bot is dead? What if round ended? What if IsFrozen?
7. **Deliver the fix** as a complete file replacement with a 1-2 line explanation of what changed

## Common Bug Patterns We've Seen
- Bot acting during freeze time → missing `IsFrozen` check
- Execute strategy pushing immediately → staging positions not set or commit check missing
- CT abandoning hold → seek-cover logic overriding hold position
- Duels not triggering → FOV too narrow (needs 270° when stationary)
- CS0246 "type not found" → enum defined in wrong file, should be in Enums.cs
- Bots not moving → NavMeshAgent not on NavMesh, or path waypoint name typo
- Mid-round panel appearing when bomb already planted → missing plant state check
