using System.Globalization;
using System.Text;

namespace SpiritVale.Overlay.PlayerNameplate;

internal enum ValueTextFormat
{
    Percent = 0,
    Current = 1,
    CurrentMax = 2,
    CurrentMaxPercent = 3,
    Hidden = 4,
}

internal enum VisibilityMode
{
    Always = 0,
    CombatOnly = 1,
}

internal enum AuraFilterMode
{
    All = 0,
    SkillsOnly = 1,
    StatusOnly = 2,
}

internal enum PlatePart
{
    None = 0,
    Frame = 1,
    Name = 2,
    Hp = 3,
    Mp = 4,
    Cast = 5,
    Buffs = 6,
    Debuffs = 7,
}

internal sealed class CharacterProfile
{
    public string CharacterId = "";
    public string CharacterName = "";

    public bool ShowFrame = true;
    public int VisibilityMode;
    public bool ShowName = true;
    public bool ShowCastBar = true;
    public bool HpAboveMana = true;
    public int TextFormat = (int)ValueTextFormat.CurrentMax;

    public float FramePosX;
    public float FramePosY = -280f;
    public float Scale = 1f;
    public float Width = 220f;
    public float BarHeight = 18f;
    public float BarGap = 3f;
    public float Opacity = 100f;
    public float NameFontSize = 14f;
    public float ValueFontSize = 12f;

    public float HpR = 0.75f, HpG = 0.18f, HpB = 0.18f;
    public float MpR = 0.22f, MpG = 0.42f, MpB = 0.85f;
    public float CastR = 0.90f, CastG = 0.75f, CastB = 0.20f;

    public bool ShowBarrier = true;
    public bool UseClassColor = true;
    public bool ShowLevel = true;
    public bool ShowBuffs = true;
    public bool ShowDebuffs = true;
    public int AuraFilter;
    public float AuraIconSize = 22f;
    public float AuraGap = 6f;
    public int AuraMaxIcons = 10;
    public bool AuraShowDuration = true;
    public bool AuraShowStacks = true;
    public bool LowHpWarning = true;
    public float LowHpThreshold = 25f;
    public float CombatFadeSeconds = 3f;
    public string AuraWhitelist = "";
    public string AuraBlacklist = "";

    public bool FreeName;
    public bool FreeHp;
    public bool FreeMp;
    public bool FreeCast;
    public bool FreeBuffs;
    public bool FreeDebuffs;
    public float NameOffX, NameOffY;
    public float HpOffX, HpOffY;
    public float MpOffX, MpOffY;
    public float CastOffX, CastOffY;
    public float BuffsOffX, BuffsOffY;
    public float DebuffsOffX, DebuffsOffY;

    public float WindowPosX = 480f;
    public float WindowPosY = 40f;

    public readonly HashSet<string> AuraWhitelistSet = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> AuraBlacklistSet = new(StringComparer.OrdinalIgnoreCase);
    public bool HasAuraWhitelist;
    public bool HasAuraBlacklist;

    public VisibilityMode GetVisibilityMode()
        => VisibilityMode == (int)PlayerNameplate.VisibilityMode.CombatOnly
            ? PlayerNameplate.VisibilityMode.CombatOnly
            : PlayerNameplate.VisibilityMode.Always;

    public ValueTextFormat GetTextFormat()
    {
        var v = TextFormat;
        if (v is < 0 or > 4) return ValueTextFormat.CurrentMax;
        return (ValueTextFormat)v;
    }

    public AuraFilterMode GetAuraFilter()
    {
        var v = AuraFilter;
        if (v is < 0 or > 2) return AuraFilterMode.All;
        return (AuraFilterMode)v;
    }

    public bool IsFree(PlatePart part) => part switch
    {
        PlatePart.Name => FreeName,
        PlatePart.Hp => FreeHp,
        PlatePart.Mp => FreeMp,
        PlatePart.Cast => FreeCast,
        PlatePart.Buffs => FreeBuffs,
        PlatePart.Debuffs => FreeDebuffs,
        _ => false,
    };

    public void SetFree(PlatePart part, bool free)
    {
        switch (part)
        {
            case PlatePart.Name: FreeName = free; break;
            case PlatePart.Hp: FreeHp = free; break;
            case PlatePart.Mp: FreeMp = free; break;
            case PlatePart.Cast: FreeCast = free; break;
            case PlatePart.Buffs: FreeBuffs = free; break;
            case PlatePart.Debuffs: FreeDebuffs = free; break;
        }
    }

