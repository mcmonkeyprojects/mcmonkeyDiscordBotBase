using System;
using System.Collections.Generic;
using System.Text;
using Discord.WebSocket;

namespace DiscordBotBase
{
    /// <summary>
    /// Common configurable options for a Discord bot.
    /// </summary>
    public class DiscordBotConfig
    {
        /// <summary>
        /// The message to display when an unknown command is input (if any).
        /// </summary>
        public string UnknownCommandMessage = "Unknown command. Consider the __**help**__ command?";

        /// <summary>
        /// An alternate handler for unknown commands (eg special info output) (if any).
        /// </summary>
        public Action<string, List<string>, SocketMessage> UnknownCommandHandler = null;

        /// <summary>
        /// A method that will return a bool indicating whether the client should respond to commands in the channel given in the parameter.
        /// </summary>
        public Func<ISocketMessageChannel, bool> ShouldPayAttentionToChannel = null;

        /// <summary>
        /// A method to run to initialize the bot.
        /// </summary>
        public Action<DiscordBot> Initialize = null;
    }
}
