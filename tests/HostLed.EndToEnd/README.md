# 刷写前端到端模拟

在 StatusDisplay 根目录执行：

```powershell
./tests/HostLed.EndToEnd/run.ps1 -Distribution Ubuntu-24.04
```

需要 Windows .NET 10 SDK，以及指定 WSL 发行版中的 GCC 和 ASan/UBSan。无需连接键盘，不会发送 USB 数据、刷写固件或修改固件仓库。

## 实际运行链路

JSON → `ConfigReader` → `RuleEngine` → `SnapshotReportCodec` → 32 字节报文文件 → 真实 `host_led_protocol.c` / `host_led_qmk.c` → RGB 接口替身。

C# 先核对规则结果，再生成 BEGIN / DATA / COMMIT / STATUS / INFO 报文。C 回放程序调用固件的 VIA 自定义回调，核对返回码、身份回显、DATA 回显及查询进度；在提交前后核对全部 108 个 LED 的覆盖状态和 RGB 值。预期画面在场景中独立指定，不从规则引擎输出推导。

每帧先模拟普通灯效颜色，再调用分块及末块 RGB 回调：已释放的键必须保留普通灯效，强制黑色的键必须仍被覆盖。亮度、RGB 关闭和重新开启均有检查。板型映射取自新分支的 `keyboards/keychron/q6_pro/ansi_encoder/keyboard.json`，并与软件元数据及固件已有测试映射逐项核对。

默认固件工作树为 `.firmware-stage/qmk-wls_2025q1`。测试原方案时必须明确指定两项：

```powershell
./tests/HostLed.EndToEnd/run.ps1 -FirmwareRoot D:/Workspace/KeyboardFirmware -BoardDefinition keyboards/keychron/q6_pro/ansi_encoder/info.json
```

## 覆盖范围

1. INFO 能力、矩阵和包容量；协议文档固定字节向量及无效编码参数。
2. JSON 多键、奇数项末包、音频切换、黑色覆盖、快照未列出的旧键释放。
3. 同时触发按创建顺序、重复通知保持、较早创建的规则最新触发后优先。
4. 仅改颜色保留触发顺序、解除覆盖屏蔽低优先级、规则移除后回退。
5. Unknown 保留，明确无设备时提交空快照释放全部。
6. 108 LED / 54 DATA 满载、倒序、重复、丢包位图、补发及提交回复丢失后的查询。
7. staging 超时、重复包内容冲突保留旧画面，新事务恢复。
8. 旧事务拒绝、会话切换后旧 DATA 拒绝、显式解除项及 active 长期保留。

2026-10-04 实测：8 组场景、121 次报文交互、39 次完整画面检查通过，ASan/UBSan 未报告错误。原有 C# 测试与固件 C 测试继续独立维护。

## 产物和边界

输出位于忽略跟踪的 `.firmware-stage/end-to-end/`：`trace.bin`、输入配置、`scenarios.txt`（场景起始记录编号）、`replay` 和 `firmware-sources.json`（本次受测源文件 SHA256）。失败时脚本返回错误；回放错误报告记录编号。

恢复步骤由测试显式执行，尚无生产 HID 传输、ACK 处理或自动重试服务。模拟使用电脑上的 GCC，替换 QMK 硬件接口，不仿真 STM32、USB 驱动、完整 VIA 路由、无线连接、休眠电源管理或实际 LED 驱动。通过测试表示软件结算与固件解析/覆盖层在这些场景下保持一致，实机通信、时序和物理灯光仍待验证。
