# Q6 Pro Host LED

Windows 结算状态规则，QMK 固件显示最终 RGB 覆盖。本项目已开始独立实现，不依赖 Smial。

- [最新开发规格](docs/开发方案.md)
- [协议边界与待冻结内容](docs/protocol.md)
- [固件版本核对与迁移记录](docs/固件版本核对.md)

## 当前完成范围

.NET 10 离线 CLI：严格 JSON 校验、配置原子保存、创建顺序维护检查、规则结算与重载、LED 项二进制编码。
另提供 `SnapshotReportCodec` 完整 32 字节事务报文编码，已通过真实固件 C 代码的离线端到端回放；尚未接入 CLI 的 HID 发送。
Windows 尚未实现 HID 设备访问、音频自动监听、文件自动热重载和开机启动。
render 使用明确传入的模拟设备 ID，不读取 Windows 实际音频状态。

## 固件进度

2026-10-04：已同步官方 `wls_2025q1` 分支，固定到 `c8b2b237b4ef09765e48cbb5c1fdb0afd164289f`。
新的固件工作树为 `D:/Workspace/StatusDisplay/.firmware-stage/qmk-wls_2025q1`，Git 分支为 `hostled/wls-2025q1`；custom 位于其中的 `keyboards/keychron/q6_pro/ansi_encoder/keymaps/custom`。
采用新分支原始 via 键位和旋钮映射；新增 VIA 自定义通道、GET_INFO、快照事务、RAM 双缓冲和 RGB 覆盖层。
完整协议见 docs/protocol.md：每包 2 个 LED 项，最大 54 包；会话/事务隔离、缺包查询、重复包校验和 5 秒 staging 超时。
17 组 C 协议测试及 QMK 接入层替身测试已通过（GCC + ASan/UBSan），实际安装文件与受测文件哈希一致。
新增 8 组跨语言端到端场景，涵盖 121 次报文交互和 39 次完整 LED 画面检查；复现方式见 [模拟测试说明](tests/HostLed.EndToEnd/README.md)。
原方案曾成功生成 `D:/Workspace/KeyboardFirmware/keychron_q6_pro_ansi_encoder_custom.bin`（67,228 字节）。这是迁移前的产物。
新分支依赖已经按 gitlink 指定版本安装，ARM 编译和链接通过。新产物为 `.firmware-stage/qmk-wls_2025q1/keychron_q6_pro_ansi_encoder_custom.bin`，最终文件 67,856 字节；SHA256 及验证记录见版本核对文档。该分支已移除原先无条件引入动态消抖的代码，不再移植旧公共构建修复。
Launcher 官方最新固件是 V1.1.0，但此公开分支仍标注 `device_version: "1.0.1"`，尚未确认两者对应的源码关系。刷写保持暂停，不能把新分支编译结果称为官方 V1.1.0 加 Host LED。

在 WSL 激活 `/home/rcrxy/.local/share/hostled-qmk-venv/bin/activate` 后，从 `/mnt/d/Workspace/StatusDisplay/.firmware-stage/qmk-wls_2025q1` 执行：

```sh
qmk compile -j 8 -kb keychron/q6_pro/ansi_encoder -km custom
bash keyboards/keychron/q6_pro/ansi_encoder/keymaps/custom/tests/run.sh
```

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

data/q6-pro-ansi.json 已与新分支的实际 keyboard.json 逐项核对并更新来源提交，共 108 个 LED 坐标；坐标及 LED 排列顺序与原方案一致。
该元数据用于离线校验；实际设备与固件仍需握手和实机确认。
现有 1.md 为用户原始讨论记录，保留不变。
