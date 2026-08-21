using System.Collections;
using SpiritVale.Overlay.Api;
using SpiritVale.Overlay.Api.Combat;
using SpiritVale.Overlay.Api.Protocol;

namespace SpiritVale.Overlay.PlayerNameplate;

internal readonly record struct AuraInfo(
    string Id,
    string? SpriteId,
    bool IsDebuff,
    bool IsSkill,
    bool Infinite,
    float Duration,
    int Stacks);

internal readonly record struct PlayerSnapshot(
    string Name,
    int Health,
    int MaxHealth,
    float HealthNorm,
    int Barrier,
    float ShieldNorm,
    int Mana,
    int MaxMana,
    float ManaNorm,
    bool IsAlive,
    bool IsCasting,
    float CastNorm,
    string CastName,
    bool InCombat,
    int Level,
    int JobLevel,
    int? ArchetypeId,
    string ClassName,
    bool HasVitals,
    bool HasHealth,
    bool HasMana);

internal sealed class PlayerTracker
{
    private const int MaxPendingVitals = 64;

    private readonly object _gate = new();
    private readonly Dictionary<int, Dictionary<string, AuraState>> _aurasByActor = new();
    private readonly Dictionary<int, PendingVitals> _pendingVitals = new();
    private readonly HashSet<int> _localObjects = new();
    private long _lastTick = Environment.TickCount64;
    private int? _characterActorId;
    private string _localName = "";
    private int _health = -1, _maxHealth = 1, _barrier;
    private int _mana, _maxMana = 1;
    private bool _alive = true;
    private int _level = 1, _jobLevel = 1;
    private int? _archetypeId;
    private string _className = "";
    private bool _casting;
    private float _castRemaining, _castMax;
    private string _castName = "";
    private long _combatUntil;
    private int _prevHp = -1;
    private bool _sawHealth, _sawMana;

    private readonly record struct PendingVitals(int? Hp, int? HpMax, int? Mp, int? MpMax);

    private sealed class AuraState
    {
        public string Id = "";
        public string? SpriteId;
        public bool IsDebuff;
        public bool IsSkill;
        public bool Infinite;
        public float Duration;
        public int Stacks = 1;
    }

