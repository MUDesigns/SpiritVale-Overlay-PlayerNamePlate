using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Api.Protocol;

namespace SpiritVale.Overlay.PlayerNameplate;

/// <summary>
/// Overlay port of the BepInEx SpiritVale Player Nameplate (WoW-style personal resource plate).
/// Vitals come from HealthComponent / SkillsComponent sync; auras from StatusComponent displays;
/// casts from CastBegin_C.
/// </summary>
public sealed class PlayerNameplatePlugin : ISpiritValePlugin
{
    private const float ConfigW = 400f;
    private const float ConfigH = 560f;

    private static readonly (float R, float G, float B, float A) Cream = OverlayHudColors.Text;
    private static readonly (float R, float G, float B, float A) Muted = OverlayHudColors.Muted;
    private static readonly (float R, float G, float B, float A) Accent = OverlayHudColors.Gold;
    private static readonly (float R, float G, float B, float A) ButtonBg = OverlayHudColors.ButtonBg;
    private static readonly (float R, float G, float B, float A) ButtonHi = OverlayHudColors.ButtonHi;
    private static readonly (float R, float G, float B, float A) Good = OverlayHudColors.Ok;
    private static readonly (float R, float G, float B, float A) Danger = OverlayHudColors.Danger;
    private static readonly (float R, float G, float B, float A) TabIdle = OverlayHudColors.Bg1;
    private static readonly (float R, float G, float B, float A) TabActive = OverlayHudColors.Orange;

    private ISpiritValeApi? _api;
    private readonly CharacterSession _session = new();
    private readonly PlayerTracker _tracker = new();
    private readonly List<AuraInfo> _buffs = new(16);
    private readonly List<AuraInfo> _debuffs = new(16);

    private bool _configOpen;
    private bool _plateOpen = true;
    private bool _snapPlate = true;
    private bool _snapConfig = true;
    private readonly Dictionary<PlatePart, bool> _snapPart = new();
    private string _toggleHotkey = "Ctrl+F2";
    private int _tab;
    private bool _wasInCombat;
    private long _combatEndedAt = -999_000;
    private float _fadeAlpha = 1f;

    public string Id => "local.spiritvale.playernameplate";
    public string Name => "SpiritVale Player Nameplate";
    public string Author => "MUDesigns";
    public string Version => "1.4.0";

    public IReadOnlyList<PluginOptionDefinition> OptionDefinitions { get; } =
    [
        new("toggleUi", "Toggle config panel", PluginOptionKind.Hotkey,
            "Show/hide the nameplate config (original mod used F2; overlay F2 is the manager, so the default is Ctrl+F2).",
            "Ctrl+F2"),
    ];

    public void OnLoad(ISpiritValeApi api)
    {
        _api = api;
        _session.SetChangedCallback(() =>
        {
            _snapPlate = true;
            _snapConfig = true;
            _fadeAlpha = 1f;
            _wasInCombat = false;
            if (!_session.IsReady)
                _configOpen = false;
        });
        api.Protocol.Packet += OnPacket;
        api.Combat.Damage += OnDamage;
        api.Combat.Heal += OnHeal;
        api.Combat.Death += OnDeath;
        api.Character.CharacterChanged += OnCharacterChanged;
        OnCharacterChanged();
    }

    public void OnUnload()
    {
        if (_api is not null)
        {
            _api.Protocol.Packet -= OnPacket;
            _api.Combat.Damage -= OnDamage;
            _api.Combat.Heal -= OnHeal;
            _api.Combat.Death -= OnDeath;
            _api.Character.CharacterChanged -= OnCharacterChanged;
        }
        _session.SaveNow();
        _session.Close();
        _api = null;
    }

