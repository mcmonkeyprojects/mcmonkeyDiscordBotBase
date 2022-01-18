using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;
using DiscordBotBase.CommandHandlers;
using DiscordBotBase.Reactables;
using FreneticUtilities.FreneticToolkit;
using FreneticUtilities.FreneticExtensions;

namespace DiscordBotBase
{
    /// <summary>Discord bot primary handler class.</summary>
    public class DiscordBot
    {
        /// <summary>Configuration folder path.</summary>
        public const string CONFIG_FOLDER = "./config/";

        /// <summary>Bot token file path.</summary>
        public const string TOKEN_FILE = CONFIG_FOLDER + "token.txt";

        /// <summary>Configuration file path.</summary>
        public const string CONFIG_FILE = CONFIG_FOLDER + "config.fds";

        /// <summary>Bot token, read from config data.</summary>
        public static readonly string TOKEN = File.ReadAllText(TOKEN_FILE).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Replace(" ", "");

        /// <summary>The configuration file section.</summary>
        public FDSSection ConfigFile;

        /// <summary>Internal Discord API bot Client handler.</summary>
        public DiscordSocketClient Client;

        /// <summary>The internal config for what this bot should be doing.</summary>
        public DiscordBotConfig ClientConfig;

        /// <summary>A cache of messages sent previously on Discord.</summary>
        public DiscordMessageCache Cache;

        /// <summary>Lock object for config file saving/loading.</summary>
        public static LockObject ConfigSaveLock = new();

        /// <summary>Signaled when the bot is stopped.</summary>
        public ManualResetEvent StoppedEvent = new(false);

        /// <summary>Monitor object to help restart the bot as needed.</summary>
        public ConnectionMonitor BotMonitor;

        /// <summary>All valid user chat commands in a map of typable command name -> command method.</summary>
        public readonly Dictionary<string, Action<CommandData>> ChatCommands = new(1024);

        /// <summary>All valid slash commands in a map of typable command name -> command method.</summary>
        public readonly Dictionary<string, Action<SocketSlashCommand>> SlashCommands = new(1024);

        /// <summary>A set of <see cref="MessageBulker"/> instances you can optionally use via <see cref="GetBulker(ulong)"/>.</summary>
        public readonly ConcurrentDictionary<ulong, MessageBulker> GenericBulkers = new();

        /// <summary>Gets or create a <see cref="MessageBulker"/> for a given channel, with exactly one bulker for each channel.
        /// Mentions default to disabled, which can be changed by editing the returned instance. Note that all messages in any one bulker either allow mentions or don't, you can't choose per-message with a bulker.</summary>
        public MessageBulker GetBulker(IMessageChannel channel)
        {
            return GenericBulkers.GetOrCreate(channel.Id, () => new MessageBulker(this, channel));
        }

