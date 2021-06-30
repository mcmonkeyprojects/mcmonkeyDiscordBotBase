using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;
using DiscordBotBase.Reactables;
using FreneticUtilities.FreneticToolkit;

namespace DiscordBotBase.CommandHandlers
{
    /// <summary>
    /// Abstract base class for commands that users can run.
    /// </summary>
    public abstract class UserCommands
    {
        /// <summary>
        /// Prefix for when the bot successfully handles user input.
        /// </summary>
        public const string SUCCESS_PREFIX = "+++ ";

        /// <summary>
        /// Prefix for when the bot refuses user input.
        /// </summary>
        public const string REFUSAL_PREFIX = "--- ";

        /// <summary>
        /// The backing bot instance.
        /// </summary>
        public DiscordBot Bot;

        /// <summary>
        /// Sends a reply to a message in the same channel.
        /// </summary>
        /// <param name="message">The message to reply to.</param>
        /// <param name="embed">The embed message to send.</param>
        public static IUserMessage SendReply(IUserMessage message, Embed embed)
        {
            return message.ReplyAsync(embed: embed, allowedMentions: AllowedMentions.None).Result;
        }

        /// <summary>
        /// Sends a "did you mean ...?" style reply. This adds a clickable reaction that triggers an automatic command when clicked by the user that originally did the command within a certain time limit.
        /// </summary>
        /// <param name="message">The message to reply to.</param>
        /// <param name="title">The message title.</param>
        /// <param name="description">The message description.</param>
        /// <param name="command">The bot command to imitate if the reaction is clicked.</param>
        public static IUserMessage SendDidYouMeanReply(IUserMessage message, string title, string description, string command)
        {
            IUserMessage sentMessage = message.Channel.SendMessageAsync(embed: GetGenericPositiveMessageEmbed(title, description)).Result;
            if (sentMessage != null)
            {
                sentMessage.AddReactionsAsync(new IEmote[] { new Emoji(Constants.ACCEPT_EMOJI), new Emoji(Constants.DENY_EMOJI) }).Wait();
                ReactionsHandler.AddReactable(message, sentMessage, command);
            }
            return sentMessage;
        }

        /// <summary>
        /// Sends a generic positive reply to a message in the same channel.
        /// </summary>
        public static IUserMessage SendGenericPositiveMessageReply(IUserMessage message, string title, string description)
        {
            return SendReply(message, GetGenericPositiveMessageEmbed(title, description));
        }

        /// <summary>
        /// Sends a generic negative reply to a message in the same channel.
        /// </summary>
        public static IUserMessage SendGenericNegativeMessageReply(IUserMessage message, string title, string description)
        {
            return SendReply(message, GetGenericNegativeMessageEmbed(title, description));
        }

        /// <summary>
        /// Sends an error message reply to a message in the same channel.
        /// </summary>
        public static IUserMessage SendErrorMessageReply(IUserMessage message, string title, string description)
        {
            return SendReply(message, GetErrorMessageEmbed(title, description));
        }

        /// <summary>
        /// Creates an Embed object for a generic positive message.
        /// </summary>
        public static Embed GetGenericPositiveMessageEmbed(string title, string description)
        {
            return new EmbedBuilder().WithTitle(title).WithColor(0, 255, 255).WithDescription(description).Build();
        }

        /// <summary>
        /// Creates an Embed object for a generic negative message.
        /// </summary>
        public static Embed GetGenericNegativeMessageEmbed(string title, string description)
        {
            return new EmbedBuilder().WithTitle(title).WithColor(255, 128, 0).WithDescription(description).Build();
        }

        /// <summary>
        /// Creates an Embed object for an error message.
        /// </summary>
        public static Embed GetErrorMessageEmbed(string title, string description)
        {
            return new EmbedBuilder().WithTitle(title).WithColor(255, 64, 32).WithThumbnailUrl(Constants.WARNING_ICON).WithDescription(description).Build();
        }

        public static AsciiMatcher NeedsEscapeMatcher = new AsciiMatcher("\\`<*_|:~");

        /// <summary>
        /// Escapes user input for output. Best when wrapped in `backticks`.
        /// </summary>
        /// <param name="text">The user input text.</param>
        /// <returns>The escaped result.</returns>
        public static string EscapeUserInput(string text)
        {
            text = text.Replace("discord.gg", "discord\x00B7gg");
            if (NeedsEscapeMatcher.ContainsAnyMatch(text))
            {
                text = text.Replace('\\', '/').Replace('`', '\'').Replace('<', '\x3008').Replace(':', '\xFF1A').Replace('*', '\x2731').Replace('_', '\xFF3F').Replace('|', '\xFF5C').Replace('~', '\x223C');
            }
            return text;
        }

        /// <summary>
        /// Generates a link to a Discord message.
        /// </summary>
        public static string LinkToMessage(IMessage message)
        {
            return "https://discordapp.com/channels/" + (message.Channel as SocketGuildChannel).Guild.Id + "/" + message.Channel.Id + "/" + message.Id;
        }
    }
}
