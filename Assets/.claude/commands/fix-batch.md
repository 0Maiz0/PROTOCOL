I'm going to paste a list of bugs. For each one:

1. Identify the root cause and which file(s) need changes
2. Write the minimal fix — don't touch unrelated code
3. Note any dependencies between the bugs (fix order matters?)
4. Apply all fixes
5. Give me a summary of what changed per file

Rules:
- Check IsFrozen edge cases
- Check null refs (bots die mid-round)
- Any new enum goes in Enums.cs
- Don't refactor working systems
- If a fix requires a new script, explain what it does and where to attach it in Unity
