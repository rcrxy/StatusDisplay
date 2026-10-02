namespace HostLed;

// Single-owner engine: the future agent must serialize provider/reload events.
public sealed class RuleEngine(Configuration configuration)
{
    private Configuration config = configuration;
    private readonly Dictionary<string, long> active = new(StringComparer.Ordinal);
    private long sequence;
    private string? endpoint;
    private bool hasKnownState;

    public void UpdateAudio(string? endpointId, bool known = true)
    {
        if (!known) return;
        endpoint = endpointId;
        hasKnownState = true;
        Evaluate();
    }

    public void Reload(Configuration replacement)
    {
        ConfigReader.ValidateReplacement(config, replacement);
        var next = replacement.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var before in config.Rules)
            if (!next.TryGetValue(before.Id, out var after) || before.EndpointId != after.EndpointId)
                active.Remove(before.Id);
        config = replacement;
        Evaluate();
    }

    private void Evaluate()
    {
        var newlyActive = new List<Rule>();
        foreach (var rule in config.Rules)
        {
            var matches = hasKnownState && endpoint is not null && rule.EndpointId == endpoint;
            if (!matches) active.Remove(rule.Id);
            else if (!active.ContainsKey(rule.Id)) newlyActive.Add(rule);
        }
        foreach (var rule in newlyActive.OrderBy(r => r.CreationOrder))
            active.Add(rule.Id, checked(++sequence));
    }

    public DisplayItem[] Snapshot()
    {
        var result = new Dictionary<KeyPosition, LedState>();
        foreach (var rule in config.Rules.Where(r => active.ContainsKey(r.Id)).OrderBy(r => active[r.Id]))
            foreach (var item in rule.Display) result[item.Key] = item.State;
        return result.Where(p => p.Value.Override).OrderBy(p => p.Key.Row).ThenBy(p => p.Key.Column)
            .Select(p => new DisplayItem(p.Key, p.Value)).ToArray();
    }
}

public static class LedItemCodec
{
    // Only item payloads; no transaction framing or HID write is performed here.
    public static byte[] Encode(IReadOnlyList<DisplayItem> items)
    {
        var bytes = new byte[checked(items.Count * 6)];
        var keys = new HashSet<KeyPosition>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!ConfigReader.HasLed(item.Key) || !keys.Add(item.Key))
                throw new ArgumentException("LED 坐标无效或重复", nameof(items));
            var offset = i * 6;
            bytes[offset] = item.Key.Row;
            bytes[offset + 1] = item.Key.Column;
            bytes[offset + 2] = item.State.Override ? (byte)1 : (byte)0;
            bytes[offset + 3] = item.State.Override ? item.State.R : (byte)0;
            bytes[offset + 4] = item.State.Override ? item.State.G : (byte)0;
            bytes[offset + 5] = item.State.Override ? item.State.B : (byte)0;
        }
        return bytes;
    }
}
