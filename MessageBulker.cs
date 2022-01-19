using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreneticUtilities.FreneticToolkit;
using Discord;
using Discord.WebSocket;

namespace DiscordBotBase
{
    /// <summary>Helper class to bulk together and send many messages to a single channel over time without hitting the ratelimit as quickly.</summary>
    public class MessageBulker
    {
        /// <summary>The underlying Discord channel.</summary>
        public IMessageChannel Channel;

        /// <summary>The preferred <see cref="AllowedMentions"/> state. Defaults to <see cref="AllowedMentions.None"/>.
        /// This can only be changed per-bulker-instance, not per-message, due to the nature of the bulking operation.</summary>
        public AllowedMentions Mentions = AllowedMentions.None;

        /// <summary>The underlying bot instance.</summary>
        public DiscordBot Bot;

        /// <summary>Struct full of internal data for this object.</summary>
        public struct InternalData
        {
            /// <summary>The bulker instance.</summary>
            public MessageBulker Self;

            /// <summary>Async locker tool.</summary>
            public LockObject Locker;

            /// <summary>The set of to-be-sent messages.</summary>
            public Queue<string> ToSend;

            /// <summary>The tick time of the last message sent, in milliseconds.</summary>
            public long LastSentTicks;

            /// <summary>If true, a task to send a message is scheduled.</summary>
            public bool HasSendTaskActive;

            /// <summary>The internal method that gets scheduled for later when scheduling a delayed send task.</summary>
            public static void WaitingTask(object _self)
            {
                MessageBulker self = _self as MessageBulker;
                Task.Delay(400).Wait();
                lock (self.Internal.Locker)
                {
                    self.Internal.HasSendTaskActive = false;
                    try
                    {
                        self.Internal.DoSend();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"MessageBulker encountered exception while sending message: {ex}");
                    }
                }
            }

            /// <summary>The internal path to schedule a send task.</summary>
            public void ScheduleTask()
            {
                lock (Locker)
                {
                    if (HasSendTaskActive)
                    {
                        return;
                    }
                    HasSendTaskActive = true;
                }
                new Thread(new ParameterizedThreadStart(WaitingTask)).Start(Self); // Not using tasks due to C# being janky around tasks with structs
            }

            /// <summary>The internal direct send-to-channel message. Do not call directly, use <see cref="Send(string)"/>.</summary>
            public void DoSend()
            {
                StringBuilder output = new();
                lock (Locker)
                {
                    while (ToSend.Any())
                    {
                        string next = ToSend.Peek();
                        if (output.Length + next.Length > 1800 && output.Length > 0)
                        {
                            break;
                        }
                        if (output.Length > 0)
                        {
                            output.Append('\n');
                        }
                        output.Append(next);
                        ToSend.Dequeue();
                    }
                    if (ToSend.Any())
                    {
                        ScheduleTask();
                    }
                    if (output.Length == 0)
                    {
                        return;
                    }
                    LastSentTicks = Environment.TickCount64;
                }
                MessageBulker self = Self;
                Task.Factory.StartNew(() => // TODO: Does this need to be swapped to Thread as well?
                {
                    try
                    {
                        self.Channel.SendMessageAsync(output.ToString(), allowedMentions: self.Mentions).Wait();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"MessageBulker encountered error while sending message to channel {self.Channel.Id}: {ex}");
                    }
                });
            }
        }

        /// <summary>Internal data for this object.</summary>
        public InternalData Internal = new() { Locker = new(), ToSend = new(), LastSentTicks = 0, HasSendTaskActive = false };


        /// <summary>Initializes a new bulker instance.</summary>
        public MessageBulker(DiscordBot _bot, IMessageChannel _channel)
        {
            Bot = _bot;
            Channel = _channel;
            Internal.Self = this;
        }

        /// <summary>Sends a message to the channel - either immediately, or in a bulk message later. Will cause delay.</summary>
        public void Send(string text)
        {
            lock (Internal.Locker)
            {
                Internal.ToSend.Enqueue(text);
                if (!Internal.HasSendTaskActive && (Internal.LastSentTicks <= 0 || Internal.LastSentTicks + 1500 < Environment.TickCount64 || text.Length > 1800))
                {
                    Internal.DoSend();
                }
                else
                {
                    Internal.ScheduleTask();
                }
            }
        }
    }
}