    public void GetOffset(PlatePart part, out float x, out float y)
    {
        x = 0f; y = 0f;
        switch (part)
        {
            case PlatePart.Name: x = NameOffX; y = NameOffY; break;
            case PlatePart.Hp: x = HpOffX; y = HpOffY; break;
            case PlatePart.Mp: x = MpOffX; y = MpOffY; break;
            case PlatePart.Cast: x = CastOffX; y = CastOffY; break;
            case PlatePart.Buffs: x = BuffsOffX; y = BuffsOffY; break;
            case PlatePart.Debuffs: x = DebuffsOffX; y = DebuffsOffY; break;
        }
    }

    public void SetOffset(PlatePart part, float x, float y)
    {
        switch (part)
        {
            case PlatePart.Name: NameOffX = x; NameOffY = y; break;
            case PlatePart.Hp: HpOffX = x; HpOffY = y; break;
            case PlatePart.Mp: MpOffX = x; MpOffY = y; break;
            case PlatePart.Cast: CastOffX = x; CastOffY = y; break;
            case PlatePart.Buffs: BuffsOffX = x; BuffsOffY = y; break;
            case PlatePart.Debuffs: DebuffsOffX = x; DebuffsOffY = y; break;
        }
    }

    public void Clamp()
    {
        Scale = Math.Clamp(Scale, 0.5f, 2.5f);
        Width = Math.Clamp(Width, 120f, 480f);
        BarHeight = Math.Clamp(BarHeight, 10f, 40f);
        BarGap = Math.Clamp(BarGap, 0f, 20f);
        Opacity = Math.Clamp(Opacity, 10f, 100f);
        NameFontSize = Math.Clamp(NameFontSize, 10f, 28f);
        ValueFontSize = Math.Clamp(ValueFontSize, 9f, 22f);
        VisibilityMode = Math.Clamp(VisibilityMode, 0, 1);
        TextFormat = Math.Clamp(TextFormat, 0, 4);
        AuraFilter = Math.Clamp(AuraFilter, 0, 2);
        AuraIconSize = Math.Clamp(AuraIconSize, 14f, 48f);
        AuraGap = Math.Clamp(AuraGap, 0f, 80f);
        AuraMaxIcons = Math.Clamp(AuraMaxIcons, 1, 24);
        LowHpThreshold = Math.Clamp(LowHpThreshold, 5f, 50f);
        CombatFadeSeconds = Math.Clamp(CombatFadeSeconds, 0f, 15f);
        RebuildAuraSets();
    }

    public void RebuildAuraSets()
    {
        ParseIdList(AuraWhitelist, AuraWhitelistSet);
        ParseIdList(AuraBlacklist, AuraBlacklistSet);
        HasAuraWhitelist = AuraWhitelistSet.Count > 0;
        HasAuraBlacklist = AuraBlacklistSet.Count > 0;
    }

    private static void ParseIdList(string raw, HashSet<string> set)
    {
        set.Clear();
        if (string.IsNullOrWhiteSpace(raw)) return;
        foreach (var part in raw.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var s = part.Trim();
            if (s.Length > 0) set.Add(s);
        }
    }

    public static CharacterProfile CreateDefaults(string id, string name)
    {
        var p = new CharacterProfile { CharacterId = id ?? "", CharacterName = name ?? "" };
        p.Clamp();
        return p;
    }
}

internal sealed class CharacterSession
{
    private CharacterProfile? _active;
    private string _activeId = "";
    private bool _dirty;
    private long _nextSaveAt;
    private Action? _onChanged;

    public bool IsReady => _active is not null && !string.IsNullOrEmpty(_activeId);
    public CharacterProfile? Active => _active;
    public string ActiveId => _activeId;
    public string ActiveName => _active?.CharacterName ?? "";

    public static string ProfilesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpiritValeOverlay", "player-nameplate", "characters");

    public void SetChangedCallback(Action? onChanged) => _onChanged = onChanged;

