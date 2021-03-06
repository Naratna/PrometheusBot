using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using PrometheusBot.Commands;
using PrometheusBot.Services;
using PrometheusBot.Services.Audio;
using PrometheusBot.Services.MessageHistory;
using PrometheusBot.Services.Music;
using PrometheusBot.Services.NonStandardCommands;
using PrometheusBot.Services.Settings;
using Victoria;

namespace PrometheusBot
{
    class Program
    {
        public static string Directory { get; } = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        static async Task Main(string[] args)
        {
            LocalSettingsService localSettings = LoadSettings();

            string settingsPath = Path.Combine(Directory, "settings.json");
            SettingsService settings = new(settingsPath, localSettings.ConnectionString);

            DiscordSocketConfig config = new()
            {
                MessageCacheSize = 100
            };
            DiscordSocketClient client = new(config);

            MessageHistoryService messageHistory = new();

            client.Log += Log;
            client.MessageUpdated += messageHistory.AddAsync;
            client.MessageDeleted += messageHistory.AddDeletedAsync;

            await client.LoginAsync(TokenType.Bot, localSettings.DiscordToken);


            CommandServiceConfig commandsConfig = new()
            {
                CaseSensitiveCommands = false,
                DefaultRunMode = RunMode.Async,
                IgnoreExtraArgs = false
            };
            CommandService commands = new(commandsConfig);
            NonStandardCommandService nonStandardCommands = new(RunMode.Async);

            var services = GetServiceProvider(client, commands, nonStandardCommands, localSettings, messageHistory, settings);
            CommandHandler commandHandler = new(services);
            await commandHandler.InstallCommandsAsync();

            async Task OnReady()
            {
                var lavaNode = services.GetService<LavaNode>();
                if (!lavaNode.IsConnected)
                {
                    await lavaNode.ConnectAsync();
                }
            }

            client.Ready += OnReady;
            await client.StartAsync();

            try
            {
                await Task.Delay(-1);
            }
            finally
            {
                await client.LogoutAsync();
            }
        }

        public static Task Log(LogMessage message)
        {
            Console.WriteLine(message);
            return Task.CompletedTask;
        }

        private static LocalSettingsService LoadSettings()
        {
            string path = Path.Join(Directory, "LocalSettings.json");
            LocalSettingsService settings = new(path);
            settings.Load();
            if (!string.IsNullOrWhiteSpace(settings.DiscordToken))
            {
                settings.Save();
                return settings;
            }

            //Token was not found, presumably first time running program. Prompt user for token
            do
            {
                Console.Write("Bot token: ");
                settings.DiscordToken = Console.ReadLine();
            } while (string.IsNullOrWhiteSpace(settings.DiscordToken));
            Console.WriteLine($"Saving settings. They can be changed later from file '{settings.Path}'");
            if (!settings.Save())
            {
                Console.WriteLine("Failed to write new settings. You will be promted for a token again next time the program runs");
            }
            return settings;
        }
        private static IServiceProvider GetServiceProvider(
            DiscordSocketClient client,
            CommandService commands,
            NonStandardCommandService nonStandardCommands,
            LocalSettingsService localSettings,
            MessageHistoryService messageHistory,
            SettingsService settings)
        {
            ReactionsService reactions = new(settings);
            AudioService audioService = new();
            Random random = new();

            var services = new ServiceCollection()
                .AddSingleton(client)
                .AddSingleton(commands)
                .AddSingleton(nonStandardCommands)
                .AddSingleton(localSettings)
                .AddSingleton(messageHistory)
                .AddSingleton(settings)
                .AddSingleton(reactions)
                .AddSingleton(random)
                .AddSingleton(audioService)
                .AddSingleton<MusicService>()
                .AddLavaNode(x =>
               {
                   x.Hostname = "localhost";
                   x.Port = 2333;
                   x.Authorization = "youshallnotpass";
               });

            return services.BuildServiceProvider();
        }
    }
}
