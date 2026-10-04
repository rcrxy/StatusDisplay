using System.Buffers.Binary;
using System.Text.Json;
using HostLed;

if (args.Length != 2) throw new ArgumentException("需要固件仓库路径和输出目录");
var firmware = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
using var board = JsonDocument.Parse(File.ReadAllText(Path.Combine(firmware, "keyboards/keychron/q6_pro/ansi_encoder/info.json")));
var map = Enumerable.Repeat((byte)255, 126).ToArray();
var positions = new List<KeyPosition>();
foreach (var led in board.RootElement.GetProperty("rgb_matrix").GetProperty("layout").EnumerateArray())
{
    var matrix = led.GetProperty("matrix");
    var key = new KeyPosition(matrix[0].GetByte(), matrix[1].GetByte());
    Check(ConfigReader.HasLed(key) && map[key.Row * 21 + key.Column] == 255, "板型坐标必须一致且唯一");
    map[key.Row * 21 + key.Column] = checked((byte)positions.Count);
    positions.Add(key);
}
Check(positions.Count == 108, "108 LED 布局");
for (byte row = 0; row < 6; row++)
    for (byte col = 0; col < 21; col++)
        Check(ConfigReader.HasLed(new(row, col)) == (map[row * 21 + col] != 255), "完整矩阵对应");

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { return; }
    throw new Exception("编码器未拒绝非法参数");
}
static DisplayItem Item(byte column, byte r, byte g, byte b, bool enabled = true) => new(new(0, column), new(enabled, r, g, b));
var red = new[] { Item(17, 255, 0, 0), Item(18, 255, 0, 0), Item(19, 255, 0, 0) };
var whiteBlack = new[] { Item(17, 255, 255, 255), Item(18, 0, 0, 0) };
var vectors = SnapshotReportCodec.Encode(1, 1, 1, [red[0]]);
Check(Convert.ToHexString(vectors[0]) == "07AC020101000000000000000100000001000000010100000000000000000000", "BEGIN 固定向量");
Check(vectors[1].SequenceEqual(Convert.FromHexString("07 AC 03 01 01 00 00 00 00 00 00 00 01 00 00 00 00 01 00 11 01 FF 00 00 00 00 00 00 00 00 00 00".Replace(" ", ""))), "DATA 固定向量及零填充");
Check(Convert.ToHexString(vectors[2]) == "07AC040101000000000000000100000000000000000000000000000000000000", "COMMIT 固定向量");
Reject(() => SnapshotReportCodec.Encode(0, 1, 1, red));
Reject(() => SnapshotReportCodec.Encode(1, 0, 1, red));
Reject(() => SnapshotReportCodec.Encode(1, 1, 0, red));
Reject(() => SnapshotReportCodec.Encode(1, 1, 1, Enumerable.Repeat(red[0], 109).ToArray()));
Reject(() => SnapshotReportCodec.Encode(1, 1, 1, [red[0], red[0]]));
Reject(() => SnapshotReportCodec.Encode(1, 1, 1, [Item(13, 255, 0, 0)]));

