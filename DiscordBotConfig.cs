using System;
using System.Collections.Generic;
using System.Text;
using Discord;

namespace DiscordBotBase
{
    /// <summary>Common configurable options for a Discord bot.</summary>
    public class DiscordBotConfig
    {
        /// <summary>The message to display when an unknown command is input (if any).</summary>
        public string UnknownCommandMessage = "Unknown command. Consider the __**help**__ command?";

        /// <summary>An alternate handler for unknown commands (eg special info output) (if any).</summary>
        public Action<string, CommandData> UnknownCommandHandler = null;

        /// <summary>
        /// A method that will return a bool indicating whether the client should respond to commands in the message given in the parameter (usually based on the channel details).
        /// </summary>
        public Func<IUserMessage, bool> ShouldPayAttentionToMessage = null;

        /// <summary>A method to run to initialize the bot.</summary>
        public Action<DiscordBot> Initialize = null;

        /// <summary>Extra message handling, after command handling is done.</summary>
        public Action<IUserMessage> OtherMessageHandling = null;

        /// <summary>Logic to run when the bot's about to shutdown.</summary>
        public Action OnShutdown = null;

        /// <summary>The command prefix to use, if any. (If null, requires a direct ping).</summary>
        public string CommandPrefix = "!";

        /// <summary>
        /// How many messages per channel to cache by default.
        /// This value can be overriden by the "discord_cache_size" config.fds option.
        /// </summary>
        public int CacheSize = 256;

        /// <summary>Whether to automatically forcibly pre-fill the cache at startup.</summary>
        public bool EnsureCaching = false;

        /// <summary>Whether to allow direct messages (if not, only guild messages are allowed).</summary>
        public bool AllowDMs = false;
    }
}
