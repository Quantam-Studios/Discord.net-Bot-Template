using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using DotNetEnv;
using Performance;

public static class Bot
{
    public static readonly DiscordSocketClient Client = new(new DiscordSocketConfig());

    private static InteractionService Service;

    private static readonly string Token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");

    private static Timer timer;

    public static async Task Main()
    {         
        if (Token is null)
        {
            throw new ArgumentException("Discord bot token not set properly.");
        }

        Client.Ready += Ready;
        Client.Log += Log;

        await Client.LoginAsync(TokenType.Bot, Token);
        await Client.StartAsync();

        await Task.Delay(Timeout.Infinite);
    }

    private static async Task Ready()
    {
        try
        {
            Service = new(Client, new InteractionServiceConfig
            {
                UseCompiledLambda = true,
                ThrowOnError = true
            });

            await Service.AddModulesAsync(Assembly.GetEntryAssembly(), null);
            await Service.RegisterCommandsGloballyAsync();

            Client.InteractionCreated += InteractionCreated;
            Service.SlashCommandExecuted += SlashCommandResulted;

            var cpuUsage = await Stats.GetCpuUsageForProcess();
            Console.WriteLine("CPU at Ready: " + cpuUsage.ToString() + "%");
            var ramUsage = Stats.GetRamUsageForProcess();
            Console.WriteLine("RAM at Ready: " + ramUsage.ToString() + "%");

            Client.Ready -= Ready;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        string[] statuses = { "Online!", "Status 2!", "Status 3!" };
        int index = 0;

        _ = Task.Run(() =>
        {
            // Status
            timer = new Timer(async x =>
            {
                try
                {
                    if (Client.ConnectionState == ConnectionState.Connected)
                    {
                        await Client.SetCustomStatusAsync(statuses[index]);
                        index = (index + 1) % statuses.Length;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error setting status: {ex.Message} | {statuses[index]}");
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(16));
        });
    }

    private static async Task InteractionCreated(SocketInteraction interaction)
    {
        try
        {
            SocketInteractionContext ctx = new(Client, interaction);
            await Service.ExecuteCommandAsync(ctx, null);
        }
        catch
        {
            if (interaction.Type == InteractionType.ApplicationCommand)
            {
                await interaction.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
            }
        }
    }

    private static async Task SlashCommandResulted(SlashCommandInfo info, IInteractionContext ctx, IResult res)
    {
        if (!res.IsSuccess)
        {
            switch (res.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    if (ctx.Interaction.HasResponded)
                    {
                        await ctx.Interaction.FollowupAsync($"❌ Something went wrong:\n- {res.ErrorReason}", ephemeral: true);
                    }
                    else
                    {
                        await ctx.Interaction.RespondAsync($"❌ Something went wrong:\n- {res.ErrorReason}", ephemeral: true);
                    }
                    break;
                case InteractionCommandError.UnknownCommand:
                    if (ctx.Interaction.HasResponded)
                    {
                        await ctx.Interaction.FollowupAsync("❌ Unknown command\n- Try refreshing your Discord client.", ephemeral: true);
                    }
                    else
                    {
                        await ctx.Interaction.RespondAsync("❌ Unknown command\n- Try refreshing your Discord client.", ephemeral: true);
                    }
                    break;
                case InteractionCommandError.BadArgs:
                    if (ctx.Interaction.HasResponded)
                    {
                        await ctx.Interaction.FollowupAsync("❌ Invalid number or arguments.", ephemeral: true);
                    }
                    else
                    {
                        await ctx.Interaction.RespondAsync("❌ Invalid number or arguments.", ephemeral: true);
                    }
                    break;
                case InteractionCommandError.Exception:
                    await ctx.Interaction.FollowupAsync($"❌ Something went wrong...\n- Try again later.", ephemeral: true);

                    var executionResult = (ExecuteResult)res;
                    Console.WriteLine($"Error: {executionResult.Exception}");

                    break;
                case InteractionCommandError.Unsuccessful:
                    await ctx.Interaction.FollowupAsync("❌ Command could not be executed.\n- Try again later.", ephemeral: true);
                    break;
                default:
                    await ctx.Interaction.FollowupAsync("❌ Command could not be executed.\n- Try again later.", ephemeral: true);
                    break;
            }
        }
        else
        {
            var cpuUsage = await Stats.GetCpuUsageForProcess();
            var ramUsage = Stats.GetRamUsageForProcess();
            string location = (ctx.Interaction.GuildId == null) ? "a DM" : (Client.GetGuild((ulong)ctx.Interaction.GuildId) == null ? "User Install" : Client.GetGuild((ulong)ctx.Interaction.GuildId).ToString());
            var commandName = info.IsTopLevelCommand ? $"/{info.Name}" : $"/{info.Module.SlashGroupName} {info.Name}";
            Console.WriteLine($"{DateTime.Now:dd/MM. H:mm:ss} | {Stats.FormatPerformance(cpuUsage, ramUsage)} | Location: {location} | Command: {commandName}");
        }
    }

    private static Task Log(LogMessage logMessage)
    {
        Console.ForegroundColor = SeverityToConsoleColor(logMessage.Severity);
        Console.WriteLine($"{DateTime.Now:dd/MM. H:mm:ss} [{logMessage.Source}] {logMessage.Message}");
        Console.ResetColor();

        return Task.CompletedTask;
    }

    private static ConsoleColor SeverityToConsoleColor(LogSeverity severity)
    {
        return severity switch
        {
            LogSeverity.Critical => ConsoleColor.Red,
            LogSeverity.Debug => ConsoleColor.Blue,
            LogSeverity.Error => ConsoleColor.Yellow,
            LogSeverity.Info => ConsoleColor.Cyan,
            LogSeverity.Verbose => ConsoleColor.Green,
            LogSeverity.Warning => ConsoleColor.Magenta,
            _ => ConsoleColor.White,
        };
    }
}