// Expected displays below are specified independently of RuleEngine.Snapshot().
using var trace = new BinaryWriter(File.Create(Path.Combine(output, "trace.bin")));
using var log = new StreamWriter(Path.Combine(output, "scenarios.txt"));
trace.Write(map);
var record = 0;
var scenarios = 0;
const ulong session = 0x8877665544332211;
uint transaction = 0x10203040;
uint version = 0x50607080;
DisplayItem[] active = [];
void Scenario(string name) { scenarios++; log.WriteLine($"record {record + 1}: {name}"); Console.WriteLine($"TRACE {name}"); }
void Packet(byte[] packet, byte status, Action<byte[], byte[]>? extra = null)
{
    var expected = new byte[32]; var mask = new byte[32];
    packet.AsSpan(0, 16).CopyTo(expected);
    Array.Fill(mask, (byte)255, 0, 16); expected[3] = status;
    if (packet[2] == 3) { expected[16] = packet[16]; expected[17] = packet[17]; Array.Fill(mask, (byte)255, 16, 16); }
    extra?.Invoke(expected, mask);
    trace.Write((byte)1); trace.Write(packet); trace.Write(expected); trace.Write(mask); record++;
}
void Frame(DisplayItem[] expected, byte brightness = 255, bool enabled = true)
{
    var data = new byte[108 * 4];
    foreach (var item in expected.Where(x => x.State.Override && enabled))
    {
        var index = map[item.Key.Row * 21 + item.Key.Column] * 4;
        data[index] = 1;
        data[index + 1] = (byte)(item.State.R * brightness / 255);
        data[index + 2] = (byte)(item.State.G * brightness / 255);
        data[index + 3] = (byte)(item.State.B * brightness / 255);
    }
    trace.Write((byte)2); trace.Write(brightness); trace.Write((byte)(enabled ? 1 : 0)); trace.Write(data); record++;
}
void Tick(uint now) { trace.Write((byte)3); trace.Write(now); record++; }
Configuration Parse(params Rule[] rules)
{
    var json = JsonSerializer.Serialize(new {
        version = 1, nextCreationOrder = 10,
        rules = rules.Select(r => new {
            id = r.Id, name = r.Name, creationOrder = r.CreationOrder,
            source = new { type = "windows.audio.defaultPlayback", role = "multimedia" },
            match = new { endpointId = r.EndpointId },
            display = r.Display.Select(d => new { row = d.Key.Row, column = d.Key.Column,
                @override = d.State.Override, color = $"#{d.State.R:X2}{d.State.G:X2}{d.State.B:X2}" })
        })
    });
    File.WriteAllText(Path.Combine(output, $"config-{scenarios}-{record}.json"), json);
    return ConfigReader.Parse(json);
}
byte[][] Encode(RuleEngine engine, DisplayItem[] expected)
{
    var snapshot = engine.Snapshot();
    Check(snapshot.SequenceEqual(expected.OrderBy(x => x.Key.Row).ThenBy(x => x.Key.Column)), "规则最终结果不符合预期");
    return SnapshotReportCodec.Encode(session, ++transaction, ++version, snapshot);
}
void Apply(RuleEngine engine, DisplayItem[] expected)
{
    var reports = Encode(engine, expected);
    foreach (var packet in reports[..^1]) Packet(packet, 0);
    Frame(active); // BEGIN and DATA must not expose the new state.
    Packet(reports[^1], 1); active = expected; Frame(active);
}
Action<byte[], byte[]> Progress(byte phase, byte packets, byte receivedItems, byte totalItems, ulong bitmap, uint snapshotVersion) => (e, m) => {
    e[16] = phase; e[17] = packets; e[18] = receivedItems; e[19] = totalItems;
    BinaryPrimitives.WriteUInt64LittleEndian(e.AsSpan(20), bitmap);
    BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(28), snapshotVersion);
    Array.Fill(m, (byte)255, 16, 16);
};

Scenario("GET_INFO 能力与坐标元数据");
Packet(SnapshotReportCodec.GetInfo(), 0, (e, m) => {
    Array.Clear(e, 4, 28); e[4] = 1; e[5] = 6; e[6] = 21; e[7] = 108; e[8] = 2; e[9] = 54;
    BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(10), 5000); e[28] = 7; Array.Fill(m, (byte)255);
});
Scenario("JSON 多键与奇数分包；音频切换；黑色覆盖与稀疏释放");
var a = new Rule("a", "音响", 1, "speaker", red);
var b = new Rule("b", "耳机", 2, "headphones", whiteBlack);
var engine = new RuleEngine(Parse(b, a));
engine.UpdateAudio("speaker"); Apply(engine, red);
engine.UpdateAudio("headphones"); Apply(engine, whiteBlack);
Frame(active, 128); Frame(active, 0); Frame(active, 255, false); Frame(active);

