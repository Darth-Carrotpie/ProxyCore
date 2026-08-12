# ProxyCore — Agent Skills (canonical source)

This folder is the **canonical, shipped copy** of ProxyCore's LLM agent skill(s).

It lives inside the package as regular content, so it travels with the package to
consuming projects: the deploy workflow copies all of `Assets/ProxyCore/` into the
published `com.shakotis.proxycore` package, and the consuming project retrieves it as
normal package contents (it does not pull the whole repo). This is a normal folder — not
a `Samples~`-style optional folder — so it is always present in an installed package.

## Installing into a project

In the Unity Editor, use **ProxyCore ▸ Install Agent Skill**, choose the agents used by
the project, and install a complete copy into each selected native location:

| Agent | Destination |
|---|---|
| Claude Code | `<project>/.claude/skills/proxycore/` |
| GitHub Copilot | `<project>/.github/skills/proxycore/` |
| OpenAI Codex | `<project>/.agents/skills/proxycore/` |

The matrix follows the current project-scope locations documented by
[Claude Code](https://code.claude.com/docs/en/skills#where-skills-live),
[GitHub Copilot](https://docs.github.com/en/copilot/concepts/agents/about-agent-skills),
and [OpenAI Codex](https://learn.chatgpt.com/docs/build-skills#where-codex-loads-local-skills).

Provider detection is informational, not an installation gate. Selected destinations
are validated and staged before being committed as one transaction. Each installed
copy includes `.proxycore-skill-install.json`, which identifies the managed payload for
deterministic updates, repair, and safe uninstall. `.meta` files are never copied.

An unmanaged or locally modified destination is preserved unless the user explicitly
approves replacement. Extra files not owned by the manifest survive updates.
**Uninstall Managed** likewise removes only manifest-owned files and preserves
unmanaged folders and extras.

The installer also migrates its legacy integration:

- The old Copilot pointer at
  `.github/instructions/proxycore.instructions.md` is deleted only when its content
  exactly matches the installer-generated version.
- The old ProxyCore block in `AGENTS.md` is removed only when one valid marker pair is
  present; all unrelated content is retained.

[Copilot may also scan](https://docs.github.com/en/copilot/concepts/agents/about-agent-skills)
`.agents/skills` and `.claude/skills`, but
[its CLI gives](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference#skills-reference)
`.github/skills` precedence for a duplicate skill name. The managed skill payload is
kept synchronized across selected destinations.

For a manual installation, copy this `proxycore/` directory to one of the native
locations above.

## Editing

Edit the skill **here** (this is the source of truth), then re-run
**ProxyCore ▸ Install Agent Skill** to refresh any installed copies. The repo-root
`.claude/skills/proxycore/` in the ProxyCore dev project is an installed copy kept for
developing ProxyCore itself.
