using Discord;
using Discord.WebSocket;
using KimiDiscordServer.Bot.Configuration;
using KimiDiscordServer.Bot.Services;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.SectionName));
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));

builder.Services.AddHttpClient("DiscordAttachments");
builder.Services.AddHttpClient("Claude", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value.Claude;
    client.Timeout = BuildTimeout(options.RequestTimeoutSeconds);
});
builder.Services.AddHttpClient("Kimi", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AiOptions>>().Value.Kimi;
    client.Timeout = BuildTimeout(options.RequestTimeoutSeconds);
});
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

static TimeSpan BuildTimeout(int timeoutSeconds)
{
    const int defaultTimeoutSeconds = 300;
    return TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : defaultTimeoutSeconds);
}