Scenario("同时触发按创建顺序；重复通知保持；较旧规则最新触发优先");
b = b with { EndpointId = "speaker" };
engine = new RuleEngine(Parse(b, a)); engine.UpdateAudio("speaker");
Apply(engine, [.. whiteBlack, red[2]]);
a = a with { EndpointId = "aux" }; engine.Reload(Parse(b, a)); Apply(engine, whiteBlack);
a = a with { EndpointId = "speaker" }; engine.Reload(Parse(b, a)); Apply(engine, red);
engine.UpdateAudio("speaker"); Apply(engine, red);

Scenario("仅改颜色保留触发顺序；高优先级解除；移除后回退");
b = b with { Display = [Item(17, 0, 0, 255)] };
engine.Reload(Parse(b, a)); Apply(engine, red);
b = b with { Display = [Item(17, 123, 123, 123, false)], EndpointId = "aux" };
engine.Reload(Parse(b, a)); Apply(engine, red);
b = b with { EndpointId = "speaker" }; engine.Reload(Parse(b, a)); Apply(engine, [red[1], red[2]]);
engine.Reload(Parse(a)); Apply(engine, red);

Scenario("Unknown 保留；明确无设备提交空快照释放全部");
engine.UpdateAudio(null, false); Apply(engine, red);
engine.UpdateAudio(null); Apply(engine, []);

Scenario("108 LED / 54 DATA：乱序、重复、丢包查询、补发、丢 ACK 后查询");
var full = positions.Select((key, i) => new DisplayItem(key, new(true, (byte)i, (byte)(255 - i), (byte)(i * 2)))).ToArray();
engine = new RuleEngine(Parse(new Rule("full", "全键盘", 1, "speaker", full))); engine.UpdateAudio("speaker");
var reports = Encode(engine, full);
Check(reports.Length == 56, "108 项需要 54 数据包");
Packet(reports[0], 0);
// Drop packet 5, reverse all other data; repeat a packet without double counting.
for (var i = 54; i >= 1; i--) if (i != 6) Packet(reports[i], 0);
Packet(reports[1], 0); Frame(active);
var bitmap = ((1UL << 54) - 1) & ~(1UL << 5);
Packet(reports[^1], 7, Progress(1, 54, 106, 108, bitmap, version));
Packet(SnapshotReportCodec.GetStatus(session, transaction), 0, Progress(1, 54, 106, 108, bitmap, version));
Packet(reports[6], 0); Packet(reports[^1], 1); active = full; Frame(active);
Packet(SnapshotReportCodec.GetStatus(session, transaction), 1, Progress(2, 54, 108, 108, (1UL << 54) - 1, version));
Packet(reports[^1], 1); Frame(active, 128);
var appliedTransaction = transaction;

Scenario("缺包超时和冲突事务均保留旧画面；新事务恢复");
engine = new RuleEngine(Parse(a)); engine.UpdateAudio("speaker");
reports = Encode(engine, red); Packet(reports[0], 0); Packet(reports[1], 0);
Tick(5001); Packet(reports[^1], 9); Frame(active);
reports = Encode(engine, red); Packet(reports[0], 0); Packet(reports[1], 0);
var corrupted = (byte[])reports[1].Clone(); corrupted[21] ^= 1;
Packet(corrupted, 8); Packet(reports[^1], 10); Frame(active);
Apply(engine, red);

Scenario("旧事务拒绝；新会话切换后旧 DATA 拒绝；显式解除项");
Packet(SnapshotReportCodec.GetStatus(session, appliedTransaction), 6); Frame(active);
var stale = reports[1];
reports = SnapshotReportCodec.Encode(session + 1, 1, 1, [Item(17, 99, 99, 99, false), Item(18, 0, 0, 0)]);
Packet(reports[0], 0); Packet(stale, 6); Frame(active);
Packet(reports[1], 0); Packet(reports[^1], 1); active = [Item(18, 0, 0, 0)]; Frame(active);
Tick(100000); Frame(active);
Console.WriteLine($"生成完成：{scenarios} 组场景，{record} 条记录；等待真实 C 固件回放验证。");
