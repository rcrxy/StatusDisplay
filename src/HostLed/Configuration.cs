using System.Text.Json;
using System.Text.RegularExpressions;

namespace HostLed;

public readonly record struct KeyPosition(byte Row, byte Column);
public readonly record struct LedState(bool Override, byte R, byte G, byte B);
public sealed record DisplayItem(KeyPosition Key, LedState State);
public sealed record Rule(string Id, string Name, long CreationOrder, string EndpointId, DisplayItem[] Display);
public sealed record Configuration(long NextCreationOrder, Rule[] Rules);

public sealed class ConfigurationException(string message) : Exception(message);

public static class ConfigReader
{
    private static readonly HashSet<KeyPosition> LedPositions = LoadPositions();
    public static bool HasLed(KeyPosition key) => LedPositions.Contains(key);

    private static HashSet<KeyPosition> LoadPositions()
    {
        var assembly = typeof(ConfigReader).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("q6-pro-ansi.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("leds").EnumerateArray()
            .Select(x => new KeyPosition(x.GetProperty("row").GetByte(), x.GetProperty("column").GetByte())).ToHashSet();
    }

    public static Configuration Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement;
        Object(root, "$", "version", "nextCreationOrder", "rules");
        if (Number(root, "version", "$", 1, 1) != 1) throw new ConfigurationException("$.version: 不支持的版本");
        var next = Number(root, "nextCreationOrder", "$", 1, long.MaxValue);
        var ruleArray = Array(root, "rules", "$");
        var rules = new List<Rule>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<long>();
        var index = 0;
        foreach (var rule in ruleArray.EnumerateArray())
        {
            var p = $"$.rules[{index++}]";
            Object(rule, p, "id", "name", "creationOrder", "source", "match", "display");
            var id = Text(rule, "id", p);
            if (!ids.Add(id)) throw new ConfigurationException($"{p}.id: 重复标识");
            var name = Text(rule, "name", p);
            var order = Number(rule, "creationOrder", p, 1, long.MaxValue - 1);
            if (!orders.Add(order)) throw new ConfigurationException($"{p}.creationOrder: 重复创建序号");
            if (order >= next) throw new ConfigurationException($"{p}.creationOrder: 必须小于 nextCreationOrder");
            var source = Required(rule, "source", p);
            Object(source, p + ".source", "type", "role");
            if (Text(source, "type", p + ".source") != "windows.audio.defaultPlayback")
                throw new ConfigurationException($"{p}.source.type: 不支持的状态来源");
            if (Text(source, "role", p + ".source") != "multimedia")
                throw new ConfigurationException($"{p}.source.role: V1 仅支持 multimedia");
            var match = Required(rule, "match", p);
            Object(match, p + ".match", "endpointId");
            var endpoint = Text(match, "endpointId", p + ".match");
            var displays = new List<DisplayItem>();
            var keys = new HashSet<KeyPosition>();
            var displayIndex = 0;
            foreach (var item in Array(rule, "display", p).EnumerateArray())
            {
                var ip = $"{p}.display[{displayIndex++}]";
                Object(item, ip, "row", "column", "override", "color");
                var key = new KeyPosition((byte)Number(item, "row", ip, 0, 5), (byte)Number(item, "column", ip, 0, 20));
                if (!HasLed(key)) throw new ConfigurationException($"{ip}: 该坐标没有 LED");
                if (!keys.Add(key)) throw new ConfigurationException($"{ip}: 同一规则内坐标重复");
                var flag = Required(item, "override", ip);
                if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ConfigurationException($"{ip}.override: 必须为布尔值");
                var color = Text(item, "color", ip);
                if (!Regex.IsMatch(color, "\\A#[0-9a-fA-F]{6}\\z", RegexOptions.CultureInvariant))
                    throw new ConfigurationException($"{ip}.color: 必须为 #RRGGBB");
                displays.Add(new(key, new(flag.GetBoolean(), Convert.ToByte(color[1..3], 16), Convert.ToByte(color[3..5], 16), Convert.ToByte(color[5..7], 16))));
            }
            if (displays.Count == 0) throw new ConfigurationException($"{p}.display: 至少配置一个按键");
            rules.Add(new(id, name, order, endpoint, displays.ToArray()));
        }
        return new(next, rules.ToArray());
    }

    public static void ValidateReplacement(Configuration old, Configuration replacement)
    {
        if (replacement.NextCreationOrder < old.NextCreationOrder)
            throw new ConfigurationException("$.nextCreationOrder: 不允许回退创建序号高水位");
        var previous = old.Rules.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var rule in replacement.Rules)
        {
            if (previous.TryGetValue(rule.Id, out var before))
            {
                if (rule.CreationOrder != before.CreationOrder)
                    throw new ConfigurationException($"规则 {rule.Id}: 已有规则必须保留 creationOrder");
            }
            else if (rule.CreationOrder < old.NextCreationOrder)
                throw new ConfigurationException($"规则 {rule.Id}: 新建规则不得重用历史创建序号");
        }
    }

    private static void Object(JsonElement e, string path, params string[] allowed)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new ConfigurationException($"{path}: 必须为对象");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in e.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new ConfigurationException($"{path}.{property.Name}: 重复属性");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) throw new ConfigurationException($"{path}.{property.Name}: 未定义的字段");
        }
    }
    private static JsonElement Required(JsonElement e, string name, string path) =>
        e.TryGetProperty(name, out var value) ? value : throw new ConfigurationException($"{path}.{name}: 缺少必填字段");
    private static string Text(JsonElement e, string name, string path)
    {
        var v = Required(e, name, path);
        if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
            throw new ConfigurationException($"{path}.{name}: 必须为非空字符串");
        return v.GetString()!;
    }
    private static long Number(JsonElement e, string name, string path, long min, long max)
    {
        var v = Required(e, name, path);
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out var n) || n < min || n > max)
            throw new ConfigurationException($"{path}.{name}: 必须为 {min}..{max} 的整数");
        return n;
    }
    private static JsonElement Array(JsonElement e, string name, string path)
    {
        var v = Required(e, name, path);
        if (v.ValueKind != JsonValueKind.Array) throw new ConfigurationException($"{path}.{name}: 必须为数组");
        return v;
    }
}