    public void Tick(string? characterId, string? characterName)
    {
        FlushIfDue();
        var id = characterId;
        var name = characterName ?? "";
        if (string.IsNullOrWhiteSpace(id)) id = name;
        if (string.IsNullOrWhiteSpace(id) || id is "You" or "Local Player")
        {
            ClearActive();
            return;
        }

        if (!string.Equals(_activeId, id, StringComparison.Ordinal))
            SwitchTo(id, name);
        else if (_active is not null && !string.IsNullOrEmpty(name)
                 && !string.Equals(_active.CharacterName, name, StringComparison.Ordinal))
        {
            _active.CharacterName = name;
            MarkDirty();
        }
    }

    public void MarkDirty()
    {
        if (_active is null) return;
        _active.Clamp();
        _dirty = true;
        _nextSaveAt = Environment.TickCount64 + 350;
    }

    public void SaveNow()
    {
        if (_active is null || string.IsNullOrEmpty(_activeId)) return;
        try
        {
            _active.Clamp();
            Directory.CreateDirectory(ProfilesDirectory);
            File.WriteAllText(ProfilePath(_activeId), Serialize(_active), Encoding.UTF8);
            _dirty = false;
        }
        catch { /* retry next flush */ }
    }

    public void Close()
    {
        SaveNow();
        _active = null;
        _activeId = "";
        _dirty = false;
    }

    private void FlushIfDue()
    {
        if (!_dirty) return;
        if (Environment.TickCount64 < _nextSaveAt) return;
        SaveNow();
    }

    private void SwitchTo(string id, string name)
    {
        if (IsReady) SaveNow();
        _active = LoadOrCreate(id, name);
        _activeId = id;
        _dirty = false;
        _onChanged?.Invoke();
    }

    private void ClearActive()
    {
        if (!IsReady) return;
        SaveNow();
        _active = null;
        _activeId = "";
        _dirty = false;
        _onChanged?.Invoke();
    }

    private static CharacterProfile LoadOrCreate(string id, string name)
    {
        var path = ProfilePath(id);
        if (File.Exists(path))
        {
            try
            {
                var p = Deserialize(File.ReadAllText(path, Encoding.UTF8));
                if (p is not null)
                {
                    p.CharacterId = id;
                    if (!string.IsNullOrEmpty(name)) p.CharacterName = name;
                    p.Clamp();
                    return p;
                }
            }
            catch { /* defaults */ }
        }

        var created = CharacterProfile.CreateDefaults(id, name);
        try
        {
            Directory.CreateDirectory(ProfilesDirectory);
            File.WriteAllText(ProfilePath(id), Serialize(created), Encoding.UTF8);
        }
        catch { /* in-memory still works */ }
        return created;
    }

    private static string ProfilePath(string id)
        => Path.Combine(ProfilesDirectory, SanitizeFileName(id) + ".json");