    public void ApplyOptions(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("toggleUi", out var hk) && !string.IsNullOrWhiteSpace(hk))
            _toggleHotkey = hk.Trim();
    }

    public IReadOnlyDictionary<string, string> ExportOptions()
        => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["toggleUi"] = _toggleHotkey,
        };

    public void OnOptionHotkey(string key)
    {
        if (key != "toggleUi") return;
        if (!_session.IsReady) return;
        _configOpen = !_configOpen;
        if (_configOpen) _snapConfig = true;
    }

    public void Draw(IOverlayUi ui)
    {
        if (_api is null) return;

        var local = _api.Character.Local;
        _session.Tick(local?.CharacterId ?? local?.DisplayName, local?.DisplayName);
        _tracker.SetLocal(local?.ActorId, local?.DisplayName, local?.Level, local?.ClassName);
        _tracker.Tick();

        if (!_session.IsReady || _session.Active is null)
            return;

        var p = _session.Active;
        if (!_tracker.TrySnapshot(out var snap))
            return;

        snap = EnrichFromHost(snap, local);

        UpdateCombatFade(p, snap);
        if (_configOpen)
            DrawConfig(ui, p, snap);

        if (!p.ShowFrame && !_configOpen)
            return;
        if (!ShouldShow(p, snap) && _fadeAlpha <= 0.02f)
            return;

        DrawPlate(ui, p, snap);
    }

    private PlayerSnapshot EnrichFromHost(PlayerSnapshot snap, Api.Character.CharacterSnapshot? local)
    {
        var name = snap.Name;
        var level = snap.Level;
        var job = snap.JobLevel;
        var className = snap.ClassName;
        var arch = snap.ArchetypeId;
        var hp = snap.Health;
        var hpMax = snap.MaxHealth;
        var hpNorm = snap.HealthNorm;
        var mp = snap.Mana;
        var mpMax = snap.MaxMana;
        var mpNorm = snap.ManaNorm;
        var alive = snap.IsAlive;
        var hasVitals = snap.HasVitals;
        var hasHealth = snap.HasHealth;
        var hasMana = snap.HasMana;

        if (local is not null)
        {
            if (!string.IsNullOrWhiteSpace(local.DisplayName)
                && local.DisplayName is not "You" and not "Local Player")
                name = local.DisplayName;
            if (local.Level is int lv && lv > 0) level = lv;
            if (!string.IsNullOrWhiteSpace(local.ClassName)) className = local.ClassName;
            if (local.Stats is { Count: > 0 } stats)
            {
                if (level <= 1 && stats.TryGetValue("level", out var sl) && sl > 1)
                    level = (int)sl;
                if (job <= 1 && stats.TryGetValue("jobLevel", out var sj) && sj > 1)
                    job = (int)sj;
            }
        }

        if (_api?.Combat.Leaderboard is { Count: > 0 } board)
        {
            var row = board.FirstOrDefault(r =>
                string.Equals(r.DisplayName, name, StringComparison.OrdinalIgnoreCase)
                || (local?.ActorId is int id && r.ActorId == id));
            if (row is not null)
            {
                arch ??= row.ArchetypeId;
                if (string.IsNullOrWhiteSpace(className)) className = row.ClassName ?? "";
                if (row.Level is int lv && lv > 0) level = lv;
            }
        }

        if (!hasHealth && _api is not null)
        {
            var self = _api.Party.Members.FirstOrDefault(m => m.IsSelf)
                       ?? _api.Party.Members.FirstOrDefault(m =>
                           string.Equals(m.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            if (self is not null && self.MaxHealth > 1)
            {
                hp = (int)self.Health;
                hpMax = Math.Max(1, (int)self.MaxHealth);
                hpNorm = hpMax > 0 ? Math.Clamp(hp / (float)hpMax, 0f, 1f) : 0f;
                alive = hp > 0;
                hasHealth = true;
                hasVitals = true;
            }
            if (self is not null && !hasMana && self.MaxMana > 1)
            {
                mp = (int)self.Mana;
                mpMax = Math.Max(1, (int)self.MaxMana);
                mpNorm = mpMax > 0 ? Math.Clamp(mp / (float)mpMax, 0f, 1f) : 0f;
                hasMana = true;
                hasVitals = true;
            }
        }

        if (hasMana && mpMax <= 1 && mp > 1)
        {
            mpMax = mp;
            mpNorm = 1f;
        }
        if (hasHealth && hpMax <= 1 && hp > 1)
        {
            hpMax = hp;
            hpNorm = 1f;
        }

        var castName = snap.CastName;
        if (snap.IsCasting && _api is not null && !string.IsNullOrWhiteSpace(castName))
        {
            var (display, _) = _api.Sprites.ResolveSkill(castName);
            if (!string.IsNullOrWhiteSpace(display) && display != "Unknown")
                castName = display;
        }

        return new PlayerSnapshot(
            name, hp, hpMax, hpNorm, snap.Barrier, snap.ShieldNorm,
            mp, mpMax, mpNorm, alive, snap.IsCasting, snap.CastNorm, castName,
            snap.InCombat, level, job, arch, className, hasVitals, hasHealth, hasMana);
    }

    private void UpdateCombatFade(CharacterProfile profile, PlayerSnapshot snap)
    {
        var engaged = snap.InCombat || snap.IsCasting
                      || _api?.Combat.CurrentEncounter is not null;
        var now = Environment.TickCount64;
        if (engaged)
        {
            _wasInCombat = true;
            _fadeAlpha = 1f;
        }
        else if (_wasInCombat)
        {
            _wasInCombat = false;
            _combatEndedAt = now;
        }

        var fadeSec = profile.CombatFadeSeconds;
        var combatOnly = profile.GetVisibilityMode() == VisibilityMode.CombatOnly;
        if (engaged || _configOpen || !combatOnly)
            _fadeAlpha = 1f;
        else if (fadeSec <= 0.01f)
            _fadeAlpha = 0f;
        else
        {
            var since = (now - _combatEndedAt) / 1000f;
            _fadeAlpha = since >= fadeSec ? 0f : Math.Clamp(1f - (since / fadeSec), 0f, 1f);
        }
    }

    private bool ShouldShow(CharacterProfile profile, PlayerSnapshot snap)
    {
        if (_configOpen) return true;
        if (profile.GetVisibilityMode() != VisibilityMode.CombatOnly) return true;
        if (snap.InCombat || snap.IsCasting) return true;
        return _fadeAlpha > 0.02f;
    }

    private void DrawPlate(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap)
    {
        var scale = Math.Clamp(p.Scale, 0.5f, 2.5f);
        var width = p.Width * scale;
        var barH = p.BarHeight * scale;
        var gap = p.BarGap * scale;
        var auraSize = p.AuraIconSize * scale;
        var auraGap = p.AuraGap * scale;
        var pad = 6f * scale;
        var nameH = p.ShowName || p.ShowLevel ? (p.NameFontSize + 6f) * scale : 0f;

        var stackH = pad * 2f;
        if (p.ShowName || p.ShowLevel) stackH += nameH;
        stackH += barH * 2f + gap;
        if (p.ShowCastBar) stackH += gap + barH;
        if (p.ShowBuffs || p.ShowDebuffs) stackH += auraGap;
        if (p.ShowBuffs) stackH += auraSize + 2f;
        if (p.ShowDebuffs) stackH += auraSize + 2f;
        if (stackH < 40f) stackH = 40f;

        var needAuras = p.ShowBuffs || p.ShowDebuffs;
        if (needAuras)
            _tracker.CollectAuras(p, _buffs, _debuffs);

        var (dw, dh) = ui.GetDisplaySize();
        if (_snapPlate)
        {
            var (ix, iy) = UnityToImGui(dw, dh, p.FramePosX, p.FramePosY, width, stackH);
            ui.SetNextWindowPos(ix, iy, once: false);
            _snapPlate = false;
        }

        ui.SetNextWindowSize(width, stackH, once: false);
        var bgAlpha = _configOpen ? 0.35f : 0.02f;
        ui.SetNextWindowBgAlpha(bgAlpha * Math.Clamp(p.Opacity, 10f, 100f) / 100f);
        ui.PushWindowPadding(pad, pad);
        ui.PushAlpha(Math.Clamp(p.Opacity, 10f, 100f) / 100f * Math.Clamp(_fadeAlpha, 0.05f, 1f));

        var flags = OverlayWindowFlags.NoTitleBar | OverlayWindowFlags.NoResize | OverlayWindowFlags.NoScrollbar
                    | OverlayWindowFlags.NoCollapse | OverlayWindowFlags.NoSavedSettings;
        if (!_configOpen)
            flags |= OverlayWindowFlags.NoMove | OverlayWindowFlags.NoBackground;
        if (_configOpen) ui.CaptureMouse();

        var y = pad;
        var rowW = width - pad * 2f;
        if (ui.BeginWindow("##svpn-plate", ref _plateOpen, flags))
        {
            if (_configOpen) ui.CaptureMouse();

            if ((p.ShowName || p.ShowLevel) && !p.FreeName)
            {
                ui.SetCursorPos(pad, y);
                DrawName(ui, p, snap, rowW);
                y += nameH;
            }
            else if (p.ShowName || p.ShowLevel)
                y += nameH;

            if (p.HpAboveMana)
            {
                y = DrawStackedBar(ui, p, snap, PlatePart.Hp, pad, y, rowW, barH, gap);
                y = DrawStackedBar(ui, p, snap, PlatePart.Mp, pad, y, rowW, barH, 0f);
            }
            else
            {
                y = DrawStackedBar(ui, p, snap, PlatePart.Mp, pad, y, rowW, barH, gap);
                y = DrawStackedBar(ui, p, snap, PlatePart.Hp, pad, y, rowW, barH, 0f);
            }

            if (p.ShowCastBar)
            {
                y += gap;
                if (!p.FreeCast)
                {
                    ui.SetCursorPos(pad, y);
                    DrawCast(ui, p, snap, rowW, barH);
                }
                y += barH;
            }

            if (p.ShowBuffs)
            {
                y += auraGap;
                if (!p.FreeBuffs)
                {
                    ui.SetCursorPos(pad, y);
                    DrawAuras(ui, p, _buffs, auraSize, rowW);
                }
                y += auraSize + 2f;
            }

            if (p.ShowDebuffs)
            {
                y += p.ShowBuffs ? 2f : auraGap;
                if (!p.FreeDebuffs)
                {
                    ui.SetCursorPos(pad, y);
                    DrawAuras(ui, p, _debuffs, auraSize, rowW);
                }
            }

            var (wx, wy) = ui.GetWindowPos();
            var (ux, uy) = ImGuiToUnity(dw, dh, wx, wy, width, stackH);
            if (Math.Abs(ux - p.FramePosX) > 0.5f || Math.Abs(uy - p.FramePosY) > 0.5f)
            {
                p.FramePosX = ux;
                p.FramePosY = uy;
                _session.MarkDirty();
            }
        }
        ui.EndWindow();
        ui.PopAlpha();
        ui.PopWindowPadding();

        DrawFreePart(ui, p, snap, PlatePart.Name, p.ShowName || p.ShowLevel, rowW, nameH,
            () => DrawName(ui, p, snap, rowW));
        DrawFreePart(ui, p, snap, PlatePart.Hp, true, rowW, barH,
            () => DrawHp(ui, p, snap, rowW, barH));
        DrawFreePart(ui, p, snap, PlatePart.Mp, true, rowW, barH,
            () => DrawMp(ui, p, snap, rowW, barH));
        DrawFreePart(ui, p, snap, PlatePart.Cast, p.ShowCastBar, rowW, barH,
            () => DrawCast(ui, p, snap, rowW, barH));
        DrawFreePart(ui, p, snap, PlatePart.Buffs, p.ShowBuffs, rowW, auraSize,
            () => DrawAuras(ui, p, _buffs, auraSize, rowW));
        DrawFreePart(ui, p, snap, PlatePart.Debuffs, p.ShowDebuffs, rowW, auraSize,
            () => DrawAuras(ui, p, _debuffs, auraSize, rowW));
    }

    private float DrawStackedBar(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap,
        PlatePart part, float pad, float y, float rowW, float barH, float gapAfter)
    {
        var free = p.IsFree(part);
        if (!free)
        {
            ui.SetCursorPos(pad, y);
            if (part == PlatePart.Hp) DrawHp(ui, p, snap, rowW, barH);
            else DrawMp(ui, p, snap, rowW, barH);
        }
        return y + barH + gapAfter;
    }

    private void DrawFreePart(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap,
        PlatePart part, bool visible, float w, float h, Action draw)
    {
        if (!visible || !p.IsFree(part) || h <= 0f) return;
        p.GetOffset(part, out var ox, out var oy);
        var (dw, dh) = ui.GetDisplaySize();
        if (!_snapPart.TryGetValue(part, out var snapOnce) || snapOnce)
        {
            var (ix, iy) = UnityToImGui(dw, dh, p.FramePosX + ox, p.FramePosY + oy, w, h);
            ui.SetNextWindowPos(ix, iy, once: false);
            _snapPart[part] = false;
        }

        ui.SetNextWindowSize(w + 8f, h + 8f, once: false);
        ui.SetNextWindowBgAlpha(_configOpen ? 0.25f : 0f);
        ui.PushWindowPadding(4f, 4f);
        ui.PushAlpha(Math.Clamp(p.Opacity, 10f, 100f) / 100f * Math.Clamp(_fadeAlpha, 0.05f, 1f));
        var flags = OverlayWindowFlags.NoTitleBar | OverlayWindowFlags.NoResize | OverlayWindowFlags.NoScrollbar
                    | OverlayWindowFlags.NoCollapse | OverlayWindowFlags.NoSavedSettings | OverlayWindowFlags.AutoResize;
        if (!_configOpen)
            flags |= OverlayWindowFlags.NoMove | OverlayWindowFlags.NoBackground;
        if (_configOpen) ui.CaptureMouse();

        var open = true;
        if (ui.BeginWindow($"##svpn-{part}", ref open, flags))
        {
            if (_configOpen) ui.CaptureMouse();
            draw();
            var (wx, wy) = ui.GetWindowPos();
            var (ux, uy) = ImGuiToUnity(dw, dh, wx, wy, w, h);
            var relX = ux - p.FramePosX;
            var relY = uy - p.FramePosY;
            if (Math.Abs(relX - ox) > 0.5f || Math.Abs(relY - oy) > 0.5f)
            {
                p.SetOffset(part, relX, relY);
                _session.MarkDirty();
            }
        }
        ui.EndWindow();
        ui.PopAlpha();
        ui.PopWindowPadding();
        _ = snap;
    }

    private static void DrawName(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap, float width)
    {
        _ = width;
        var label = BuildNameLabel(p, snap);
        ui.TextUnformatted(label);
    }

    private static string BuildNameLabel(CharacterProfile p, PlayerSnapshot snap)
    {
        var label = "";
        if (p.ShowName) label = snap.Name ?? "";
        if (p.ShowLevel)
        {
            var lv = "Lv" + snap.Level + " / J" + snap.JobLevel;
            if (!string.IsNullOrEmpty(snap.ClassName))
                lv = snap.ClassName + "  " + lv;
            label = string.IsNullOrEmpty(label) ? lv : label + "  ·  " + lv;
        }
        return label;
    }

    private static void DrawHp(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap, float width, float height)
    {
        _ = width;
        var color = p.UseClassColor
            ? ClassColor(snap.ClassName, snap.ArchetypeId)
            : (R: p.HpR, G: p.HpG, B: p.HpB);
        if (p.LowHpWarning && snap.HasHealth && snap.IsAlive && snap.HealthNorm * 100f <= p.LowHpThreshold)
        {
            var t = Environment.TickCount64 / 1000.0;
            var ping = (float)Math.Abs((t * 3.2) % 2.0 - 1.0);
            var pulse = 0.55f + 0.45f * ping;
            color = Lerp(color, (R: 1f, G: 0.2f, B: 0.15f), pulse);
        }

        string overlay;
        if (!snap.HasHealth)
            overlay = "";
        else if (!snap.IsAlive)
            overlay = "DEAD";
        else
        {
            overlay = PlayerTracker.FormatValue(p.GetTextFormat(), snap.Health, snap.MaxHealth, snap.HealthNorm);
            if (p.ShowBarrier && snap.Barrier > 0)
                overlay = string.IsNullOrEmpty(overlay) ? "+" + snap.Barrier : overlay + "  +" + snap.Barrier;
        }

        ui.ProgressBar(snap.HasHealth && snap.IsAlive ? snap.HealthNorm : 0f, color.R, color.G, color.B, height, overlay);
    }

    private static void DrawMp(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap, float width, float height)
    {
        _ = width;
        var overlay = snap.HasMana
            ? PlayerTracker.FormatValue(p.GetTextFormat(), snap.Mana, snap.MaxMana, snap.ManaNorm)
            : "";
        ui.ProgressBar(snap.HasMana ? snap.ManaNorm : 0f, p.MpR, p.MpG, p.MpB, height, overlay);
    }

    private static void DrawCast(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap, float width, float height)
    {
        _ = width;
        if (!snap.IsCasting)
        {
            ui.Dummy(width, height);
            return;
        }
        var name = string.IsNullOrEmpty(snap.CastName) ? "Casting" : snap.CastName;
        ui.ProgressBar(snap.CastNorm, p.CastR, p.CastG, p.CastB, height, name);
    }

    private void DrawAuras(IOverlayUi ui, CharacterProfile p, List<AuraInfo> auras, float size, float width)
    {
        _ = width;
        var count = Math.Min(auras.Count, p.AuraMaxIcons);
        if (count == 0)
        {
            ui.Dummy(size, size);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            if (i > 0) ui.SameLine(2f);
            var aura = auras[i];
            var sprite = aura.SpriteId;
            if (string.IsNullOrWhiteSpace(sprite) && _api is not null)
            {
                var (_, sid) = _api.Sprites.ResolveSkill(aura.Id);
                sprite = sid;
            }
            string? text = null;
            if (p.AuraShowDuration && !aura.Infinite && aura.Duration > 0f)
                text = FormatDuration(aura.Duration);
            if (p.AuraShowStacks && aura.Stacks > 1)
                text = string.IsNullOrEmpty(text) ? aura.Stacks.ToString() : text + " x" + aura.Stacks;
            ui.CooldownSlot(sprite, size, 0f, text);
        }
    }

    private static string FormatDuration(float seconds)
    {
        if (seconds >= 60f) return ((int)(seconds / 60f)) + "m";
        if (seconds >= 10f) return ((int)seconds).ToString();
        var tenths = (int)(seconds * 10f);
        return (tenths / 10) + "." + (tenths % 10);
    }

    private void DrawConfig(IOverlayUi ui, CharacterProfile p, PlayerSnapshot snap)
    {
        ui.CaptureMouse();
        var (dw, dh) = ui.GetDisplaySize();
        if (_snapConfig)
        {
            var (ix, iy) = UnityToImGui(dw, dh, p.WindowPosX, p.WindowPosY, ConfigW, ConfigH);
            ui.SetNextWindowPos(ix, iy, once: false);
            _snapConfig = false;
        }

        ui.SetNextWindowSize(ConfigW, ConfigH, once: false);
        ui.SetNextWindowBgAlpha(0.97f);
        var flags = OverlayWindowFlags.NoResize | OverlayWindowFlags.NoCollapse | OverlayWindowFlags.NoSavedSettings;
        if (!ui.BeginWindow("Player Nameplate", ref _configOpen, flags))
        {
            ui.EndWindow();
            return;
        }

        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "drag title · drag plate parts while open");
        ui.Spacing();

        var tabW = (ui.GetContentWidth() - 12f) / 4f;
        TabButton(ui, "General", 0, tabW);
        ui.SameLine(4f);
        TabButton(ui, "Layout", 1, tabW);
        ui.SameLine(4f);
        TabButton(ui, "Auras", 2, tabW);
        ui.SameLine(4f);
        TabButton(ui, "Combat", 3, tabW);
        ui.Spacing();

        ui.PushStyleColor(OverlayCol.ChildBg, 0.08f, 0.09f, 0.11f, 1f);
        if (ui.BeginWindowChild("##svpn-tab", 0f, 400f))
        {
            switch (_tab)
            {
                case 1: DrawLayoutTab(ui, p); break;
                case 2: DrawAurasTab(ui, p); break;
                case 3: DrawCombatTab(ui, p); break;
                default: DrawGeneralTab(ui, p); break;
            }
        }
        ui.EndWindowChild();
        ui.PopStyleColor();

        ui.Spacing();
        var name = string.IsNullOrEmpty(_session.ActiveName) ? _session.ActiveId : _session.ActiveName;
        var vitals = snap.HasVitals ? "vitals" : "waiting for HP sync";
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, $"{name} · {_toggleHotkey} close · {vitals}");
        StyledButton(ui, "Close", 100f, 28f, Accent, () => _configOpen = false);

        var (wx, wy) = ui.GetWindowPos();
        var (ux, uy) = ImGuiToUnity(dw, dh, wx, wy, ConfigW, ConfigH);
        if (Math.Abs(ux - p.WindowPosX) > 0.5f || Math.Abs(uy - p.WindowPosY) > 0.5f)
        {
            p.WindowPosX = ux;
            p.WindowPosY = uy;
            _session.MarkDirty();
        }

        ui.EndWindow();
    }

    private void DrawGeneralTab(IOverlayUi ui, CharacterProfile p)
    {
        var full = ui.GetContentWidth();
        var half = (full - 8f) * 0.5f;
        var q = (full - 18f) * 0.25f;
        var third = (full - 16f) / 3f;

        StyledButton(ui, p.ShowFrame ? "Frame ON" : "Frame OFF", full, 28f,
            p.ShowFrame ? Good : Danger, () => { p.ShowFrame = !p.ShowFrame; Touch(); });

        ui.Spacing();
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "Visibility");
        StyledButton(ui, "Always", half, 28f,
            p.GetVisibilityMode() == VisibilityMode.Always ? Good : ButtonBg,
            () => { p.VisibilityMode = (int)VisibilityMode.Always; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Combat only", half, 28f,
            p.GetVisibilityMode() == VisibilityMode.CombatOnly ? Good : ButtonBg,
            () => { p.VisibilityMode = (int)VisibilityMode.CombatOnly; Touch(); });

        ui.Spacing();
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "HP / MP text");
        FmtButton(ui, "%", ValueTextFormat.Percent, q, p);
        ui.SameLine(6f);
        FmtButton(ui, "Cur", ValueTextFormat.Current, q, p);
        ui.SameLine(6f);
        FmtButton(ui, "Cur/Max", ValueTextFormat.CurrentMax, q, p);
        ui.SameLine(6f);
        FmtButton(ui, "Both", ValueTextFormat.CurrentMaxPercent, q, p);

        ui.Spacing();
        StyledButton(ui, "Toggle name", half, 28f, ButtonBg, () => { p.ShowName = !p.ShowName; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Toggle cast bar", half, 28f, ButtonBg, () => { p.ShowCastBar = !p.ShowCastBar; Touch(); });
        StyledButton(ui, "Swap HP / Mana order", full, 28f, ButtonBg, () => { p.HpAboveMana = !p.HpAboveMana; Touch(); });

        ui.Spacing();
        StyledButton(ui, "Barrier", third, 28f, p.ShowBarrier ? Good : ButtonBg,
            () => { p.ShowBarrier = !p.ShowBarrier; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Class color", third, 28f, p.UseClassColor ? Good : ButtonBg,
            () => { p.UseClassColor = !p.UseClassColor; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Level", third, 28f, p.ShowLevel ? Good : ButtonBg,
            () => { p.ShowLevel = !p.ShowLevel; Touch(); });
        StyledButton(ui, "Low HP warn", full, 28f, p.LowHpWarning ? Good : ButtonBg,
            () => { p.LowHpWarning = !p.LowHpWarning; Touch(); });
    }

    private void DrawLayoutTab(IOverlayUi ui, CharacterProfile p)
    {
        var full = ui.GetContentWidth();
        var third = (full - 16f) / 3f;
        var scalePct = p.Scale * 100f;

        Label(ui, $"Scale {MathF.Round(scalePct):0}%");
        if (ui.SliderFloat("##np-scale", ref scalePct, 50f, 250f))
        {
            p.Scale = scalePct / 100f;
            Touch();
        }

        Label(ui, $"Width {MathF.Round(p.Width):0}");
        if (ui.SliderFloat("##np-width", ref p.Width, 120f, 480f)) Touch();

        Label(ui, $"Bar height {MathF.Round(p.BarHeight):0}");
        if (ui.SliderFloat("##np-height", ref p.BarHeight, 10f, 40f)) Touch();

        Label(ui, $"Bar gap {MathF.Round(p.BarGap):0}");
        if (ui.SliderFloat("##np-gap", ref p.BarGap, 0f, 20f)) Touch();

        Label(ui, $"Opacity {MathF.Round(p.Opacity):0}%");
        if (ui.SliderFloat("##np-opacity", ref p.Opacity, 10f, 100f)) Touch();

        ui.Spacing();
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "Free move — unlocks drag; part stays put");
        FreeButton(ui, "Name", PlatePart.Name, p, third);
        ui.SameLine(8f);
        FreeButton(ui, "HP", PlatePart.Hp, p, third);
        ui.SameLine(8f);
        FreeButton(ui, "Mana", PlatePart.Mp, p, third);
        FreeButton(ui, "Cast", PlatePart.Cast, p, third);
        ui.SameLine(8f);
        FreeButton(ui, "Buffs", PlatePart.Buffs, p, third);
        ui.SameLine(8f);
        FreeButton(ui, "Debuffs", PlatePart.Debuffs, p, third);
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "Snap again to rejoin the stack");
    }

    private void DrawAurasTab(IOverlayUi ui, CharacterProfile p)
    {
        var full = ui.GetContentWidth();
        var half = (full - 8f) * 0.5f;
        var third = (full - 16f) / 3f;

        StyledButton(ui, "Buffs", half, 28f, p.ShowBuffs ? Good : ButtonBg,
            () => { p.ShowBuffs = !p.ShowBuffs; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Debuffs", half, 28f, p.ShowDebuffs ? Good : ButtonBg,
            () => { p.ShowDebuffs = !p.ShowDebuffs; Touch(); });

        ui.Spacing();
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A, "Aura filter");
        var filt = p.GetAuraFilter();
        StyledButton(ui, "All", third, 28f, filt == AuraFilterMode.All ? Good : ButtonBg,
            () => { p.AuraFilter = (int)AuraFilterMode.All; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Skills", third, 28f, filt == AuraFilterMode.SkillsOnly ? Good : ButtonBg,
            () => { p.AuraFilter = (int)AuraFilterMode.SkillsOnly; Touch(); });
        ui.SameLine(8f);
        StyledButton(ui, "Status", third, 28f, filt == AuraFilterMode.StatusOnly ? Good : ButtonBg,
            () => { p.AuraFilter = (int)AuraFilterMode.StatusOnly; Touch(); });

        ui.Spacing();
        Label(ui, $"Buff / debuff icon size {MathF.Round(p.AuraIconSize):0}");
        if (ui.SliderFloat("##np-aurasize", ref p.AuraIconSize, 14f, 48f)) Touch();

        Label(ui, $"Aura distance from bars {MathF.Round(p.AuraGap):0}");
        if (ui.SliderFloat("##np-auragap", ref p.AuraGap, 0f, 60f)) Touch();
    }

    private void DrawCombatTab(IOverlayUi ui, CharacterProfile p)
    {
        Label(ui, $"Combat fade {p.CombatFadeSeconds:0.0}s");
        if (ui.SliderFloat("##np-fade", ref p.CombatFadeSeconds, 0f, 10f)) Touch();

        Label(ui, $"Low HP at {MathF.Round(p.LowHpThreshold):0}%");
        if (ui.SliderFloat("##np-lowhp", ref p.LowHpThreshold, 5f, 50f)) Touch();

        ui.Spacing();
        ui.TextColored(Muted.R, Muted.G, Muted.B, Muted.A,
            "Combat-only uses your hits, incoming damage, casting, and the overlay encounter timer.");
    }

    private void TabButton(IOverlayUi ui, string label, int id, float w)
        => StyledButton(ui, label, w, 28f, _tab == id ? TabActive : TabIdle, () => _tab = id);

    private void FmtButton(IOverlayUi ui, string label, ValueTextFormat fmt, float w, CharacterProfile p)
        => StyledButton(ui, label, w, 28f, p.GetTextFormat() == fmt ? Good : ButtonBg, () =>
        {
            p.TextFormat = (int)fmt;
            Touch();
        });

    private void FreeButton(IOverlayUi ui, string label, PlatePart part, CharacterProfile p, float w)
        => StyledButton(ui, label, w, 28f, p.IsFree(part) ? Good : ButtonBg, () =>
        {
            var next = !p.IsFree(part);
            if (next)
            {
                // Seed from current frame so unlock does not jump to 0,0.
                p.GetOffset(part, out var x, out var y);
                if (Math.Abs(x) < 0.01f && Math.Abs(y) < 0.01f)
                    p.SetOffset(part, 0f, 0f);
                _snapPart[part] = true;
            }
            p.SetFree(part, next);
            Touch();
        });

    private void Touch()
    {
        _session.MarkDirty();
        _snapPlate = true;
    }

    private void OnPacket(DecodedFishNetEvent evt) => _tracker.OnPacket(evt);
    private void OnDamage(CombatDamageEvent evt) => _tracker.OnDamage(evt);
    private void OnHeal(CombatHealEvent evt) => _tracker.OnHeal(evt);
    private void OnDeath(CombatDeathEvent evt) => _tracker.OnDeath(evt);

    private void OnCharacterChanged()
    {
        var local = _api?.Character.Local;
        _tracker.SetLocal(local?.ActorId, local?.DisplayName, local?.Level, local?.ClassName);
    }

    private static void Label(IOverlayUi ui, string text)
        => ui.TextColored(Cream.R, Cream.G, Cream.B, Cream.A, text);

    private static void StyledButton(IOverlayUi ui, string label, float w, float h,
        (float R, float G, float B, float A) bg, Action onClick)
    {
        ui.PushStyleColor(OverlayCol.Button, bg.R, bg.G, bg.B, bg.A);
        ui.PushStyleColor(OverlayCol.ButtonHovered, ButtonHi.R, ButtonHi.G, ButtonHi.B, ButtonHi.A);
        ui.PushStyleColor(OverlayCol.ButtonActive, Accent.R, Accent.G, Accent.B, Accent.A);
        ui.PushStyleColor(OverlayCol.Text, Cream.R, Cream.G, Cream.B, Cream.A);
        if (ui.Button(label, w, h)) onClick();
        ui.PopStyleColor(4);
    }

    private static (float R, float G, float B) ClassColor(string className, int? archetypeId)
    {
        var key = className ?? "";
        if (string.IsNullOrWhiteSpace(key) && archetypeId is int id)
            key = id.ToString();

        if (ContainsAny(key, "Warrior", "Knight", "Paladin", "Dragon", "Berserker", "Revenant", "Blade"))
            return (0.78f, 0.28f, 0.22f);
        if (ContainsAny(key, "Mage", "Wizard", "Chronomancer", "Warlock", "Necromancer", "Spellblade", "Summoner"))
            return (0.35f, 0.45f, 0.92f);
        if (ContainsAny(key, "Rogue", "Assassin", "Shinobi", "Nightshade", "Jester"))
            return (0.62f, 0.28f, 0.78f);
        if (ContainsAny(key, "Acolyte", "Priest", "Monk", "Druid"))
            return (0.92f, 0.82f, 0.35f);
        if (ContainsAny(key, "Scout", "Ranger", "Gunslinger"))
            return (0.32f, 0.72f, 0.38f);
        if (ContainsAny(key, "Mechanist", "Alchemist", "Weaver", "Merchant", "Blacksmith", "Craftsman"))
            return (0.72f, 0.55f, 0.32f);
        return (0.75f, 0.18f, 0.18f);
    }

    private static bool ContainsAny(string hay, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (hay.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t)
        => (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static (float X, float Y) UnityToImGui(float dw, float dh, float ux, float uy, float w, float h)
        => (dw * 0.5f + ux - w * 0.5f, dh * 0.5f - uy - h * 0.5f);

    private static (float X, float Y) ImGuiToUnity(float dw, float dh, float ix, float iy, float w, float h)
        => (ix + w * 0.5f - dw * 0.5f, dh * 0.5f - iy - h * 0.5f);
}
