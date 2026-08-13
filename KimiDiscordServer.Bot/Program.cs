using Discord;
using Discord.WebSocket;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

builder.Services.AddSingleton(new HttpClient());
builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds
        | GatewayIntents.GuildMessages
        | GatewayIntents.DirectMessages
        | GatewayIntents.MessageContent,
    LogGatewayIntentWarnings = false
}));

builder.Services.AddSingleton<AttachmentContentService>();
builder.Services.AddSingleton<AiProviderFactory>();
builder.Services.AddSingleton<IAiChatClient, ClaudeAiClient>();
builder.Services.AddSingleton<IAiChatClient, KimiAiClient>();
builder.Services.AddSingleton<ChannelExecutionCoordinator>();
builder.Services.AddHostedService<DiscordBotService>();

await builder.Build().RunAsync();
