using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using RuriLib.Attributes;
using RuriLib.Functions.Imap;
using RuriLib.Functions.Mail;
using RuriLib.Logging;
using RuriLib.Models.Bots;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RuriLib.Extensions;
using static RuriLib.Functions.Time.TimeConverter;

namespace RuriLib.Blocks.Requests.Imap
{
    [BlockCategory("IMAP", "Blocks for working with the IMAP protocol", "#93c", "#fff")]
    public static class Methods
    {
        private static readonly List<string> subdomains = new() { "mail", "imap-mail", "inbound", "in", "mx", "imap", "imaps", "m" };

        [Block("Connects to an IMAP server by automatically detecting the host and port")]
        public static async Task ImapAutoConnect(BotData data, string email, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "imapLoggerStream", "imapLogger");

            var client = new ImapClient(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            await MailAutoConnectHelper.AutoConnectAsync(data, client, email, new MailAutoConnectOptions<ImapClient>
            {
                ClientObjectKey = "imapClient",
                LogColor = LogColors.DarkOrchid,
                CandidatePorts = new[] { 993, 143 },
                CandidateSubdomains = subdomains,
                GetKnownServersAsync = data.Providers.EmailDomains.GetImapServers,
                CacheConnectedServerAsync = data.Providers.EmailDomains.TryAddImapServer,
                ParseAutoconfig = ImapAutoconfig.Parse
            }).ConfigureAwait(false);
        }

        [Block("Connects to an IMAP server")]
        public static async Task ImapConnect(BotData data, string host, int port, int timeoutMilliseconds = 60000)
        {
            data.Logger.LogHeader();

            var protocolLogger = MailAutoConnectHelper.InitLogger(data, "imapLoggerStream", "imapLogger");

            var client = new ImapClient(protocolLogger)
            {
                Timeout = timeoutMilliseconds,
                ServerCertificateValidationCallback = (s, c, h, e) => true
            };

            if (data.UseProxy && data.Proxy != null)
            {
                client.ProxyClient = MailAutoConnectHelper.MapProxyClient(data);
            }

            data.SetObject("imapClient", client);

            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.Auto, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log($"Connected to {host} on port {port}. SSL/TLS: {client.IsSecure}", LogColors.DarkOrchid);
        }

