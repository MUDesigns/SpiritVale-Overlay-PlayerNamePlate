using System.Text.Json;

namespace SpiritVale.Overlay.PlayerNameplate;

internal sealed record StatusInfo(string Id, string DisplayName, string? SpriteId, bool IsDebuff);

internal static class StatusCatalog
{
    private static readonly Dictionary<string, StatusInfo> ById =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DebuffHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stun", "Blind", "Silence", "Slow", "Root", "Freeze", "Frozen", "Poison",
        "Bleed", "Bleeding", "Burn", "Burning", "Curse", "Decay", "Sleep", "Fear",
        "Weakness", "ArmorBreak", "Marked", "Anathema",
    };

    static StatusCatalog()
    {
        try
        {
            var asm = typeof(StatusCatalog).Assembly;
            using var stream = asm.GetManifestResourceStream("SpiritVale.Overlay.PlayerNameplate.status-catalog.json");
            if (stream is null) return;
            using var doc = JsonDocument.Parse(stream);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = el.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = el.TryGetProperty("displayName", out var n) ? n.GetString() ?? id : id;
                string? sprite = null;
                if (el.TryGetProperty("spriteId", out var s) && s.ValueKind == JsonValueKind.String)
                    sprite = s.GetString();
                var debuff = el.TryGetProperty("isDebuff", out var d) && d.ValueKind == JsonValueKind.True;
                ById[id] = new StatusInfo(id, name, sprite, debuff);
            }
        }
        catch
        {
            // Catalog is optional; ids still render as colored tiles.
        }
    }

    public static StatusInfo Resolve(string id)
    {
        if (ById.TryGetValue(id, out var info)) return info;
        var debuff = LooksLikeDebuff(id);
        return new StatusInfo(id, id, null, debuff);
    }

    private static bool LooksLikeDebuff(string id)
    {
        foreach (var hint in DebuffHints)
        {
            if (id.Contains(hint, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return id.EndsWith("Enemy", StringComparison.OrdinalIgnoreCase);
    }
}
