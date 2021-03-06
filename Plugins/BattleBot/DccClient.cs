﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using CBot;
using AnIRC;

namespace BattleBot {
    internal class DccClient : IrcClient {
        protected BattleBotPlugin plugin;
        protected internal IrcUser Target;

        private TcpClient client;
        private StreamWriter writer;
        private byte[] buffer;
        private StringBuilder messageBuilder;

        internal DccClient(BattleBotPlugin plugin) : base(plugin.ArenaConnection.Me) {
            this.plugin = plugin;
            this.Address = "!" + plugin.Key + ".DCC";
        }

        public override void Connect(string host, int port) {
            // Connect to the DCC session.
            this.client = new TcpClient();
            this.client.Connect(host, port);
            this.writer = new StreamWriter(client.GetStream());
            this.buffer = new byte[512];
            this.messageBuilder = new StringBuilder();

            this.buffer = new byte[512];

            Thread readThread = new Thread(this.Read);
            readThread.Start();

            this.LastSpoke = DateTime.Now;
            this.State = IrcClientState.Online;
        }

        public override void Disconnect() {
            client.Close();
        }

        public override void Send(string t) {
            if (this.State != IrcClientState.Online) return;
            ConsoleUtils.WriteLine("%cDKGRAY{0} %cDKRED<<%cDKGRAY {1}%r", this.Address, t.Replace("%", "%%"));

            var line = IrcLine.Parse(t);

            if ((line.Message.Equals("PRIVMSG", StringComparison.OrdinalIgnoreCase) || line.Message.Equals("NOTICE", StringComparison.OrdinalIgnoreCase)) && (line.Parameters[0] == "#" ||
                IrcStringComparer.RFC1459.Equals("#Lobby", line.Parameters[0]) ||
                IrcStringComparer.RFC1459.Equals("#BattleRoom", line.Parameters[0]) ||
                IrcStringComparer.RFC1459.Equals(this.Target.Nickname, line.Parameters[0]))) {
                // Emulate a channel message or PM to the target by sending it over DCC.
                this.SendSub(line.Parameters[1].Replace("\u000F", "\u000F\u000312,99"));
            }
        }

        public void SendSub(string t) {
            writer.Write(t);
            writer.Write("\r\n");
            writer.Flush();
        }

        private void Read() {
            int n;
            while (true) {
                try {
                    n = client.GetStream().Read(buffer, 0, 512);
                } catch (IOException ex) {
                    this.OnDisconnect(ex.Message);
                    return;
                }
                if (n < 1) {
                    this.OnDisconnect("The server closed the connection.");
                    return;
                }
                for (int i = 0; i < n; ++i) {
                    if (buffer[i] == 13 || buffer[i] == 10) {
                        if (this.messageBuilder.Length > 0) {
                            try {
                                this.DCCReceivedLine(this.messageBuilder.ToString());
                            } catch (Exception ex) {
                                this.plugin.LogError("DCCClient.ReceivedLine", ex);
                            }
                            this.messageBuilder.Clear();
                        }
                    } else {
                        messageBuilder.Append((char) buffer[i]);
                    }
                }
            }
        }

        protected void DCCReceivedLine(string message) {
            Match match;
            match = Regex.Match(message, @"^\x034\[([^\]]*)\] <([^>]*)> \x0312(.*)");
            if (match.Success) {
                // A chat message
                this.ReceivedLine(":" + match.Groups[2].Value + "!*@* PRIVMSG #" + match.Groups[1].Value + " :" + match.Groups[3].Value);
            } else {
                match = Regex.Match(message, @"^\x0313\*\x034\[([^\]]*)\] [^ ]* \x0312 (.*)\x0313\*$");
                if (match.Success) {
                    // A chat action
                    this.ReceivedLine(":" + match.Groups[2].Value + "!*@* PRIVMSG #" + match.Groups[1].Value + " :\u0001ACTION " + match.Groups[3].Value + "\u0001");
                } else {
                    this.ReceivedLine(":" + this.Target.ToString() + " PRIVMSG " + Me.Nickname + " :" + message);
                }
            }
        }

        private void OnDisconnect(string reason) {
            this.plugin.LoggedIn = null;
            this.client = null;
            this.messageBuilder = null;
            this.plugin.WriteLine(1, 4, "DCC connection closed: {0}", reason);
            for (int i = 1; i < Bot.Clients.Count; ++i) {
                if (Bot.Clients[i].Client == this) {
                    Bot.Clients.RemoveAt(i);
                    break;
                }
            }
        }
    }
}