        [Block("Disconnects from an IMAP server")]
        public static async Task ImapDisconnect(BotData data)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);

            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, data.CancellationToken).ConfigureAwait(false);
                data.Logger.Log($"Client disconnected", LogColors.DarkOrchid);
            }
            else
            {
                data.Logger.Log($"The client was not connected", LogColors.DarkOrchid);
            }
        }

        [Block("Logs into an account")]
        public static async Task ImapLogin(BotData data, string email, string password, bool openInbox = true, int timeoutMilliseconds = 10000)
        {
            data.Logger.LogHeader();

            var client = GetClient(data);
            client.AuthenticationMechanisms.Remove("XOAUTH2");

            using var cts = new CancellationTokenSource(timeoutMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, data.CancellationToken);
            await client.AuthenticateAsync(email, password, linkedCts.Token).ConfigureAwait(false);
            data.Logger.Log("Authenticated successfully", LogColors.DarkOrchid);

            if (openInbox)
            {
                await client.Inbox.OpenAsync(FolderAccess.ReadWrite, data.CancellationToken).ConfigureAwait(false);
                SetCurrentFolder(data, client.Inbox);
                data.Logger.Log($"Opened the inbox, there are {client.Inbox.Count} total messages", LogColors.DarkOrchid);
            }
        }

        [Block("Gets the protocol log", name = "Get Imap Log")]
        public static string ImapGetLog(BotData data)
        {
            data.Logger.LogHeader();

            var protocolLogger = data.TryGetObject<ProtocolLogger>("imapLogger");
            var bytes = (protocolLogger.Stream as MemoryStream)!.ToArray();
            var log = Encoding.UTF8.GetString(bytes);

            data.Logger.Log(log, LogColors.DarkOrchid);

            return log;
        }

        [Block("Opens the inbox folder")]
        public static async Task ImapOpenInbox(BotData data)
        {
            data.Logger.LogHeader();

            var client = GetAuthenticatedClient(data);
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite, data.CancellationToken).ConfigureAwait(false);

            SetCurrentFolder(data, client.Inbox);
            
            data.Logger.Log($"Opened the inbox, there are {client.Inbox.Count} total messages", LogColors.DarkOrchid);
        }

        [Block("Searches for mails", extraInfo = "The 'delivered after' expects a Unix timestamp (UTC) in seconds.")]
        public static async Task<List<string>> ImapSearchMails(BotData data, SearchField field1 = SearchField.Subject, string text1 = "",
            SearchField field2 = SearchField.From, string text2 = "", int deliveredAfter = 1)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);

            if (!folder.IsOpen)
            {
                await folder.OpenAsync(FolderAccess.ReadWrite, data.CancellationToken).ConfigureAwait(false);
            }

            SearchQuery query = new DateSearchQuery(SearchTerm.DeliveredAfter, ((long)deliveredAfter).ToDateTimeUtc());

            if (!string.IsNullOrEmpty(text1))
            {
                query = query.And(new TextSearchQuery(MapSearchTerm(field1), text1));
            }

            if (!string.IsNullOrEmpty(text2))
            {
                query = query.And(new TextSearchQuery(MapSearchTerm(field2), text2));
            }

            IList<UniqueId> mails = null;

            try
            {
                mails = await folder.SearchAsync(query, data.CancellationToken).ConfigureAwait(false);
            }
            catch
            {
                data.Logger.Log("Search denied by the server", LogColors.DarkOrchid);
                return [];
            }

            var ids = mails.Select(id => id.Id.ToString()).ToList();

            data.Logger.Log($"{ids.Count} mails matched the search", LogColors.DarkOrchid);
            data.Logger.Log(ids, LogColors.DarkOrchid);

            return ids;
        }

        [Block("Gets a text (or HTML) representation of a mail")]
        public static async Task<string> ImapReadMail(BotData data, string id, bool preferHtml = false)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);
            var uniqueId = new UniqueId(uint.Parse(id));
            using var mail = await folder.GetMessageAsync(uniqueId, data.CancellationToken).ConfigureAwait(false);

            var body = mail.TextBody;

            if (string.IsNullOrEmpty(body) || preferHtml)
            {
                body = mail.HtmlBody;
            }

            var output =
                $"""
                 From: {mail.From.First()}
                 To: {mail.To.First()}
                 Subject: {mail.Subject}
                 Body:
                 {body}
                 """;

            data.Logger.Log($"From: {mail.From.First()}", LogColors.DarkOrchid);
            data.Logger.Log($"To: {mail.To.First()}", LogColors.DarkOrchid);
            data.Logger.Log($"Subject: {mail.Subject}", LogColors.DarkOrchid);
            data.Logger.Log("Body:", LogColors.DarkOrchid);
            data.Logger.Log(body, LogColors.DarkOrchid, true);
            return output;
        }

        [Block("Gets a mail in EML format")]
        public static async Task<byte[]> ImapReadMailRaw(BotData data, string id)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);
            var uniqueId = new UniqueId(uint.Parse(id));
            using var mail = await folder.GetMessageAsync(uniqueId, data.CancellationToken).ConfigureAwait(false);
            
            using var ms = new MemoryStream();
            await mail.WriteToAsync(ms, data.CancellationToken);
            ms.Seek(0, SeekOrigin.Begin);
            var bytes = ms.ToArray();

            data.Logger.Log($"Received {bytes.Length} bytes", LogColors.DarkOrchid);

            return bytes;
        }

        [Block("Deletes a mail", name = "Imap Delete Mail")]
        public static async Task ImapDeleteMail(BotData data, string id)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);
            var uniqueId = new UniqueId(uint.Parse(id));
            await folder.AddFlagsAsync(uniqueId, MessageFlags.Deleted, true, data.CancellationToken).ConfigureAwait(false);
            await folder.ExpungeAsync(data.CancellationToken).ConfigureAwait(false);

            data.Logger.Log($"Deleted mail with id {id}", LogColors.DarkOrchid);
        }
        
        [Block("Gets a list of folders", name = "Imap List Folders")]
        public static async Task<List<string>> ListFolders(BotData data)
        {
            data.Logger.LogHeader();

            // We always try to get the cached folders first, since it's
            // improbable that they change during the bot's execution
            var folders = data.TryGetObject<List<IMailFolder>>("imapFolders");

            if (folders is null)
            {
                var client = GetAuthenticatedClient(data);
                folders = [];

                foreach (var personalNamespace in client.PersonalNamespaces)
                {
                    try
                    {
                        var foldersInNamespace = await client.GetFoldersAsync(personalNamespace, cancellationToken: data.CancellationToken).ConfigureAwait(false);
                        folders.AddRange(foldersInNamespace.ToList());
                    }
                    catch (ImapCommandException)
                    {
                        data.Logger.Log($"Failed to get folders in namespace {personalNamespace}", LogColors.DarkOrchid);
                    }
                }
                
                data.SetObject("imapFolders", folders);
            }
            
            var folderNames = folders.Select(folder => folder.FullName).ToList();
            data.Logger.Log($"Folders: {folderNames.AsString()}", LogColors.DarkOrchid);
            return folderNames;
        }

        [Block("Opens a folder given its full name", name = "Imap Open Folder")]
        public static async Task<bool> ImapOpenFolder(BotData data, string folderName, FolderAccess folderAccess = FolderAccess.ReadOnly)
        {
            data.Logger.LogHeader();

            var folders = GetFolders(data);
            var folder = folders.Find(f => f.FullName.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Folder '{folderName}' not found");

            await folder.OpenAsync(folderAccess, data.CancellationToken).ConfigureAwait(false);
            data.Logger.Log(folder.IsOpen 
                ? $"Folder '{folder.Name}' is opened (messages: {folder.Count})" 
                : $"Folder '{folder.Name}' isn't opening",
                LogColors.DarkOrchid);

            SetCurrentFolder(data, folder);

            return folder.IsOpen;
        }

        [Block("Close folder", name = "Imap Close Folder")]
        public static async Task ImapCloseFolder(BotData data)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);

            if (folder.IsOpen)
            {
                await folder.CloseAsync();
            }

            SetCurrentFolder(data, null);
            data.Logger.Log($"Folder '{folder.Name}' is closed", LogColors.DarkOrchid);
        }

        [Block("Gets the number of email messages in a folder", name = "Imap Get Mail Count")]
        public static async Task<int> GetMailCount(BotData data)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);

            if (!folder.IsOpen)
            {
                await folder.OpenAsync(FolderAccess.ReadOnly, data.CancellationToken).ConfigureAwait(false);
            }
            
            data.Logger.Log($"Mail count: {folder.Count}", LogColors.DarkOrchid);

            return folder.Count;
        }

        [Block("Gets the id of the last message in the current folder", name = "Imap Get Last Message Id")]
        public static async Task<int> GetLastMessageId(BotData data)
        {
            data.Logger.LogHeader();

            var folder = GetCurrentFolder(data);

            if (!folder.IsOpen)
            {
                await folder.OpenAsync(FolderAccess.ReadWrite, data.CancellationToken).ConfigureAwait(false);
            }

            data.Logger.Log($"Last message Id: {folder.Count - 1}", LogColors.DarkOrchid);

            return folder.Count - 1;
        }

        private static ImapClient GetClient(BotData data)
            => data.TryGetObject<ImapClient>("imapClient") ?? throw new Exception("Connect the IMAP client first!");

        private static ImapClient GetAuthenticatedClient(BotData data)
        {
            var client = GetClient(data);

            if (!client.IsAuthenticated)
            {
                throw new Exception("Authenticate the IMAP client first!");
            }

            return client;
        }

        private static List<IMailFolder> GetFolders(BotData data)
            => data.TryGetObject<List<IMailFolder>>("imapFolders") ?? throw new Exception("Get the list of folders first!");
        
        private static IMailFolder GetCurrentFolder(BotData data)
            => data.TryGetObject<IMailFolder>("imapCurrentFolder") ?? throw new Exception("Open a folder first!");
        
        private static void SetCurrentFolder(BotData data, IMailFolder folder)
            => data.SetObject("imapCurrentFolder", folder);

        private static SearchTerm MapSearchTerm(SearchField field) => field switch
        {
            SearchField.To => SearchTerm.ToContains,
            SearchField.From => SearchTerm.FromContains,
            SearchField.Subject => SearchTerm.SubjectContains,
            SearchField.Body => SearchTerm.BodyContains,
            _ => throw new NotImplementedException()
        };
    }
}
