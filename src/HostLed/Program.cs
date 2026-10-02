using System.Text;
using System.Text.Json;
using HostLed;

Console.OutputEncoding = Encoding.UTF8;
try
{
    if (args is ["validate", var input])
    {
        var config = ConfigReader.Parse(File.ReadAllText(input));
        Console.WriteLine($"配置有效：{config.Rules.Length} 条规则。仅完成离线校验，未验证音频设备存在性或连接键盘。");
        return 0;
    }
    if (args is ["config-set", var source, var destination])
    {
        var json = File.ReadAllText(source);
        var config = ConfigReader.Parse(json);
        var target = Path.GetFullPath(destination);
        if (File.Exists(target)) ConfigReader.ValidateReplacement(ConfigReader.Parse(File.ReadAllText(target)), config);
        var temp = Path.Combine(Path.GetDirectoryName(target)!, $".hostled-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Console.WriteLine($"配置已校验并保存：{target}");
        return 0;
    }
    if (args.Length == 4 && args[0] == "render" && args[2] == "--endpoint")
    {
        var engine = new RuleEngine(ConfigReader.Parse(File.ReadAllText(args[1])));
        engine.UpdateAudio(args[3]);
        var snapshot = engine.Snapshot();
        Console.WriteLine(JsonSerializer.Serialize(snapshot.Select(i => new
        {
            row = i.Key.Row, column = i.Key.Column, @override = i.State.Override,
            color = $"#{i.State.R:X2}{i.State.G:X2}{i.State.B:X2}"
        }), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"LED 项二进制（不含 HID 事务头，未发送）：{Convert.ToHexString(LedItemCodec.Encode(snapshot))}");
        return 0;
    }
    Console.WriteLine("HostLed V1 离线工具\n  validate <配置.json>\n  config-set <来源.json> <目标.json>\n  render <配置.json> --endpoint <精确设备ID>");
    return args.Length == 0 ? 0 : 2;
}
catch (JsonException ex)
{
    Console.Error.WriteLine($"JSON 语法错误：{ex.Path}，行 {ex.LineNumber + 1}，字节列 {ex.BytePositionInLine + 1}：{ex.Message}");
    return 1;
}
catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
