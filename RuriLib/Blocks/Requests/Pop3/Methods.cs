using MailKit;
using MailKit.Net.Pop3;
using RuriLib.Attributes;
using RuriLib.Functions.Mail;
using RuriLib.Functions.Pop3;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RuriLib.Blocks.Requests.Pop3
{
    [BlockCategory("POP3", "Blocks for working with the POP3 protocol", "#74c365", "#000")]
    public static class Methods
    {
        private static readonly List<string> subdomains = new() { "mail", "pop", "pop3", "m", "pop3-mail", "pop-mail", "inbound", "in", "mx" };

        [Block("Connects to a POP3 server by automatically detecting the host and port")]
        public static async Task Pop3AutoConnect(BotData data, string email, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "pop3LoggerStream", "pop3Logger");

            var client = new Pop3Client(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            await MailAutoConnectHelper.AutoConnectAsync(data, client, email, new MailAutoConnectOptions<Pop3Client>
            {
                ClientObjectKey = "pop3Client",
                LogColor = LogColors.Mantis,
                CandidatePorts = new[] { 995, 110 },
                CandidateSubdomains = subdomains,
                GetKnownServersAsync = data.Providers.EmailDomains.GetPop3Servers,
                CacheConnectedServerAsync = data.Providers.EmailDomains.TryAddPop3Server,
                ParseAutoconfig = Pop3Autoconfig.Parse
            }).ConfigureAwait(false);
        }

        [Block("Connects to a POP3 server")]
        public static async Task Pop3Connect(BotData data, string host, int port, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "pop3LoggerStream", "pop3Logger");

            var client = new Pop3Client(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            data.SetObject("pop3Client", client);

            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.Auto, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log($"Connected to {host} on port {port}. SSL/TLS: {client.IsSecure}", LogColors.Mantis);
        }

        [Block("Disconnects from a POP3 server")]
        public static async Task Pop3Disconnect(BotData data)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);

            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, data.CancellationToken).ConfigureAwait(false);
                data.Logger.Log($"Client disconnected", LogColors.Mantis);
            }
            else
            {
                data.Logger.Log($"The client was not connected", LogColors.Mantis);
            }
        }

        [Block("Logs into an account")]
        public static async Task Pop3Login(BotData data, string email, string password, int timeoutMilliseconds = 10000)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);
            client.AuthenticationMechanisms.Remove("XOAUTH2");

            using var cts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, data.CancellationToken);
            await client.AuthenticateAsync(email, password, linkedCts.Token).ConfigureAwait(false);
            data.Logger.Log($"Authenticated successfully, there are {client.Count} total messages", LogColors.Mantis);
        }

        [Block("Gets the protocol log", name = "Get Pop3 Log")]
        public static string Pop3GetLog(BotData data)
        {
            data.Logger.LogHeader();

            var protocolLogger = data.TryGetObject<ProtocolLogger>("pop3Logger");
            var bytes = (protocolLogger.Stream as MemoryStream).ToArray();
            var log = Encoding.UTF8.GetString(bytes);

            data.Logger.Log(log, LogColors.Mantis);

            return log;
        }

        [Block("Gets a text (or HTML) representation of a mail at a specified index")]
        public static async Task<string> Pop3ReadMail(BotData data, int index, bool preferHtml = false)
        {
            data.Logger.LogHeader();

            var client = GetAuthenticatedClient(data);
            var mail = await client.GetMessageAsync(client.Count - index - 1, data.CancellationToken).ConfigureAwait(false);
            var body = mail.TextBody;

            if (string.IsNullOrEmpty(body) || preferHtml)
            {
                body = mail.HtmlBody;
            }

            var output =
$@"From: {mail.From.First()}
To: {mail.To.First()}
Subject: {mail.Subject}
Body:
{body}";

            data.Logger.Log($"From: {mail.From.First()}", LogColors.Mantis);
            data.Logger.Log($"To: {mail.To.First()}", LogColors.Mantis);
            data.Logger.Log($"Subject: {mail.Subject}", LogColors.Mantis);
            data.Logger.Log("Body:", LogColors.Mantis);
            data.Logger.Log(body, LogColors.Mantis, true);
            return output;
        }

        [Block("Gets a list of all mails in the form From|To|Subject (all if Max Amount is 0) from newest to oldest",
            extraInfo = "Use the Index Of block (in list functions) to get the index of the mail " +
            "you want to read, and pass it to the Pop3 Read Mail block")]
        public static async Task<List<string>> Pop3GetMails(BotData data, int maxAmount = 0)
        {
            data.Logger.LogHeader();
            var client = GetAuthenticatedClient(data);
            var list = new List<string>();
            var currentIndex = 0;

            // Return from newest to oldest
            for (var i = client.Count - 1; i >= (maxAmount <= 0 ? 0 : Math.Max(0, client.Count - maxAmount)); i--)
            {
                var message = await client.GetMessageAsync(i, data.CancellationToken).ConfigureAwait(false);
                var from = message.From.FirstOrDefault()?.Name ?? "None";
                var to = message.To.FirstOrDefault()?.Name ?? "None";
                var subject = message.Subject ?? "None";
                var toWrite = $"{from}|{to}|{subject}";
                data.Logger.Log($"[{currentIndex}] {toWrite}", LogColors.Mantis);
                list.Add(toWrite);
                currentIndex++;
            }

            return list;
        }

        [Block("Deletes a mail", name = "Delete Mail")]
        public static async Task Pop3DeleteMail(BotData data, int index)
        {
            data.Logger.LogHeader();

            var client = GetAuthenticatedClient(data);
            await client.DeleteMessageAsync(index, data.CancellationToken).ConfigureAwait(false);

            data.Logger.Log($"Deleted mail with index {index}", LogColors.Mantis);
        }

        private static Pop3Client GetClient(BotData data)
            => data.TryGetObject<Pop3Client>("pop3Client") ?? throw new Exception("Connect the POP3 client first!");

        private static Pop3Client GetAuthenticatedClient(BotData data)
        {
            var client = GetClient(data);

            if (!client.IsAuthenticated)
            {
                throw new Exception("Authenticate the POP3 client first!");
            }

            return client;
        }

    }
}
