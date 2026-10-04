# Host LED Protocol V1

2026-10-02：固件侧格式冻结并实现；Windows CLI 当前仅编码 LED 项，不发送完整 HID 报告。
目标：Q6 Pro ANSI Encoder，6×21 矩阵，108 个 LED；其他板型不能直接使用。

## 传输与路由

USB Raw HID 32 字节报告，usage page 0xFF60 / usage 0x61；Host 库所需 Report ID 0 在报告外。
使用 VIA 标准 custom-value 入口：byte 0 为 0x07(set) 或 0x08(get)，byte 1 为自定义 channel 0xAC。
0xAC **不是顶层命令**。固件 `via_custom_value_command_kb` 只改写回复，`via.c` 负责一次 raw_hid_send。
不改变 `raw_hid_receive`、标准 RGB 通道或 Keychron 0xAA/0xAB 路由。
所有多字节整数均为 little-endian，所有未使用字节必须填零。

## 公共头

| 偏移 | 请求 | 回复 |
|---|---|---|
| 0 | VIA 命令 | 原样回显 |
| 1 | 0xAC | 原样回显 |
| 2 | 子命令 | 原样回显 |
| 3 | 协议版本 1 | 结果码 |
| 4..11 | session:uint64 | 原样回显（INFO 例外） |
| 12..15 | transaction:uint32 | 原样回显（INFO 例外） |

session 为进程启动时随机生成的非零 uint64；同一会话 transaction 从 1 单调递增。
事务序号即将溢出时启用新 session。协议仅支持一个 Host 写入者，不支持多进程抢占。
USB 请求逐条发送并等待对应回复。核对 VIA/channel/subcommand/session/transaction；DATA 还需核对 sequence。
切换会话前排空旧请求/回复，不能重发先前会话 BEGIN；session 防止旧 DATA 被误用于新事务，并非鉴权。

## 子命令

| 子命令 | 值 | VIA 命令 | 请求偏移 16 起 |
|---|---|---|---|
| GET_INFO | 1 | 0x08 | 4..31 全零 |
| SNAPSHOT_BEGIN | 2 | 0x07 | 16..19 snapshotVersion:u32，20 packetCount:u8，21 itemCount:u8，22..31 零 |
| SNAPSHOT_DATA | 3 | 0x07 | 16 sequence:u8，17 count:u8，18..29 两个 LED 项，30..31 零 |
| SNAPSHOT_COMMIT | 4 | 0x07 | 16..31 零 |
| SNAPSHOT_STATUS | 5 | 0x08 | 16..31 零 |

snapshotVersion 非零，描述 Host 逻辑状态，可在新事务重试中复用，不在固件中按大小拒绝。
itemCount 为 0..108，packetCount 必须等于 ceil(itemCount/2)，最大 54；零项为零数据包。
sequence 从 0 开始，不得超过 packetCount。非末包 count=2，末包 count 为剩余项数。
不足两项的剩余 6 字节填零，不允许空 DATA。

## LED 项

6 字节：row:u8, column:u8, flags:u8, r:u8, g:u8, b:u8。
flags=1 覆盖，黑色也是覆盖；flags=0 解除覆盖，此时 RGB 必须全零。
其他 flags 值拒绝。无 LED 的位置、越界位置、同包/跨包重复目标 LED 均拒绝。
Host 结算后通常省略解除项，按 row/column 排序；Firmware 允许 DATA 乱序抵达。
BEGIN 清空 staging，COMMIT 替换全部 active；旧快照有而新快照没有的位置自动解除。

## 结果码 byte 3

| 值 | 名称 | 含义 |
|---|---|---|
| 0 | OK | 请求成功（不一定已经提交） |
| 1 | APPLIED | 查询的事务已经替换 active |
| 2 | BAD_LENGTH | 报告长度错误 |
| 3 | BAD_VERSION | 协议版本错误 |
| 4 | BAD_COMMAND | 未知子命令或 get/set 组合错误 |
| 5 | BAD_ARGUMENT | 数量、坐标、flags、保留字节等错误 |
| 6 | WRONG_TRANSACTION | 事务不存在或序号已过时 |
| 7 | MISSING | 尚未收齐数据，可补发 |
| 8 | CONFLICT | 重复包异内容、重复 LED 或 BEGIN 参数冲突 |
| 9 | EXPIRED | staging 无进展 5000ms 超时 |
| 10 | INVALID | 事务已拒绝，必须用新 transaction 重建 |