    private static string SanitizeFileName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var s = sb.ToString().Trim();
        return string.IsNullOrEmpty(s) ? "unknown" : s;
    }

    private static string Serialize(CharacterProfile p)
    {
        var sb = new StringBuilder(1600);
        sb.Append("{\n");
        Append(sb, "characterId", p.CharacterId, true);
        Append(sb, "characterName", p.CharacterName, true);
        Append(sb, "showFrame", p.ShowFrame);
        Append(sb, "visibilityMode", p.VisibilityMode);
        Append(sb, "showName", p.ShowName);
        Append(sb, "showCastBar", p.ShowCastBar);
        Append(sb, "hpAboveMana", p.HpAboveMana);
        Append(sb, "textFormat", p.TextFormat);
        Append(sb, "framePosX", p.FramePosX);
        Append(sb, "framePosY", p.FramePosY);
        Append(sb, "scale", p.Scale);
        Append(sb, "width", p.Width);
        Append(sb, "barHeight", p.BarHeight);
        Append(sb, "barGap", p.BarGap);
        Append(sb, "opacity", p.Opacity);
        Append(sb, "nameFontSize", p.NameFontSize);
        Append(sb, "valueFontSize", p.ValueFontSize);
        Append(sb, "hpR", p.HpR); Append(sb, "hpG", p.HpG); Append(sb, "hpB", p.HpB);
        Append(sb, "mpR", p.MpR); Append(sb, "mpG", p.MpG); Append(sb, "mpB", p.MpB);
        Append(sb, "castR", p.CastR); Append(sb, "castG", p.CastG); Append(sb, "castB", p.CastB);
        Append(sb, "showBarrier", p.ShowBarrier);
        Append(sb, "useClassColor", p.UseClassColor);
        Append(sb, "showLevel", p.ShowLevel);
        Append(sb, "showBuffs", p.ShowBuffs);
        Append(sb, "showDebuffs", p.ShowDebuffs);
        Append(sb, "auraFilter", p.AuraFilter);
        Append(sb, "auraIconSize", p.AuraIconSize);
        Append(sb, "auraGap", p.AuraGap);
        Append(sb, "auraMaxIcons", p.AuraMaxIcons);
        Append(sb, "auraShowDuration", p.AuraShowDuration);
        Append(sb, "auraShowStacks", p.AuraShowStacks);
        Append(sb, "lowHpWarning", p.LowHpWarning);
        Append(sb, "lowHpThreshold", p.LowHpThreshold);
        Append(sb, "combatFadeSeconds", p.CombatFadeSeconds);
        Append(sb, "auraWhitelist", p.AuraWhitelist ?? "", true);
        Append(sb, "auraBlacklist", p.AuraBlacklist ?? "", true);
        Append(sb, "freeName", p.FreeName);
        Append(sb, "freeHp", p.FreeHp);
        Append(sb, "freeMp", p.FreeMp);
        Append(sb, "freeCast", p.FreeCast);
        Append(sb, "freeBuffs", p.FreeBuffs);
        Append(sb, "freeDebuffs", p.FreeDebuffs);
        Append(sb, "nameOffX", p.NameOffX); Append(sb, "nameOffY", p.NameOffY);
        Append(sb, "hpOffX", p.HpOffX); Append(sb, "hpOffY", p.HpOffY);
        Append(sb, "mpOffX", p.MpOffX); Append(sb, "mpOffY", p.MpOffY);
        Append(sb, "castOffX", p.CastOffX); Append(sb, "castOffY", p.CastOffY);
        Append(sb, "buffsOffX", p.BuffsOffX); Append(sb, "buffsOffY", p.BuffsOffY);
        Append(sb, "debuffsOffX", p.DebuffsOffX); Append(sb, "debuffsOffY", p.DebuffsOffY);
        Append(sb, "windowPosX", p.WindowPosX);
        Append(sb, "windowPosY", p.WindowPosY, last: true);
        sb.Append("}\n");
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string key, string value, bool isString, bool last = false)
    {
        sb.Append("  \"").Append(key).Append("\": \"").Append(Escape(value)).Append('"');
        sb.Append(last ? "\n" : ",\n");
    }

    private static void Append(StringBuilder sb, string key, bool value, bool last = false)
    {
        sb.Append("  \"").Append(key).Append("\": ").Append(value ? "true" : "false");
        sb.Append(last ? "\n" : ",\n");
    }

    private static void Append(StringBuilder sb, string key, float value, bool last = false)
    {
        sb.Append("  \"").Append(key).Append("\": ")
            .Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append(last ? "\n" : ",\n");
    }

    private static void Append(StringBuilder sb, string key, int value, bool last = false)
    {
        sb.Append("  \"").Append(key).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture));
        sb.Append(last ? "\n" : ",\n");
    }

    private static string Escape(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static CharacterProfile Deserialize(string json)
    {
        var p = new CharacterProfile
        {
            CharacterId = ReadString(json, "characterId") ?? "",
            CharacterName = ReadString(json, "characterName") ?? "",
            ShowFrame = ReadBool(json, "showFrame", true),
            VisibilityMode = ReadInt(json, "visibilityMode", 0),
            ShowName = ReadBool(json, "showName", true),
            ShowCastBar = ReadBool(json, "showCastBar", true),
            HpAboveMana = ReadBool(json, "hpAboveMana", true),
            TextFormat = ReadInt(json, "textFormat", (int)ValueTextFormat.CurrentMax),
            FramePosX = ReadFloat(json, "framePosX", 0f),
            FramePosY = ReadFloat(json, "framePosY", -280f),
            Scale = ReadFloat(json, "scale", 1f),
            Width = ReadFloat(json, "width", 220f),
            BarHeight = ReadFloat(json, "barHeight", 18f),
            BarGap = ReadFloat(json, "barGap", 3f),
            Opacity = ReadFloat(json, "opacity", 100f),
            NameFontSize = ReadFloat(json, "nameFontSize", 14f),
            ValueFontSize = ReadFloat(json, "valueFontSize", 12f),
            HpR = ReadFloat(json, "hpR", 0.75f), HpG = ReadFloat(json, "hpG", 0.18f), HpB = ReadFloat(json, "hpB", 0.18f),
            MpR = ReadFloat(json, "mpR", 0.22f), MpG = ReadFloat(json, "mpG", 0.42f), MpB = ReadFloat(json, "mpB", 0.85f),
            CastR = ReadFloat(json, "castR", 0.90f), CastG = ReadFloat(json, "castG", 0.75f), CastB = ReadFloat(json, "castB", 0.20f),
            ShowBarrier = ReadBool(json, "showBarrier", true),
            UseClassColor = ReadBool(json, "useClassColor", true),
            ShowLevel = ReadBool(json, "showLevel", true),
            ShowBuffs = ReadBool(json, "showBuffs", true),
            ShowDebuffs = ReadBool(json, "showDebuffs", true),
            AuraFilter = ReadInt(json, "auraFilter", 0),
            AuraIconSize = ReadFloat(json, "auraIconSize", 22f),
            AuraGap = ReadFloat(json, "auraGap", 6f),
            AuraMaxIcons = ReadInt(json, "auraMaxIcons", 10),
            AuraShowDuration = ReadBool(json, "auraShowDuration", true),
            AuraShowStacks = ReadBool(json, "auraShowStacks", true),
            LowHpWarning = ReadBool(json, "lowHpWarning", true),
            LowHpThreshold = ReadFloat(json, "lowHpThreshold", 25f),
            CombatFadeSeconds = ReadFloat(json, "combatFadeSeconds", 3f),
            AuraWhitelist = ReadString(json, "auraWhitelist") ?? "",
            AuraBlacklist = ReadString(json, "auraBlacklist") ?? "",
            FreeName = ReadBool(json, "freeName", false),
            FreeHp = ReadBool(json, "freeHp", false),
            FreeMp = ReadBool(json, "freeMp", false),
            FreeCast = ReadBool(json, "freeCast", false),
            FreeBuffs = ReadBool(json, "freeBuffs", false),
            FreeDebuffs = ReadBool(json, "freeDebuffs", false),
            NameOffX = ReadFloat(json, "nameOffX", 0f), NameOffY = ReadFloat(json, "nameOffY", 0f),
            HpOffX = ReadFloat(json, "hpOffX", 0f), HpOffY = ReadFloat(json, "hpOffY", 0f),
            MpOffX = ReadFloat(json, "mpOffX", 0f), MpOffY = ReadFloat(json, "mpOffY", 0f),
            CastOffX = ReadFloat(json, "castOffX", 0f), CastOffY = ReadFloat(json, "castOffY", 0f),
            BuffsOffX = ReadFloat(json, "buffsOffX", 0f), BuffsOffY = ReadFloat(json, "buffsOffY", 0f),
            DebuffsOffX = ReadFloat(json, "debuffsOffX", 0f), DebuffsOffY = ReadFloat(json, "debuffsOffY", 0f),
            WindowPosX = ReadFloat(json, "windowPosX", 480f),
            WindowPosY = ReadFloat(json, "windowPosY", 40f),
        };
        p.Clamp();
        return p;
    }

    private static string? ReadString(string json, string key)
    {
        var token = "\"" + key + "\"";
        var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i = json.IndexOf(':', i + token.Length);
        if (i < 0) return null;
        i++;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '"') return null;
        i++;
        var sb = new StringBuilder();
        while (i < json.Length)
        {
            var c = json[i++];
            if (c == '\\' && i < json.Length) { sb.Append(json[i++]); continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool ReadBool(string json, string key, bool fallback)
    {
        var raw = ReadRaw(json, key);
        if (raw is null) return fallback;
        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }

    private static float ReadFloat(string json, string key, float fallback)
    {
        var raw = ReadRaw(json, key);
        if (raw is null) return fallback;
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static int ReadInt(string json, string key, int fallback)
    {
        var raw = ReadRaw(json, key);
        if (raw is null) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static string? ReadRaw(string json, string key)
    {
        var token = "\"" + key + "\"";
        var i = json.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i = json.IndexOf(':', i + token.Length);
        if (i < 0) return null;
        i++;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] == '"') return null;
        var start = i;
        while (i < json.Length)
        {
            var c = json[i];
            if (c == ',' || c == '}' || char.IsWhiteSpace(c)) break;
            i++;
        }
        return json[start..i];
    }
}
