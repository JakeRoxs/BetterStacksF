# Goal: Add Steam Networking support and enforce host-set values for stack sizes & options

**Status:** Proposed goal for the BetterStacks mod — integrate Steam networking (see `docs/networklib.md`) and make server/host authoritative for stack sizes and selected mod options.

---

## Summary
- Integrate the Steam networking library (implementation details in `docs/networklib.md`).
- Add server-authoritative enforcement so hosts can *set and lock* stack sizes and other selected options; clients must accept host-set values on join.

## Motivation 💡
- Prevent client-side config drift and cheating by making critical gameplay parameters server-authoritative.
- Support Steam networking features for more reliable peer-to-peer and dedicated-server scenarios.

## Background / Reference
- Steam networking design & APIs: `docs/networklib.md` (read first).
- Existing mod config: `MelonPreferences` (category `BetterStacks`) and `dist/BetterStacks_v2.1.0/mods/BetterStacksConfig.json` (example).

## In-scope
- Add Steam networking adapter and handshake per `docs/networklib.md`.
- Implement host-authoritative enforcement for stack sizes and a configurable set of other options.
- Schema + config changes, tests, and documentation updates.

## Out-of-scope
- UI redesigns beyond small config display changes.
- Non-Steam networking libraries (except where compatibility/fallback is explicitly handled).

## Requirements (high level)
1. When server/host defines a value (e.g., stack size), that value is authoritative and clients cannot override it.
2. Server sends authoritative configuration as part of the connection handshake.
3. Clients apply host values on join and reject conflicting local overrides.
4. Provide a configurable list (or per-field flag) of which config fields are host-enforced.
5. Backwards compatible: servers that do not use Steam networking keep current behavior.

## Acceptance criteria ✅
- [ ] The codebase successfully builds with Steam networking enabled and compiles when disabled.
- [ ] On client connection, the server sends a `HostConfig` message containing authoritative values.
- [ ] Clients automatically apply the `HostConfig` and cannot persist local changes to host-enforced fields.
- [ ] Unit tests cover enforcement logic and handshake serialization/deserialization.
- [ ] Integration test shows a client attempting to set a larger stack size is corrected/rejected when connecting to a host that enforces a smaller size.
- [ ] Documentation updated (`docs/networklib.md`, `docs/steam-networking-goal.md`) and `dist/BetterStacks_v2.1.0/mods/BetterStacksConfig.json` example updated.

## Implementation plan & tasks 🔧
- [ ] Add Steam networking adapter/module using `docs/networklib.md` as spec.
- [ ] Define `HostConfig` DTO and handshake message.
- [ ] Implement `HostConfigEnforcer` on server + client apply/validation path.
- [ ] Add config schema change: either per-field `serverAuthoritative: true` or a `serverEnforcedFields: ["stackSize", ...]` list exposed via MelonPreferences / ModConfig.
- [ ] Add unit tests (serialization, enforcement) and integration tests (client/server scenarios).
- [ ] Add feature-flag/config option `EnableSteamNetworking` and migration notes.
- [ ] Update documentation and add migration/compatibility notes in `dist/BetterStacks_v2.1.0/mods/BetterStacksConfig.json`.
- [ ] Add changelog and open PR with tests.

Estimated effort: small feature + tests (1–3 days), full integration/QA (additional 1–2 days).

## Design notes (technical) 🧩
- Server-authoritative handshake:
  - Server sends `HostConfig` immediately after authentication/connection acceptance.
  - Client receives and replaces local values for host-enforced fields; if a critical mismatch is detected, client should show a warning and auto-apply the server values.
- Enforcement approach:
  - Prefer explicit `serverEnforcedFields` list in config for clarity and future extensibility.
  - All enforcement must be validated server-side on any client-sent update; do not trust client state.
- Backwards compatibility: if handshake absent, preserve existing client-side config behavior.

## Security & Edge cases ⚠️
- Validate all values received from clients on the server; never accept client-initiated overrides for host-enforced fields.
- Handle version mismatches gracefully: if client/server protocol versions differ, reject the connection with a clear message.

## Tests & QA 🎯
- Unit: HostConfig serialization, enforcement logic, config-schema parsing.
- Integration: Dedicated server (Steam networking) + two clients — attempt to use disallowed stack sizes and verify enforcement.
- Manual: Start server without Steam networking and with Steam networking; verify behavior matches expectations.

## Decisions needed (please pick / confirm) ❗
- Which additional fields should be host-enforced beyond stack sizes? Suggested defaults:
  - `maxStackCount`
  - `stackMultiplier`
  - `pickupCooldown` (if applicable)
  - OR: enforce *all* mod config values server-side

  (If you prefer, we can add a `serverEnforcedFields` array so you can select later.)

## Migration notes
- Add `serverEnforcedFields` to ModConfig (exposed via MelonPreferences) and update `dist/BetterStacks_v2.1.0/mods/BetterStacksConfig.json` examples.
- Document the handshake behavior and explain client update flow in `docs/networklib.md`.

## Next steps ▶️
1. Confirm which additional fields to enforce (see Decisions above).
2. I can open an implementation PR that: adds the Steam networking adapter, `HostConfig` handshake, enforcement logic, tests, and docs.

---

References:
- `docs/networklib.md` — Steam networking details
- `MelonPreferences` (category `BetterStacks`) — current config
- `dist/BetterStacks_v2.1.0/mods/BetterStacksConfig.json` — published mod example
