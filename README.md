# KimiDiscordServer

基于 C#/.NET 8 的 Discord Server Bot 后端示例。项目使用 Visual Studio 常见的 `解决方案(.sln) + Project` 结构，能够接收 Discord Channel/私信中的文本、图片和文档附件，并把请求转发给 Claude 或 Kimi，再将结果回复到 Discord。

## 目录结构

```text
KimiDiscordServer.sln
KimiDiscordServer.Bot/
├── Configuration/
├── Models/
├── Services/
├── appsettings.json
└── Program.cs
```

## 功能说明

- 连接 Discord Bot
- 支持多个 Channel / 多个用户并发请求
- 支持文本消息
- 支持图片附件（会以内嵌 base64 形式转给模型）
- 支持文本类文档附件（`.txt`、`.md`、`.json`、代码文件等）
- 支持 Claude / Kimi 两种模型提供方
- 通过每个 Channel 的顺序队列，避免同一频道回复交错

## 触发方式

- 在 `general` 频道里 `@Bot` 后发送请求
- 其他用户独立频道里可直接发消息，无需 `@Bot`
- 或直接使用前缀：
  - `/claude 你的问题`
  - `/kimi 你的问题`
  - `claude: 你的问题`
  - `kimi: 你的问题`
- 私信 Bot（如果 `AllowDirectMessages=true`）

## 配置

编辑 `KimiDiscordServer.Bot/appsettings.json`，填入：

- `Discord:Token`：Discord Bot Token
- `Ai:DefaultProvider`：默认模型提供方，`Claude` 或 `Kimi`
- `Ai:Claude:ApiKey`
- `Ai:Kimi:ApiKey`

可选项：

- `Discord:AllowedChannelIds`：限制允许处理的频道 ID
- `Discord:RequireBotMention`：是否启用 `general` 频道必须 `@Bot` 的规则
- `Discord:MentionOptionalChannelIds`：这些频道即使叫 `general` 也可直接发消息，无需 `@Bot`
- `Discord:MaxAttachmentBytes`：单文件最大读取大小
- `Discord:MaxTextDocumentBytes`：自动解析为纯文本的文档大小上限
- `Ai:Claude:RequestTimeoutSeconds`：Claude 请求超时时间（秒）
- `Ai:Kimi:RequestTimeoutSeconds`：Kimi 请求超时时间（秒）

如果同时配置了 `Discord:AllowedChannelIds`，那么免 `@Bot` 的频道也需要出现在 `AllowedChannelIds` 中，才会被处理。

建议把 `Discord Token` 和各模型 `API Key` 放到 **User Secrets** 或环境变量中，不要直接把真实密钥写入仓库里的 `appsettings.json`。

## 运行

```bash
dotnet restore
dotnet build
dotnet run --project KimiDiscordServer.Bot/KimiDiscordServer.Bot.csproj
```

## 注意

- 二进制文档（如 PDF、Word、Excel）当前会保留文件元信息，但不会自动抽取全文
- 如果要增强 PDF/Office 解析，可以在 `AttachmentContentService` 中继续扩展
- 请在 Discord Developer Portal 为 Bot 打开读取消息内容所需的权限（Message Content Intent）
- 如果模型响应较慢，可适当调大 `Ai:*:RequestTimeoutSeconds`，默认是 300 秒
