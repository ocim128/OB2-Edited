using MailKit.Net.Smtp;
using RuriLib.Attributes;
using RuriLib.Functions.Mail;
using RuriLib.Functions.Smtp;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MimeKit;
using System.Linq;
using RuriLib.Extensions;
using MailKit;
using System.Text;
using System.Threading;

namespace RuriLib.Blocks.Requests.Smtp
{
    [BlockCategory("SMTP", "Blocks for working with the SMTP protocol", "#b5651d", "#fff")]
    public static class Methods
    {
        private static readonly List<string> subdomains = new() { "mail", "smtp-mail", "outbound", "out", "mx", "smtp", "smtps", "m" };
        
        [Block("Connects to a SMTP server by automatically detecting the host and port")]
        public static async Task SmtpAutoConnect(BotData data, string email, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "smtpLoggerStream", "smtpLogger");

            var client = new SmtpClient(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            await MailAutoConnectHelper.AutoConnectAsync(data, client, email, new MailAutoConnectOptions<SmtpClient>
            {
                ClientObjectKey = "smtpClient",
                LogColor = LogColors.LightBrown,
                CandidatePorts = new[] { 465, 587, 25 },
                CandidateSubdomains = subdomains,
                GetKnownServersAsync = data.Providers.EmailDomains.GetSmtpServers,
                CacheConnectedServerAsync = data.Providers.EmailDomains.TryAddSmtpServer,
                ParseAutoconfig = SmtpAutoconfig.Parse,
                ValidateConnectionAsync = connectedClient =>
                {
                    if (connectedClient.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                    {
                        return Task.FromResult(true);
                    }

                    data.Logger.Log("Server doesn't support authentication, trying another one...");
                    return Task.FromResult(false);
                }
            }).ConfigureAwait(false);
        }

        [Block("Connects to a SMTP server")]
        public static async Task SmtpConnect(BotData data, string host, int port, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "smtpLoggerStream", "smtpLogger");

            var client = new SmtpClient(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            data.SetObject("smtpClient", client);

            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.Auto, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log($"Connected to {host} on port {port}. SSL/TLS: {client.IsSecure}", LogColors.LightBrown);
        }

        [Block("Disconnects from a SMTP server")]
        public static async Task SmtpDisconnect(BotData data)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);

            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, data.CancellationToken).ConfigureAwait(false);
                data.Logger.Log($"Client disconnected", LogColors.LightBrown);
            }
            else
            {
                data.Logger.Log($"The client was not connected", LogColors.LightBrown);
            }
        }

        [Block("Logs into an account")]
        public static async Task SmtpLogin(BotData data, string email, string password, int timeoutMilliseconds = 10000)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);
            using var logger = client.ProtocolLogger;
            client.AuthenticationMechanisms.Remove("XOAUTH2");

            using var cts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, data.CancellationToken);
            await client.AuthenticateAsync(email, password, linkedCts.Token).ConfigureAwait(false);
            data.Logger.Log("Authenticated successfully", LogColors.LightBrown);
        }

        [Block("Gets the protocol log", name = "Get Smtp Log")]
        public static string SmtpGetLog(BotData data)
        {
            data.Logger.LogHeader();

            var protocolLogger = data.TryGetObject<ProtocolLogger>("smtpLogger");
            var bytes = (protocolLogger.Stream as MemoryStream).ToArray();
            var log = Encoding.UTF8.GetString(bytes);

            data.Logger.Log(log, LogColors.LightBrown);

            return log;
        }

        [Block("Sends a mail to the recipient")]
        public static async Task SmtpSendMail(BotData data, string senderName, string senderAddress,
            string recipientName, string recipientAddress, string subject, string textBody, string htmlBody)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderAddress));
            message.To.Add(new MailboxAddress(recipientName, recipientAddress));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody.Unescape(),
                TextBody = textBody.Unescape()
            };

            message.Body = bodyBuilder.ToMessageBody();

            await client.SendAsync(message, data.CancellationToken).ConfigureAwait(false);

            data.Logger.Log($"Email sent to {recipientAddress} ({recipientName})", LogColors.LightBrown);
        }

        [Block("Sends a mail in advanced mode", name = "Smtp Send Mail (Advanced)", 
            extraInfo = "Senders/Recipients in the format name: address. For attachments, path to one file per line.")]
        public static async Task SmtpSendMailAdvanced(BotData data, Dictionary<string, string> senders,
            Dictionary<string, string> recipients, string subject, string textBody, string htmlBody,
            Dictionary<string, string> customHeaders, List<string> fileAttachments)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);

            var message = new MimeMessage();
            message.From.AddRange(senders.Select(s => new MailboxAddress(s.Key, s.Value)));
            message.To.AddRange(recipients.Select(r => new MailboxAddress(r.Key, r.Value)));
            message.Subject = subject;

            foreach (var header in customHeaders)
            {
                message.Headers.Add(header.Key, header.Value);
            }

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody.Unescape(),
                TextBody = textBody.Unescape()
            };

            foreach (var file in fileAttachments)
            {
                await bodyBuilder.Attachments.AddAsync(file, data.CancellationToken).ConfigureAwait(false);
            }

            message.Body = bodyBuilder.ToMessageBody();

            await client.SendAsync(message, data.CancellationToken).ConfigureAwait(false);

            data.Logger.Log($"Email sent to {recipients.Count} recipients", LogColors.LightBrown);
        }

        private static SmtpClient GetClient(BotData data)
            => data.TryGetObject<SmtpClient>("smtpClient") ?? throw new Exception("Connect the SMTP client first!");

    }
}
