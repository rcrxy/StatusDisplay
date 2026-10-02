# Q6 Pro Host LED

Windows 结算状态规则，QMK 固件显示最终 RGB 覆盖。本项目已开始独立实现，不依赖 Smial。

- [最新开发规格](docs/开发方案.md)
- [协议边界与待冻结内容](docs/protocol.md)

## 当前完成范围

.NET 10 离线 CLI：严格 JSON 校验、配置原子保存、创建顺序维护检查、规则结算与重载、LED 项二进制编码。
尚未实现 HID 设备访问、固件、音频自动监听、文件自动热重载和开机启动。
render 使用明确传入的模拟设备 ID，不读取 Windows 实际音频状态。

```powershell
dotnet build src/HostLed/HostLed.csproj
dotnet run --project tests/HostLed.Tests
dotnet run --project src/HostLed -- validate config.example.json
dotnet run --project src/HostLed -- render config.example.json --endpoint REPLACE_WITH_SPEAKER_ENDPOINT_ID
dotnet run --project src/HostLed -- config-set config.example.json hostled.json
```

示例 Endpoint ID 是占位文本，必须用真实设备 ID 替换。离线校验通过不表示设备存在。
config-set 接收完整来源文件，先校验后替换目标；目标已存在时检查创建顺序历史。
错误返回退出码 1，命令用法错误返回 2。render 输出仅含 LED 项，没有事务头，不能直接作为 HID 报告。

## 配置约定

字段大小写精确匹配，拒绝未知字段、重复属性、无效颜色和无 LED 坐标。
creationOrder 唯一且小于 nextCreationOrder；后者是不可回退的创建序号高水位。
默认音频角色为 multimedia，匹配 Endpoint ID；同时触发按创建顺序，重复通知不重新触发。
规则创建顺序不依赖数组位置。示例中两个规则各控制 F13/F14：选中白色、另一个强制黑色。

## 数据依据

data/q6-pro-ansi.json 从本机 Keychron 固件工作树的实际 info.json 提取，记录源提交和路径，共 108 个 LED 坐标。
该元数据用于离线校验；实际设备与固件仍需握手和实机确认。
现有 1.md 为用户原始讨论记录，保留不变。