        /// <summary>Bot command response handler.</summary>
        /// <param name="message">The message received.</param>
        /// <param name="outputUnknowns">Whether to output "unknown command" messages.</param>
        /// <param name="wasMentioned">Whether the bot was mentioned to trigger this command.</param>
        /// <param name="altContent">Alternate message content, if the message is being autosent.</param>
        public void Respond(IUserMessage message, bool outputUnknowns, bool wasMentioned, string altContent = null)
        {
            UserCommands.LastRepliedMessage = 0;
            string messageText = altContent ?? message.Content;
            try
            {
                if (ClientConfig.CommandPrefix != null && messageText.StartsWith(ClientConfig.CommandPrefix))
                {
                    messageText = messageText[ClientConfig.CommandPrefix.Length..];
                }
                string[] messageDataSplit = messageText.Split(' ');
                StringBuilder resultBuilder = new(messageText.Length);
                List<string> argsCleaned = new();
                List<string> argsRaw = new();
                foreach (string originalArg in messageDataSplit)
                {
                    if (originalArg.Contains("<@") && originalArg.Contains('>'))
                    {
                        if (originalArg[2..^1].Replace("!", "") != Client.CurrentUser.Id.ToString())
                        {
                            argsRaw.Add(originalArg);
                        }
                        continue;
                    }
                    resultBuilder.Append(originalArg).Append(' ');
                    if (originalArg.Length > 0)
                    {
                        argsCleaned.Add(originalArg);
                        argsRaw.Add(originalArg);
                    }
                }
                if (argsCleaned.Count == 0)
                {
                    Console.WriteLine("Empty input, ignoring: " + message.Author.Username);
                    return;
                }
                string fullMessageCleaned = resultBuilder.ToString();
                Console.WriteLine("Found input from: (" + message.Author.Username + "), in channel: " + message.Channel.Name + ": " + fullMessageCleaned);
                string commandNameLowered = argsCleaned[0].ToLowerFast();
                argsCleaned.RemoveAt(0);
                argsRaw.RemoveAt(0);
                CommandData commandData = new() { Message = message, CleanedArguments = argsCleaned.ToArray(), RawArguments = argsRaw.ToArray(), WasBotMention = wasMentioned, Bot = this };
                if (ChatCommands.TryGetValue(commandNameLowered, out Action<CommandData> commandHandlerMethod))
                {
                    commandHandlerMethod.Invoke(commandData);
                }
                else if (outputUnknowns && ClientConfig.UnknownCommandMessage != null)
                {
                    message.Channel.SendMessageAsync(embed: UserCommands.GetErrorMessageEmbed("Unknown Command", ClientConfig.UnknownCommandMessage)).Wait();
                }
                else
                {
                    ClientConfig.UnknownCommandHandler?.Invoke(commandNameLowered, commandData);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error while handling command '{messageText}': {ex}");
            }
        }

        /// <summary>Saves the config file.</summary>
        public void SaveConfig()
        {
            lock (ConfigSaveLock)
            {
                ConfigFile.SaveToFile(CONFIG_FILE);
            }
        }

        /// <summary>Registers a command to a name and any number of aliases.</summary>
        public void RegisterCommand(Action<CommandData> command, params string[] names)
        {
            foreach (string name in names)
            {
                ChatCommands.Add(name.ToLowerFast(), command);
            }
        }

        /// <summary>Registers a slash command to a name and any number of aliases.</summary>
        public void RegisterSlashCommand(Action<SocketSlashCommand> command, params string[] names)
        {
            foreach (string name in names)
            {
                SlashCommands.Add(name.ToLowerFast(), command);
            }
        }

        /// <summary>Shuts the bot down entirely.</summary>
        public void Shutdown()
        {
            ClientConfig.OnShutdown?.Invoke();
            Client.StopAsync().Wait();
            Client.Dispose();
            StoppedEvent.Set();
        }

        /// <summary>Initializes the bot object, connects, and runs the active loop.</summary>
        public void InitAndRun(string[] args)
        {
            Console.WriteLine("Preparing...");
            BotMonitor = new ConnectionMonitor(this);
            if (File.Exists(CONFIG_FILE))
            {
                lock (ConfigSaveLock)
                {
                    ConfigFile = FDSUtility.ReadFile(CONFIG_FILE);
                }
            }
            Cache = new DiscordMessageCache(this, ConfigFile?.GetInt("discord_cache_size") ?? ClientConfig.CacheSize);
            Console.WriteLine("Loading Discord...");
            DiscordSocketConfig config = new()
            {
                MessageCacheSize = 50,
                AlwaysDownloadUsers = true,
                GatewayIntents = ClientConfig.GatewayIntents
            };
            //config.LogLevel = LogSeverity.Debug;
            Client = new DiscordSocketClient(config);
            /*Client.Log += (m) =>
            {
                Console.WriteLine(m.Severity + ": " + m.Source + ": " + m.Exception + ": "  + m.Message);
                return Task.CompletedTask;
            };*/
            ClientConfig.Initialize(this);
            Client.Ready += () =>
            {
                Console.WriteLine($"Bot declared ready...");
                if (BotMonitor.ShouldStopAllLogic())
                {
                    Console.WriteLine($"But bot is already dead.");
                    return Task.CompletedTask;
                }
                BotMonitor.ConnectedCurrently = true;
                if (BotMonitor.ConnectedOnce)
                {
                    Console.WriteLine($"But bot was connected before, so just refill the cache.");
                    if (ClientConfig.EnsureCaching)
                    {
                        Cache.Prefill();
                    }
                    return Task.CompletedTask;
                }
                Console.WriteLine($"Args: {args.Length}");
                if (args.Length > 0 && ulong.TryParse(args[0], out ulong argument1))
                {
                    try
                    {
                        ISocketMessageChannel channelToNotify = Client.GetChannel(argument1) as ISocketMessageChannel;
                        Console.WriteLine($"Restarted as per request in channel: {channelToNotify.Name}");
                        channelToNotify.SendMessageAsync(embed: UserCommands.GetGenericPositiveMessageEmbed("Restarted", "Connected and ready!")).Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error with sending restart message: {ex}");
                    }
                }
                BotMonitor.ConnectedOnce = true;
                Console.WriteLine($"Filling cache...");
                if (ClientConfig.EnsureCaching)
                {
                    Cache.Prefill();
                }
                return Task.CompletedTask;
            };
            Client.ReactionAdded += (message, channel, reaction) =>
            {
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                message.GetOrDownloadAsync().Wait();
                if (reaction.UserId == Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                ReactionsHandler.CheckReactables();
                if (!reaction.User.IsSpecified)
                {
                    return Task.CompletedTask;
                }
                if (reaction.User.Value.IsBot || reaction.User.Value.IsWebhook)
                {
                    return Task.CompletedTask;
                }
                ReactionsHandler.TestReaction(message.Id, reaction);
                return Task.CompletedTask;
            };
            Client.MessageReceived += (socketMessage) =>
            {
                if (socketMessage is not IUserMessage message)
                {
                    return Task.CompletedTask;
                }
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (!ClientConfig.AllowDMs && message.Channel is not SocketGuildChannel)
                {
                    return Task.CompletedTask;
                }
                Cache.CacheMessage(socketMessage);
                if (message.Author.Id == Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                ReactionsHandler.CheckReactables();
                BotMonitor.LoopsSilent = 0;
                if (message.Author.IsBot || message.Author.IsWebhook)
                {
                    return Task.CompletedTask;
                }
                if (!ClientConfig.ShouldPayAttentionToMessage(message))
                {
                    Console.WriteLine($"Refused message from ({message.Author.Username}): (Non-whitelisted Channel: {message.Channel.Name}): {message.Content}");
                    return Task.CompletedTask;
                }
                bool mentionedMe = socketMessage.Content.Contains($"<@{Client.CurrentUser.Id}>") || socketMessage.Content.Contains($"<@!{Client.CurrentUser.Id}>");
                Console.WriteLine($"Parsing message from ({message.Author.Username}), in channel: {message.Channel.Name}: {message.Content}");
                if (mentionedMe || (ClientConfig.CommandPrefix != null && message.Content.StartsWith(ClientConfig.CommandPrefix)))
                {
                    try
                    {
                        Respond(message, mentionedMe, mentionedMe);
                    }
                    catch (Exception ex)
                    {
                        if (ex is ThreadAbortException)
                        {
                            throw;
                        }
                        Console.WriteLine($"Error handling command: {ex}");
                    }
                }
                ClientConfig.OtherMessageHandling?.Invoke(message);
                return Task.CompletedTask;
            };
            Client.MessageUpdated += (cache, socketMessage, channel) =>
            {
                if (socketMessage is not IUserMessage message)
                {
                    return Task.CompletedTask;
                }
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                if (!ClientConfig.AllowDMs && message.Channel is not SocketGuildChannel)
                {
                    return Task.CompletedTask;
                }
                Cache.CacheMessage(socketMessage);
                return Task.CompletedTask;
            };
            Client.SlashCommandExecuted += (command) =>
            {
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                Console.WriteLine($"Found slash command '{command.CommandName}' from user id {command.User.Id} in channel id {command.Channel.Id}");
                if (!ClientConfig.AllowSlashCommandsInDM && command.Channel is not IGuildChannel)
                {
                    command.RespondAsync("Commands don't work there.", ephemeral: true);
                    return Task.CompletedTask;
                }
                if (!SlashCommands.TryGetValue(command.CommandName.ToLowerFast(), out Action<SocketSlashCommand> cmd))
                {
                    command.RespondAsync("Unknown command.", ephemeral: true);
                }
                try
                {
                    cmd(command);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Slash command '{command.CommandName}' execution errored: {ex}");
                }
                return Task.CompletedTask;
            };
            Console.WriteLine("Logging in to Discord...");
            Client.LoginAsync(TokenType.Bot, TOKEN).Wait();
            Console.WriteLine("Connecting to Discord...");
            Client.StartAsync().Wait();
            Console.WriteLine("Running Discord!");
            Console.WriteLine("Starting monitor...");
            BotMonitor.StartMonitorLoop();
            StoppedEvent.WaitOne();
        }
    }
}