    public void SetLocal(int? actorId, string? name, int? level, string? className)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(name) && name is not "You" and not "Local Player")
            {
                var trimmed = name.Trim();
                if (_localName.Length > 0
                    && !string.Equals(trimmed, _localName, StringComparison.OrdinalIgnoreCase))
                    ResetRuntimeState();
                _localName = trimmed;
            }

            _characterActorId = actorId;
            if (level is int lv && lv > 0) _level = lv;
            if (!string.IsNullOrWhiteSpace(className)) _className = className;
        }
    }

    public void Tick()
    {
        var now = Environment.TickCount64;
        lock (_gate)
        {
            var dt = Math.Clamp((now - _lastTick) / 1000f, 0f, 0.25f);
            _lastTick = now;
            if (dt <= 0f) return;

            if (_casting)
            {
                _castRemaining = Math.Max(0f, _castRemaining - dt);
                if (_castRemaining <= 0.02f)
                {
                    _casting = false;
                    _castRemaining = 0f;
                }
            }

            foreach (var map in _aurasByActor.Values)
            {
                foreach (var aura in map.Values)
                {
                    if (!aura.Infinite && aura.Duration > 0f)
                        aura.Duration = Math.Max(0f, aura.Duration - dt);
                }
            }
        }
    }

    public void OnPacket(DecodedFishNetEvent evt)
    {
        lock (_gate)
        {
            if (evt.PacketName == "objectDespawn" && evt.ObjectId is int gone)
            {
                _aurasByActor.Remove(gone);
                _pendingVitals.Remove(gone);
                _localObjects.Remove(gone);
                return;
            }

            if (evt.PacketName == "serverRpc" && evt.ObjectId is int serverOid
                && IsUnitBehaviour(evt.NetworkBehaviourType))
                RememberLocal(serverOid);

            if (evt.RpcName is "LoadCharacter_T" or "CharacterCallback_T")
            {
                var job = ReadInt(evt.Fields, "jobLevel");
                if (job is int j && j > 0) _jobLevel = j;
                var lv = ReadInt(evt.Fields, "level");
                if (lv is int l && l > 0) _level = l;
                var cls = ReadString(evt.Fields, "className");
                if (!string.IsNullOrWhiteSpace(cls)) _className = cls;
                var arch = ReadInt(evt.Fields, "archetype");
                if (arch is int a) _archetypeId = a;
            }

            if (evt.PacketName == "syncType")
            {
                var vitals = evt.NetworkBehaviourType is "HealthComponent" or "SkillsComponent";
                if (vitals && evt.ObjectId is int oid)
                {
                    if (IsLocalObject(oid))
                        ApplyVitals(evt.Fields);
                    else
                        BufferVitals(oid, evt.Fields);
                }
                return;
            }

            switch (evt.RpcName)
            {
                case "CastBegin_C":
                    if (!IsLocalObject(evt.ObjectId)) return;
                    BeginCast(evt.Fields);
                    MarkCombat(3.5f);
                    break;
                case "CastComplete_C":
                case "CastCancel_C":
                case "CastInterrupt_C":
                    if (!IsLocalObject(evt.ObjectId)) return;
                    _casting = false;
                    _castRemaining = 0f;
                    break;
                case "ApplyEffectDisplays_O":
                    if (evt.ObjectId is int displayOid)
                        ApplyDisplayBatch(displayOid, evt.Fields);
                    break;
                case "ApplyEffect_T":
                    if (evt.ObjectId is int applyOid)
                    {
                        RememberLocal(applyOid);
                        UpsertAura(applyOid,
                            ReadString(evt.Fields, "statusId") ?? ReadString(evt.Fields, "id"),
                            isSkill: false, remaining: -1f, stacks: 1);
                    }
                    break;
                case "RemoveEffect_T":
                    if (evt.ObjectId is int removeOid)
                    {
                        RememberLocal(removeOid);
                        RemoveAura(removeOid, ReadString(evt.Fields, "statusId") ?? ReadString(evt.Fields, "id"));
                    }
                    break;
                case "ApplySkillDisplay_O":
                    if (evt.ObjectId is int skillOid)
                        UpsertAura(skillOid,
                            ReadString(evt.Fields, "id") ?? ReadString(evt.Fields, "skillId"),
                            isSkill: true, remaining: -1f, stacks: 1);
                    break;
                case "RemoveSkillDisplay_O":
                    if (evt.ObjectId is int skillRemoveOid)
                        RemoveAura(skillRemoveOid, ReadString(evt.Fields, "id") ?? ReadString(evt.Fields, "skillId"));
                    break;
                case "Attack_C":
                    if (IsLocalObject(evt.ObjectId))
                        MarkCombat(3.5f);
                    break;
                case "FullHeal_C":
                    if (!IsLocalObject(evt.ObjectId)) return;
                    if (_maxHealth > 0) _health = _maxHealth;
                    _alive = true;
                    _sawHealth = true;
                    break;
                case "Death_C":
                    if (!IsLocalObject(evt.ObjectId)) return;
                    _alive = false;
                    _health = 0;
                    _sawHealth = true;
                    _casting = false;
                    break;
            }
        }
    }

    public void OnDamage(CombatDamageEvent evt)
    {
        lock (_gate)
        {
            if (NamesMatch(evt.TargetName) && evt.TargetActorId > 0)
                RememberLocal(evt.TargetActorId);
            if (NamesMatch(evt.SourceName) && evt.SourceActorId > 0)
                RememberLocal(evt.SourceActorId);

            var local = IsLocalActor(evt.SourceActorId) || IsLocalActor(evt.TargetActorId)
                        || NamesMatch(evt.SourceName) || NamesMatch(evt.TargetName);
            if (!local) return;
            MarkCombat(3.5f);
        }
    }

    public void OnHeal(CombatHealEvent evt)
    {
        lock (_gate)
        {
            if (NamesMatch(evt.TargetName) && evt.TargetActorId > 0)
                RememberLocal(evt.TargetActorId);
            if (!IsLocalActor(evt.TargetActorId) && !NamesMatch(evt.TargetName)) return;
            MarkCombat(2f);
        }
    }

    public void OnDeath(CombatDeathEvent evt)
    {
        lock (_gate)
        {
            if (!IsLocalActor(evt.ActorId) && !NamesMatch(evt.ActorName)) return;
            _alive = false;
            _health = 0;
            _sawHealth = true;
            _casting = false;
        }
    }

    public bool TrySnapshot(out PlayerSnapshot snap)
    {
        snap = default;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_localName) && _characterActorId is null && _localObjects.Count == 0)
                return false;

            if (_sawHealth && _health > _maxHealth) _maxHealth = Math.Max(1, _health);
            if (_sawMana && _mana > _maxMana) _maxMana = Math.Max(1, _mana);

            var hp = _sawHealth ? Math.Max(0, _health) : 0;
            var hpMax = Math.Max(1, _maxHealth);
            var mp = _sawMana ? Math.Max(0, _mana) : 0;
            var mpMax = Math.Max(1, _maxMana);
            var hpNorm = _sawHealth && hpMax > 0 ? Math.Clamp(hp / (float)hpMax, 0f, 1f) : 0f;
            var mpNorm = _sawMana && mpMax > 0 ? Math.Clamp(mp / (float)mpMax, 0f, 1f) : 0f;
            var shield = Math.Max(0, _barrier);
            var shieldNorm = hpMax > 0 ? Math.Clamp(shield / (float)hpMax, 0f, 1f) : 0f;
            var now = Environment.TickCount64;
            var combat = _casting || now < _combatUntil;
            var castNorm = _casting && _castMax > 0.001f
                ? Math.Clamp(1f - (_castRemaining / _castMax), 0f, 1f)
                : (_casting ? 0.5f : 0f);
            var hasVitals = _sawHealth || _sawMana;

            snap = new PlayerSnapshot(
                string.IsNullOrWhiteSpace(_localName) ? "Player" : _localName,
                hp, hpMax, hpNorm,
                shield, shieldNorm,
                mp, mpMax, mpNorm,
                !_sawHealth || (_alive && hp > 0),
                _casting, castNorm, _castName,
                combat,
                Math.Max(1, _level), Math.Max(1, _jobLevel),
                _archetypeId, _className, hasVitals, _sawHealth, _sawMana);
            return hasVitals || !string.IsNullOrWhiteSpace(_localName);
        }
    }

    public void CollectAuras(CharacterProfile profile, List<AuraInfo> buffs, List<AuraInfo> debuffs)
    {
        buffs.Clear();
        debuffs.Clear();
        lock (_gate)
        {
            var merged = new Dictionary<string, AuraState>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in LocalAuraKeys())
            {
                if (!_aurasByActor.TryGetValue(id, out var map)) continue;
                foreach (var (auraId, aura) in map)
                    merged[auraId] = aura;
            }
            if (merged.Count == 0) return;

            var filter = profile.GetAuraFilter();
            var max = Math.Max(1, profile.AuraMaxIcons);
            foreach (var aura in merged.Values.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                if (!PassesFilter(aura.IsSkill, filter)) continue;
                if (!PassesListFilters(aura.Id, profile)) continue;
                var info = new AuraInfo(aura.Id, aura.SpriteId, aura.IsDebuff, aura.IsSkill,
                    aura.Infinite, aura.Duration, Math.Max(1, aura.Stacks));
                if (info.IsDebuff)
                {
                    if (!profile.ShowDebuffs || debuffs.Count >= max) continue;
                    debuffs.Add(info);
                }
                else
                {
                    if (!profile.ShowBuffs || buffs.Count >= max) continue;
                    buffs.Add(info);
                }
            }
        }
    }

    public static string FormatValue(ValueTextFormat format, int current, int max, float norm)
    {
        var pct = (int)Math.Round(Math.Clamp(norm, 0f, 1f) * 100f);
        return format switch
        {
            ValueTextFormat.Hidden => "",
            ValueTextFormat.Percent => pct + "%",
            ValueTextFormat.Current => current.ToString(),
            ValueTextFormat.CurrentMaxPercent => current + " / " + max + "  (" + pct + "%)",
            _ => current + " / " + max,
        };
    }

    private IEnumerable<int> LocalAuraKeys()
    {
        foreach (var id in _localObjects)
            yield return id;
        if (_characterActorId is int character)
            yield return character;
    }

    private void ApplyVitals(IReadOnlyDictionary<string, object?> fields)
    {
        var hp = ReadInt(fields, "currentHealth");
        var hpMax = ReadInt(fields, "maxHealth");
        var mp = ReadInt(fields, "currentMana");
        var mpMax = ReadInt(fields, "maxMana");
        var barrier = ReadInt(fields, "barrier");
        if (hp is int h)
        {
            if (_prevHp >= 0 && h < _prevHp) MarkCombat(3.5f);
            _prevHp = h;
            _health = h;
            _alive = h > 0;
            _sawHealth = true;
            if (h > _maxHealth) _maxHealth = h;
        }
        if (hpMax is int hm && hm > 0)
        {
            _maxHealth = hm;
            _sawHealth = true;
        }
        if (mp is int m)
        {
            _mana = m;
            _sawMana = true;
            if (m > _maxMana) _maxMana = m;
        }
        if (mpMax is int mm && mm > 0)
        {
            _maxMana = mm;
            _sawMana = true;
        }
        if (barrier is int b) _barrier = Math.Max(0, b);
    }

    private void BufferVitals(int objectId, IReadOnlyDictionary<string, object?> fields)
    {
        _pendingVitals.TryGetValue(objectId, out var cur);
        var next = new PendingVitals(
            ReadInt(fields, "currentHealth") ?? cur.Hp,
            ReadInt(fields, "maxHealth") ?? cur.HpMax,
            ReadInt(fields, "currentMana") ?? cur.Mp,
            ReadInt(fields, "maxMana") ?? cur.MpMax);
        if (next.Hp is null && next.HpMax is null && next.Mp is null && next.MpMax is null)
            return;
        if (_pendingVitals.Count >= MaxPendingVitals && !_pendingVitals.ContainsKey(objectId))
            _pendingVitals.Remove(_pendingVitals.Keys.First());
        _pendingVitals[objectId] = next;
    }

    private void FlushPendingVitals(int objectId)
    {
        if (!_pendingVitals.TryGetValue(objectId, out var pending))
            return;
        var fields = new Dictionary<string, object?>();
        if (pending.Hp is int hp) fields["currentHealth"] = hp;
        if (pending.HpMax is int hpMax) fields["maxHealth"] = hpMax;
        if (pending.Mp is int mp) fields["currentMana"] = mp;
        if (pending.MpMax is int mpMax) fields["maxMana"] = mpMax;
        ApplyVitals(fields);
        _pendingVitals.Remove(objectId);
    }

    private void BeginCast(IReadOnlyDictionary<string, object?> fields)
    {
        var id = ReadString(fields, "dto.Id") ?? ReadString(fields, "skillId");
        var live = ReadFloat(fields, "castTime", 0f);
        var dtoCast = ReadFloat(fields, "dto.CastTime", 0f);
        var duration = live > 0.05f ? live : dtoCast;
        if (duration <= 0.05f) duration = 0.8f;
        _casting = true;
        _castMax = duration;
        _castRemaining = duration;
        _castName = string.IsNullOrWhiteSpace(id) ? "Casting" : id;
    }

    private void ApplyDisplayBatch(int actorId, IReadOnlyDictionary<string, object?> fields)
    {
        if (!fields.TryGetValue("effectApplies", out var raw) || raw is null)
            return;

        foreach (var line in ReadStringList(raw))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length < 1) continue;
            var id = parts[0];
            var remaining = -1f;
            var stacks = 1;
            if (parts.Length > 1)
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out remaining);
            if (parts.Length > 2) int.TryParse(parts[2], out stacks);
            UpsertAura(actorId, id, isSkill: false, remaining, Math.Max(1, stacks));
        }
    }

    private void UpsertAura(int actorId, string? id, bool isSkill, float remaining, int stacks)
    {
        if (actorId <= 0 || string.IsNullOrWhiteSpace(id)) return;
        var map = ActorAuras(actorId);
        var meta = StatusCatalog.Resolve(id);
        if (!map.TryGetValue(id, out var aura))
        {
            aura = new AuraState { Id = id };
            map[id] = aura;
        }
        aura.IsSkill = aura.IsSkill || isSkill;
        aura.IsDebuff = meta.IsDebuff;
        aura.SpriteId = meta.SpriteId;
        aura.Stacks = Math.Max(1, stacks);
        if (remaining < 0f)
        {
            aura.Infinite = true;
        }
        else
        {
            aura.Infinite = false;
            aura.Duration = remaining;
        }
    }

    private void RemoveAura(int actorId, string? id)
    {
        if (actorId <= 0 || string.IsNullOrWhiteSpace(id)) return;
        if (_aurasByActor.TryGetValue(actorId, out var map))
            map.Remove(id);
    }

    private Dictionary<string, AuraState> ActorAuras(int actorId)
    {
        if (!_aurasByActor.TryGetValue(actorId, out var map))
        {
            map = new Dictionary<string, AuraState>(StringComparer.OrdinalIgnoreCase);
            _aurasByActor[actorId] = map;
        }
        return map;
    }

    private void RememberLocal(int objectId)
    {
        if (objectId <= 0) return;
        if (_localObjects.Add(objectId))
            FlushPendingVitals(objectId);
    }

    private void ResetRuntimeState()
    {
        _localObjects.Clear();
        _pendingVitals.Clear();
        _aurasByActor.Clear();
        _health = -1;
        _maxHealth = 1;
        _mana = 0;
        _maxMana = 1;
        _barrier = 0;
        _sawHealth = false;
        _sawMana = false;
        _prevHp = -1;
        _casting = false;
        _alive = true;
    }

    private void MarkCombat(float seconds)
        => _combatUntil = Math.Max(_combatUntil, Environment.TickCount64 + (long)(seconds * 1000));

    private bool IsLocalObject(int? objectId)
        => objectId is int oid && oid > 0 && _localObjects.Contains(oid);

    private bool IsLocalActor(int actorId)
        => actorId > 0 && _localObjects.Contains(actorId);

    private bool NamesMatch(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !string.IsNullOrWhiteSpace(_localName)
           && string.Equals(name, _localName, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnitBehaviour(string? type) => type is
        "PlayerController" or "HealthComponent" or "SkillsComponent"
        or "StatusComponent" or "CombatComponent";

    private static bool PassesFilter(bool isSkill, AuraFilterMode filter) => filter switch
    {
        AuraFilterMode.SkillsOnly => isSkill,
        AuraFilterMode.StatusOnly => !isSkill,
        _ => true,
    };

    private static bool PassesListFilters(string id, CharacterProfile profile)
    {
        if (profile.HasAuraBlacklist && profile.AuraBlacklistSet.Contains(id)) return false;
        if (profile.HasAuraWhitelist && !profile.AuraWhitelistSet.Contains(id)) return false;
        return true;
    }

    private static List<string> ReadStringList(object? raw)
    {
        switch (raw)
        {
            case List<string> list:
                return list;
            case IEnumerable<string> strings:
                return strings.ToList();
            case IEnumerable enumerable:
            {
                var result = new List<string>();
                foreach (var item in enumerable)
                {
                    var text = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                }
                return result;
            }
            default:
                return [];
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields, string key)
        => fields.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int? ReadInt(IReadOnlyDictionary<string, object?> fields, string key)
    {
        if (!fields.TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            int i => i,
            long l => (int)l,
            float f => (int)f,
            double d => (int)d,
            _ => int.TryParse(v.ToString(), out var p) ? p : null,
        };
    }

    private static float ReadFloat(IReadOnlyDictionary<string, object?> fields, string key, float fallback)
    {
        if (!fields.TryGetValue(key, out var v) || v is null) return fallback;
        return v switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            _ => float.TryParse(v.ToString(), out var p) ? p : fallback,
        };
    }
}
