using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Discord;

namespace DiscordBotBase.CommandHandlers
{
    /// <summary>Commands that most bots need.</summary>
    public class CoreCommands(Func<IUser, bool> isUserAdmin) : UserCommands
    {
        /// <summary>Method to check if the user is an admin.</summary>
        public Func<IUser, bool> UserAdminCheckMethod = isUserAdmin;

        /// <summary>Bot restart admin command.</summary>
        public void CMD_Restart(CommandData command)
        {
            if (!UserAdminCheckMethod(command.Message.Author))
            {
                SendErrorMessageReply(command.Message, "Authorization Failure", "Nope! That's not for you!");
                return;
            }
            SendGenericPositiveMessageReply(command.Message, "Restarting", "Yes, boss. Restarting now...");
            Task.Factory.StartNew(() =>
            {
                Task.Delay(1000).Wait();
                Console.WriteLine("Shutdown!");
                Environment.Exit(42);
            });
            Bot.BotMonitor.StopAllLogic = true;
            Bot.Shutdown();
        }
    }
}
