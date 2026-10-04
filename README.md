# Q6 Pro Host LED

Windows 结算状态规则，QMK 固件显示最终 RGB 覆盖。本项目已开始独立实现，不依赖 Smial。

- [最新开发规格](docs/开发方案.md)
- [协议边界与待冻结内容](docs/protocol.md)

## 当前完成范围

.NET 10 离线 CLI：严格 JSON 校验、配置原子保存、创建顺序维护检查、规则结算与重载、LED 项二进制编码。
另提供 `SnapshotReportCodec` 完整 32 字节事务报文编码，已通过真实固件 C 代码的离线端到端回放；尚未接入 CLI 的 HID 发送。
Windows 尚未实现 HID 设备访问、音频自动监听、文件自动热重载和开机启动。
render 使用明确传入的模拟设备 ID，不读取 Windows 实际音频状态。

## 固件进度

已在独立仓库 `D:/Workspace/KeyboardFirmware/keyboards/keychron/q6_pro/ansi_encoder/keymaps/custom` 创建固件 keymap。
原样保留 via 键位和旋钮映射；新增 VIA 自定义通道、GET_INFO、快照事务、RAM 双缓冲和 RGB 覆盖层。
完整协议见 docs/protocol.md：每包 2 个 LED 项，最大 54 包；会话/事务隔离、缺包查询、重复包校验和 5 秒 staging 超时。
17 组 C 协议测试及 QMK 接入层替身测试已通过（GCC + ASan/UBSan），实际安装文件与受测文件哈希一致。
新增 8 组跨语言端到端场景，涵盖 121 次报文交互和 39 次完整 LED 画面检查；复现方式见 [模拟测试说明](tests/HostLed.EndToEnd/README.md)。
2026-10-04：依赖安装后，已通过 WSL Ubuntu-24.04 / QMK CLI 1.2.0 / ARM GCC 13.2.1 完成目标固件编译和链接。
产物：`D:/Workspace/KeyboardFirmware/keychron_q6_pro_ansi_encoder_custom.bin`，最终文件 67,228 字节。尚未刷写或完成实机验证。
修复公共构建文件 `keyboards/keychron/common/common.mk`：仅在 `DEBOUNCE_TYPE` 为 `custom` 时引入动态消抖，与现有 `keychron_common.mk` 的条件一致；Q6 Pro 使用 QMK 默认消抖，避免重复实现和 EEPROM 配置冲突。未修改 EEPROM 布局、扫描代码、蓝牙、RGB 驱动或 VIA 实现。

在 WSL 激活 `/home/rcrxy/.local/share/hostled-qmk-venv/bin/activate` 后，从 `/mnt/d/Workspace/KeyboardFirmware` 执行：

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

data/q6-pro-ansi.json 从本机 Keychron 固件工作树的实际 info.json 提取，记录源提交和路径，共 108 个 LED 坐标。
该元数据用于离线校验；实际设备与固件仍需握手和实机确认。
现有 1.md 为用户原始讨论记录，保留不变。