DATA 回复 16/17 回显 sequence/count，18..31 清零；状态和缺包位图通过 STATUS 查询。
BEGIN、COMMIT、STATUS 回复的 16..31 采用下表。

| 偏移 | 内容 |
|---|---|
| 16 | phase：0 unknown/idle、1 receiving、2 committed、3 rejected、4 expired |
| 17 | packetCount |
| 18 | receivedItemCount（重复包不累计） |
| 19 | expectedItemCount |
| 20..27 | receivedBitmap:u64，bit n 为包 n 已收到 |
| 28..31 | 该事务 snapshotVersion:u32 |

不认识的事务以上字段全零。最近一次 APPLIED 身份即使在下一次 BEGIN 后仍可查询，直到另一事务提交替换。
错误请求也可能带当前状态信息；以结果码为准，不能把错误回复视为成功。

## GET_INFO 成功回复

0..2 回显，3=OK；其余：

| 偏移 | 内容 |
|---|---|
| 4 | protocolVersion=1 |
| 5/6/7 | rows=6 / columns=21 / LED count=108 |
| 8/9 | itemsPerPacket=2 / maxPackets=54 |
| 10..11 | stagingTimeoutMs:u16=5000 |
| 12..19 | active session:u64，尚未提交为零 |
| 20..23 | active transaction:u32 |
| 24..27 | active snapshotVersion:u32 |
| 28 | capabilities=7：bit0 完整快照、bit1 亮度缩放、bit2 断连保持 |
| 29..31 | 零 |

## 事务状态机

相同 BEGIN 且参数一致，返回已有进度，不清空数据，也不延长无进展超时。
同 ID BEGIN 参数冲突拒绝 staging；已提交事务的冲突不会改变 active。
新事务可替代 staging。同一 session 的旧 transaction 不能覆盖较新的 staging/active。
每包完整校验后才修改 staging；非法 DATA 将该事务置为 rejected。
重复 DATA 必须 12 字节项区完全一致，相同则仅 ACK；不同则拒绝该事务。
超时只丢弃 staging，active 永不因超时而清除。STATUS、重复 BEGIN、重复 DATA 不续命。
COMMIT 缺包返回 MISSING 和位图；补发缺包后重试。
COMMIT 成功后重复 COMMIT/STATUS 返回 APPLIED；已提交后的 DATA 不再接受，Host 应查询 STATUS。
Host 最多做 3 轮恢复，失败后退避并以新事务重发最新 Desired；不能因 Desired 未变化停止恢复。

## 提交与 RGB 帧

COMMIT 在 QMK 主循环中完整复制 staging 到 active；同一主循环串行处理 HID 和 RGB，不从 ISR 修改缓冲。
**APPLIED 表示 active RAM 已替换，不承诺物理 LED 刷新已经完成。**
advanced-user 回调只在最后一个 LED 范围，对全体覆盖键统一绘制一次；不在各分块分别覆盖。
因此下一次完整帧只使用一份 active，且覆盖晚于普通灯效与键盘 basic indicators。
RGB 关闭或休眠时不强制绘制，但仍可提交和查询；重新开启后正常绘制保留状态。
最终颜色为 channel * rgb_matrix_get_val() / 255，不写 EEPROM。

## 固定报文示例

GET_INFO：`08 AC 01 01` 后跟 28 个零。
BEGIN session=1, transaction=1, version=1, 一项：
`07 AC 02 01 01 00 00 00 00 00 00 00 01 00 00 00 01 00 00 00 01 01 00 00 00 00 00 00 00 00 00 00`
DATA 包 0，[0,17] 红色：
`07 AC 03 01 01 00 00 00 00 00 00 00 01 00 00 00 00 01 00 11 01 FF 00 00 00 00 00 00 00 00 00 00`
COMMIT：
`07 AC 04 01 01 00 00 00 00 00 00 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00`

该文档同步复制到固件 custom/protocol.md。双方修改协议时必须同步版本和测试。
