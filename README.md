# SpiritVale Overlay — Player Nameplate

Standalone overlay plugin (not BepInEx). WoW-style personal resource plate for the [SpiritVale Overlay](https://github.com/MUDesigns/SpiritVale-Overlay).

This repo is **not** part of the overlay tree. It is a port of [SpiritVale-PlayerNameplate](https://github.com/MUDesigns/SpiritVale-PlayerNameplate) onto `ISpiritValePlugin`.

## Install (DLL)

1. Install and run a current [SpiritVale Overlay](https://github.com/MUDesigns/SpiritVale-Overlay) (Npcap + overlay host). HP, mana, and auras need an overlay build that decodes Health/Skills sync and `ApplyEffectDisplays_O`.
2. Download the zip from the latest [Release](https://github.com/MUDesigns/SpiritVale-Overlay-PlayerNamePlate/releases).
3. Extract so you have a folder named `SpiritVale.Overlay.PlayerNameplate` containing `SpiritVale.Overlay.PlayerNameplate.dll`.
4. Copy that folder to:

   `%AppData%\SpiritValeOverlay\plugins\SpiritVale.Overlay.PlayerNameplate\`

   Full path example:

   `C:\Users\<you>\AppData\Roaming\SpiritValeOverlay\plugins\SpiritVale.Overlay.PlayerNameplate\SpiritVale.Overlay.PlayerNameplate.dll`
5. Fully quit the overlay host and start it again.
6. On the **PLUGINS** tab, enable **SpiritVale Player Nameplate** if it is not already on.

**Ctrl+F2** opens the nameplate config (rebind in overlay Settings). Overlay **F2** is the plugin manager.

## Requirements (build from source)

- .NET 8 SDK
- Overlay checkout at `X:\projects\SpiritVale-Overlay` (override with `/p:OverlayRoot=...`)
- Overlay host built at least once so the API project exists
- Overlay Capture that decodes Health/Mana sync, `CastBegin_C` `castTime`, and `ApplyEffectDisplays_O` into protocol Fields (this plugin's companion overlay changes)

## Build

```powershell
dotnet build SpiritVale.Overlay.PlayerNameplate.slnx -c Release
```

The build copies `SpiritVale.Overlay.PlayerNameplate.dll` to:

- Overlay host `Plugins\SpiritVale.Overlay.PlayerNameplate\`
- `dist\win-x64\Plugins\SpiritVale.Overlay.PlayerNameplate\` (if that publish folder exists)
- `%AppData%\SpiritValeOverlay\plugins\SpiritVale.Overlay.PlayerNameplate\`

Relaunch the overlay host after copying. Enable the plugin on the **PLUGINS** tab if needed.

## Usage

Sign into a character with overlay capture running. **Ctrl+F2** opens the config panel (rebind in plugin Settings). Overlay **F2** toggles the manager, so the default is Ctrl+F2.

Profiles: `%AppData%\SpiritValeOverlay\player-nameplate\characters\<id>.json`  
Same JSON schema as the BepInEx Player Nameplate — you can copy those character files in.

### Features

- Health and mana bars with `%` / Cur / Cur/Max / Both text
- Name, level / job, class, class-tinted HP
- Cast bar from `CastBegin_C`
- Barrier amount on the HP text when the server reports it
- Buff / debuff icon rows (filter, size, duration, stacks)
- Always or combat-only visibility, fade-after-combat, low-HP pulse
- Scale, width, bar height, gap, opacity
- Swap HP/mana order; free-move parts while the config is open

## Overlay API

Needs `CooldownSlot`, window flags, `CaptureMouse`, and decoded protocol Fields for:

- `syncType` + `HealthComponent` / `SkillsComponent` → `currentHealth` / `maxHealth` / `currentMana` / `maxMana`
- `CastBegin_C` → `skillId`, `castTime`
- `ApplyEffectDisplays_O` → `effectApplies` / `effectRemoves`
