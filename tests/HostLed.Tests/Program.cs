using System.Text.Json;
using System.Text.Json.Nodes;
using HostLed;

var passed = 0;
void Test(string name, Action action)
{
    action();
    passed++;
    Console.WriteLine($"PASS {name}");
}
void Assert(bool condition) { if (!condition) throw new Exception("断言失败"); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception e) when (e is ConfigurationException or JsonException or ArgumentException) { return; }
    throw new Exception("无效输入未被拒绝");
}
var key = new KeyPosition(0, 17);
var red = new LedState(true, 255, 0, 0);
var white = new LedState(true, 255, 255, 255);
Rule Rule(string id, long order, LedState state, string endpoint = "speaker") =>
    new(id, id, order, endpoint, [new(key, state)]);
var a = Rule("a", 1, red);
var b = Rule("b", 2, white);
var json = JsonSerializer.Serialize(new
{
    version = 1, nextCreationOrder = 2,
    rules = new[] { new {
        id = "a", name = "音响", creationOrder = 1,
        source = new { type = "windows.audio.defaultPlayback", role = "multimedia" },
        match = new { endpointId = "speaker" },
        display = new[] { new { row = 0, column = 17, @override = true, color = "#FF0000" } }
    } }
});
Test("有效配置和精确匹配", () => {
    var engine = new RuleEngine(ConfigReader.Parse(json));
    engine.UpdateAudio("Speaker"); Assert(engine.Snapshot().Length == 0);
    engine.UpdateAudio("speaker"); Assert(engine.Snapshot().Single().State == red);
});
Test("同时激活按创建顺序而非数组位置", () => {
    var engine = new RuleEngine(new(3, [b, a]));
    engine.UpdateAudio("speaker"); Assert(engine.Snapshot().Single().State == white);
});
Test("新触发优先，重复通知不抢占，规则移除回退", () => {
    var engine = new RuleEngine(new(3, [a, b with { EndpointId = "headphones" }]));
    engine.UpdateAudio("headphones");
    engine.Reload(new(3, [a with { EndpointId = "headphones" }, b with { EndpointId = "headphones" }]));
    Assert(engine.Snapshot().Single().State == red);
    engine.UpdateAudio("headphones"); Assert(engine.Snapshot().Single().State == red);
    engine.Reload(new(3, [b with { EndpointId = "headphones" }]));
    Assert(engine.Snapshot().Single().State == white);
});
Test("仅颜色更新保留触发顺序", () => {
    var engine = new RuleEngine(new(3, [a, b])); engine.UpdateAudio("speaker");
    engine.Reload(new(3, [a with { Display = [new(key, new(true, 0, 0, 255))] }, b]));
    Assert(engine.Snapshot().Single().State == white);
});
Test("解除覆盖阻止低优先级显示，失效后恢复", () => {
    var release = b with { Display = [new(key, new(false, 0, 0, 0))] };
    var engine = new RuleEngine(new(3, [a, release])); engine.UpdateAudio("speaker");
    Assert(engine.Snapshot().Length == 0);
    engine.Reload(new(3, [a])); Assert(engine.Snapshot().Single().State == red);
});
Test("黑色仍保留覆盖", () => {
    var engine = new RuleEngine(new(2, [Rule("a", 1, new(true, 0, 0, 0))]));
    engine.UpdateAudio("speaker"); Assert(engine.Snapshot().Single().State.Override);
});
Test("Unknown保留，明确无设备清除，初始Unknown为空", () => {
    var engine = new RuleEngine(new(2, [a])); engine.UpdateAudio(null, false);
    Assert(engine.Snapshot().Length == 0);
    engine.UpdateAudio("speaker"); engine.UpdateAudio(null, false);
    Assert(engine.Snapshot().Length == 1);
    engine.UpdateAudio(null); Assert(engine.Snapshot().Length == 0);
});
Test("创建顺序不得修改或重用", () => {
    Reject(() => ConfigReader.ValidateReplacement(new(3, [a]), new(3, [a with { CreationOrder = 2 }])));
    Reject(() => ConfigReader.ValidateReplacement(new(3, [a]), new(3, [b])));
    Reject(() => ConfigReader.ValidateReplacement(new(3, [a]), new(2, [a])));
});
Test("语法错误和null拒绝", () => { Reject(() => ConfigReader.Parse("{")); Reject(() => ConfigReader.Parse("null")); });
Test("未知/大小写错误/重复字段拒绝", () => {
    Reject(() => ConfigReader.Parse(json.Replace("\"version\":1", "\"Version\":1")));
    Reject(() => ConfigReader.Parse(json.Replace("\"version\":1", "\"version\":1,\"version\":1")));
    Reject(() => ConfigReader.Parse(json.Replace("\"version\":1", "\"version\":1,\"extra\":true")));
});
Test("无LED/越界/错误类型/错误颜色拒绝", () => {
    Reject(() => ConfigReader.Parse(json.Replace("\"column\":17", "\"column\":13")));
    Reject(() => ConfigReader.Parse(json.Replace("\"column\":17", "\"column\":21")));
    Reject(() => ConfigReader.Parse(json.Replace("\"override\":true", "\"override\":1")));
    Reject(() => ConfigReader.Parse(json.Replace("#FF0000", "red")));
});
Test("LED二进制固定向量及重复坐标拒绝", () => {
    Assert(Convert.ToHexString(LedItemCodec.Encode([new(key, red)])) == "001101FF0000");
    Assert(Convert.ToHexString(LedItemCodec.Encode([new(key, new(false, 255, 255, 255))])) == "001100000000");
    Reject(() => LedItemCodec.Encode([new(key, red), new(key, white)]));
});
Test("重复规则/创建序号/显示坐标拒绝", () => {
    var node = JsonNode.Parse(json)!;
    var rules = node["rules"]!.AsArray();
    rules.Add(rules[0]!.DeepClone());
    Reject(() => ConfigReader.Parse(node.ToJsonString()));
    rules[1]!["id"] = "b";
    Reject(() => ConfigReader.Parse(node.ToJsonString()));
    node = JsonNode.Parse(json)!;
    var items = node["rules"]![0]!["display"]!.AsArray();
    items.Add(items[0]!.DeepClone());
    Reject(() => ConfigReader.Parse(node.ToJsonString()));
});
Test("重载失败保持当前结果", () => {
    var engine = new RuleEngine(new(3, [a, b])); engine.UpdateAudio("speaker");
    Reject(() => engine.Reload(new(2, [a])));
    Assert(engine.Snapshot().Single().State == white);
});
Test("多键稀疏快照稳定排序及二进制长度", () => {
    var rule = a with { Display = [new(new(0, 20), white), new(key, red)] };
    var engine = new RuleEngine(new(2, [rule])); engine.UpdateAudio("speaker");
    var snapshot = engine.Snapshot();
    Assert(snapshot.Length == 2 && snapshot[0].Key == key);
    Assert(LedItemCodec.Encode(snapshot).Length == 12);
});
Console.WriteLine($"全部通过：{passed} 组测试。");
