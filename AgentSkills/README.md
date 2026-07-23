# ProxyCore — Agent Skills (canonical source)

This folder is the **canonical, shipped copy** of ProxyCore's LLM agent skill(s).

It lives inside the package as regular content, so it travels with the package to
consuming projects: the deploy workflow copies all of `Assets/ProxyCore/` into the
published `com.shakotis.proxycore` package, and the consuming project retrieves it as
normal package contents (it does not pull the whole repo). This is a normal folder — not
a `Samples~`-style optional folder — so it is always present in an installed package.

## Installing into a project

In the Unity Editor, use **ProxyCore ▸ Install Agent Skill**. It copies `proxycore/`
into the current project's agent folder(s) — for Claude Code that's
`<project>/.claude/skills/proxycore/`. See `Editor/Global Actions/InstallAgentSkill.cs`.

## Editing

Edit the skill **here** (this is the source of truth), then re-run
**ProxyCore ▸ Install Agent Skill** to refresh any installed copies. The repo-root
`.claude/skills/proxycore/` in the ProxyCore dev project is an installed copy kept for
developing ProxyCore itself.
