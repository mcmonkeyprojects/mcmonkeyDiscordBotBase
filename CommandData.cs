using System;
using System.Collections.Generic;
using System.Text;
using Discord;

namespace DiscordBotBase
{
    /// <summary>
    /// All data related to a user command.
    /// </summary>
    public class CommandData
    {
        /// <summary>
        /// The original Discord message.
        /// </summary>
        public IUserMessage Message;

        /// <summary>
        /// The raw argument text, split by spaces, after the command base. Includes pings and other data.
        /// </summary>
        public string[] RawArguments;

        /// <summary>
        /// The raw argument text, split by spaces, after the command base. Excludes pings or other special ignorable formats.
        /// </summary>
        public string[] CleanedArguments;

        /// <summary>
        /// Set to 'true' when the command was written like "@Bot commandhere", set to 'false' when the command was written with a prefix like "!commandhere" (or other special execution method).
        /// </summary>
        public bool WasBotMention;
    }
}
