WoW-style personal HP / mana / cast / aura nameplate for [SpiritVale Overlay](https://github.com/MUDesigns/SpiritVale-Overlay). Not a BepInEx mod.

## Install

1. Install a **current** [SpiritVale Overlay](https://github.com/MUDesigns/SpiritVale-Overlay) (Npcap + host). HP, mana, and auras need an overlay that decodes Health/Skills sync and `ApplyEffectDisplays_O`.
2. Download **SpiritVale.Overlay.PlayerNameplate-1.3.1.zip** below.
3. Extract and copy the `SpiritVale.Overlay.PlayerNameplate` folder to:

   `%AppData%\SpiritValeOverlay\plugins\`

   You should end up with:

   `%AppData%\SpiritValeOverlay\plugins\SpiritVale.Overlay.PlayerNameplate\SpiritVale.Overlay.PlayerNameplate.dll`
4. Fully quit the overlay and start it again.
5. On the **PLUGINS** tab, enable **SpiritVale Player Nameplate**.

**Ctrl+F2** opens the nameplate config. Overlay **F2** is the plugin manager.

## Usage

Sign in with overlay capture running. Per-character settings live at:

`%AppData%\SpiritValeOverlay\player-nameplate\characters\<id>.json`

Same JSON schema as the BepInEx Player Nameplate — you can copy those files in.

## Assets

- **Zip** — drop-in plugin folder (recommended)
- **DLL** — same binary if you already have the folder
