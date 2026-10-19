---
name: lane
description: Lanes (parallel checkouts, each with its own Unity editor): used only when the user tells you to. Claiming one, the rules while you hold it, releasing it, and relinquishing another agent's lane. Load it when the user asks you to work in a lane, or to free one.
argument-hint: "[lane] | release | free <lane>"
allowed-tools: Bash(pwsh -NoProfile -File *)
---

<!-- A template (UnityJigs Lanes~/skill-template.md): copy to <repo>/.claude/skills/lane/SKILL.md and replace
     <LANE> with the path of this repo's lane script (a forwarder such as tools/lane.ps1, see
     forwarder-template.ps1) and <README> with where the workflow is described for this repo. -->

# Lanes

Your session id is `${CLAUDE_SESSION_ID}`. Asked with: `$ARGUMENTS`.

Right now:

!`pwsh -NoProfile -File "<LANE>" status -Session ${CLAUDE_SESSION_ID}`

The lane script is `pwsh -NoProfile -File <LANE>` (below: `lane.ps1`). The whole workflow, and why, is
<README> (UnityJigs `Lanes~/README.md`): read it before your first claim in a session.

- **Only when the user tells you to.** Otherwise work in the main checkout as usual.
- **Claim:** `lane.ps1 claim -Session ${CLAUDE_SESSION_ID} -Name <your ListAgents name> -Task "<one line>"`
  (lane A first; a letter asks for that lane). No free lane: ask the user; never take a held lane.
- **Then:** read the lane's memory file (`lane_<id>.md`), check `git -C <lane root> status -sb` and its
  editor, and tell the user which lane you're in (they `/rename` your tab; you never do).
- **While you hold it:** `--project-path <lane root>\<UnityProject>` on every Unity CLI call; absolute paths
  under the lane root; `git -C <lane root>`; no commits or pushes unless told; lane branches are never pushed;
  verify with the lane's editor, not the IDE hook; keep `lane_<id>.md` current, and touch no other lane's.
- **Release:** update `lane_<id>.md`, then `lane.ps1 release -Session ${CLAUDE_SESSION_ID}`. Uncommitted work
  may stay for the next holder.
- **Freeing someone else's lane:** only its holder releases it. Ask a running holder with `SendMessage`; a
  closed one only after the user agrees, by resuming its transcript headless (the README has the command).
