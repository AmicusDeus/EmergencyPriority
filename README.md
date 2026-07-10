# Emergency Priority

Keeps responding fire engines, ambulances and police cars alive and moving through traffic jams in **Cities: Skylines II**.

**Paradox Mods:** https://mods.paradoxplaza.com/mods/150540

## What it does
Vanilla CS2 deletes a responding emergency vehicle the moment it's flagged as stuck in traffic, and never re-evaluates its route after dispatch. This mod:

- **Despawn guard** — when a responder is flagged stuck, it requests a fresh route instead of being deleted, so it keeps responding.
- **Re-route around congestion** — a responder blocked behind traffic for too long looks for a new route (vanilla only prices congestion once, at dispatch).

Everything is opt-in; turn the mod off for exact vanilla behaviour.

## Options (Options → Mods → Emergency Priority)
- Enable / disable
- Prevent stuck responders from despawning
- Re-route around congestion
- Re-route after N seconds blocked (slider)

## Under the hood (for the curious / security-minded)
- **Pure ECS — no Harmony patches.** It reads emergency vehicles (`CarFlags.Emergency`) and writes `PathOwner.m_State`, the same field the game itself uses to request a repath.
- **No network access at all** — no HTTP, no sockets. Nothing leaves your machine.
- **Filesystem:** writes only its own settings file and a log (`EmergencyPriority.Mod.log`) in the game's log folder. Nothing else.
- **Dependencies:** none beyond the base game.

You don't have to take my word for it — the full source is here, and a compiled .NET mod decompiles cleanly (ILSpy/dnSpy) if you want to confirm the DLL matches.

## Build from source
Requires the official CS2 modding toolchain. `dotnet build -c Release` compiles and deploys to your local Mods folder.

## License
[MIT](LICENSE).

---

*Made with [Claude Code](https://claude.com/claude-code), Anthropic's agentic coding tool.*
