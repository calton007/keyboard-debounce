# 原 C# 行为基准

`csharp-filter-v0.2.0.json` 在迁移期间从提交 `3124d1a7e503a99572bcf98ba6e32f0bb65dc760` 的原始 `DebounceEngine.cs`、`KeyboardEventFilter.cs`、`AppSettings.cs` 生成。

六条流分别使用敏感度 0.5、1、1.5、2、2.75、3，每条 900 个事件。C# Random 种子为 21000–21005；包含修饰键、注入标记、Extended、按下/释放、暂停、游戏模式、忽略切换、运行态重置及 DWORD 时间回绕。

每个事件记录原过滤决策与 VK 学习快照；不比较墙钟时间和显示文案。数据全部由测试程序合成，不含真实输入序列。

Rust `tests/parity.rs` 重放并逐项比较。保留此文件作为独立行为基准，不能用待测试的 Rust 实现重新生成期望值。正常构建和验证不需要 .NET。
