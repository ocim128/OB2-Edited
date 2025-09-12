using RuriLib.Functions.Networking;
using System.Collections.Generic;
using System.Xml;

namespace RuriLib.Functions
{
    /// <summary>
    /// Base class for email autoconfig parsing that eliminates duplicate code
    /// across SMTP, POP3, and IMAP autoconfig implementations.
    /// </summary>
    public static class EmailAutoconfigBase
    {
        /// <summary>
        /// Parses email autoconfig XML to extract host entries for a specific server type.
        /// </summary>
        /// <param name="xml">The XML configuration string</param>
        /// <param name="serverType">The server type (smtp, pop3, imap)</param>
        /// <param name="isIncoming">Whether this is an incoming server (true for pop3/imap, false for smtp)</param>
        /// <returns>List of host entries</returns>
        public static List<HostEntry> Parse(string xml, string serverType, bool isIncoming)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);

            var serverPath = isIncoming 
                ? $"/clientConfig/emailProvider/incomingServer[contains(@type,'{serverType}')]"
                : $"/clientConfig/emailProvider/outgoingServer[contains(@type,'{serverType}')]";

            var servers = doc.DocumentElement.SelectNodes(serverPath);

            var hosts = new List<HostEntry>();

            foreach (XmlNode server in servers)
            {
                var hostname = server.SelectSingleNode("hostname")?.FirstChild?.Value;
                var portNode = server.SelectSingleNode("port")?.FirstChild?.Value;

                if (!string.IsNullOrEmpty(hostname) && !string.IsNullOrEmpty(portNode) && int.TryParse(portNode, out var port))
                {
                    hosts.Add(new HostEntry(hostname, port));
                }
            }

            return hosts;
        }
    }
}