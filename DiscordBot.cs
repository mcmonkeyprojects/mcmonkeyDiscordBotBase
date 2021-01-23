using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FreneticUtilities.FreneticDataSyntax;
using DiscordBotBase.CommandHandlers;
using DiscordBotBase.Reactables;

namespace DiscordBotBase
{
    /// <summary>
    /// Discord bot primary handler class.
    /// </summary>
    public class DiscordBot
    {
        /// <summary>
        /// Configuration folder path.
        /// </summary>
        public const string CONFIG_FOLDER = "./config/";

        /// <summary>
        /// Bot token file path.
        /// </summary>
        public const string TOKEN_FILE = CONFIG_FOLDER + "token.txt";

        /// <summary>
        /// Configuration file path.
        /// </summary>
        public const string CONFIG_FILE = CONFIG_FOLDER + "config.fds";

        /// <summary>
        /// Bot token, read from config data.
        /// </summary>
        public static readonly string TOKEN = File.ReadAllText(TOKEN_FILE).Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Replace(" ", "");

        /// <summary>
        /// The configuration file section.
        /// </summary>
        public FDSSection ConfigFile;

        /// <summary>
        /// Internal Discord API bot Client handler.
        /// </summary>
        public DiscordSocketClient Client;

        /// <summary>
        /// The internal config for what this bot should be doing.
        /// </summary>
        public DiscordBotConfig ClientConfig;

        /// <summary>
        /// Bot command response handler.
        /// </summary>
        /// <param name="message">The message received.</param>
        /// <param name="outputUnknowns">Whether to output "unknown command" messages.</param>
        /// <param name="wasMentioned">Whether the bot was mentioned to trigger this command.</param>
        /// <param name="altContent">Alternate message content, if the message is being autosent.</param>
        public void Respond(IUserMessage message, bool outputUnknowns, bool wasMentioned, string altContent = null)
        {
            string messageText = altContent ?? message.Content;
            if (ClientConfig.CommandPrefix != null && messageText.StartsWith(ClientConfig.CommandPrefix))
            {
                messageText = messageText[ClientConfig.CommandPrefix.Length..];
            }
            string[] messageDataSplit = messageText.Split(' ');
            StringBuilder resultBuilder = new StringBuilder(messageText.Length);
            List<string> argsCleaned = new List<string>();
            List<string> argsRaw = new List<string>();
            foreach (string originalArg in messageDataSplit)
            {
                if (originalArg.Contains("<@") && originalArg.Contains(">"))
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
            string commandNameLowered = argsCleaned[0].ToLowerInvariant();
            argsCleaned.RemoveAt(0);
            argsRaw.RemoveAt(0);
            CommandData commandData = new CommandData() { Message = message, CleanedArguments = argsCleaned.ToArray(), RawArguments = argsRaw.ToArray(), WasBotMention = wasMentioned };
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

        /// <summary>
        /// All valid user commands in a map of typable command name -> command method.
        /// </summary>
        public readonly Dictionary<string, Action<CommandData>> ChatCommands = new Dictionary<string, Action<CommandData>>(1024);
        
        /// <summary>
        /// Saves the config file.
        /// </summary>
        public void SaveConfig()
        {
            lock (ConfigSaveLock)
            {
                ConfigFile.SaveToFile(CONFIG_FILE);
            }
        }

        /// <summary>
        /// Lock object for config file saving/loading.
        /// </summary>
        public static Object ConfigSaveLock = new Object();

        /// <summary>
        /// Registers a command to a name and any number of aliases.
        /// </summary>
        public void RegisterCommand(Action<CommandData> command, params string[] names)
        {
            foreach (string name in names)
            {
                ChatCommands.Add(name, command);
            }
        }

        /// <summary>
        /// Shuts the bot down entirely.
        /// </summary>
        public void Shutdown()
        {
            ClientConfig.OnShutdown?.Invoke();
            Client.StopAsync().Wait();
            Client.Dispose();
            StoppedEvent.Set();
        }

        /// <summary>
        /// Signaled when the bot is stopped.
        /// </summary>
        public ManualResetEvent StoppedEvent = new ManualResetEvent(false);

        /// <summary>
        /// Monitor object to help restart the bot as needed.
        /// </summary>
        public ConnectionMonitor BotMonitor;

        /// <summary>
        /// Prefills cache data (if enable by <see cref="ClientConfig"/>).
        /// </summary>
        public void PrefillCache()
        {
            if (!ClientConfig.EnsureCaching)
            {
                return;
            }
            foreach (SocketGuild guild in Client.Guilds)
            {
                foreach (SocketTextChannel channel in guild.TextChannels)
                {
                    if (BotMonitor.ShouldStopAllLogic())
                    {
                        return;
                    }
                    try
                    {
                        channel.GetMessagesAsync(100).ForEachAwaitAsync(async col => await Task.Delay(100));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error while prefilling cache: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// Initializes the bot object, connects, and runs the active loop.
        /// </summary>
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
            Console.WriteLine("Loading Discord...");
            DiscordSocketConfig config = new DiscordSocketConfig
            {
                MessageCacheSize = ConfigFile.GetInt("discord_cache_size", ClientConfig.CacheSize).Value,
                AlwaysDownloadUsers = true
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
                if (BotMonitor.ShouldStopAllLogic())
                {
                    return Task.CompletedTask;
                }
                BotMonitor.ConnectedCurrently = true;
                if (BotMonitor.ConnectedOnce)
                {
                    PrefillCache();
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
                PrefillCache();
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
                if (message.Author.Id == Client.CurrentUser.Id)
                {
                    return Task.CompletedTask;
                }
                if (!ClientConfig.AllowDMs && message.Channel is not SocketGuildChannel)
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
                bool mentionedMe = socketMessage.MentionedUsers.Any((su) => su.Id == Client.CurrentUser.Id);
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
