using RuriLib.Helpers;
using System.Threading.Tasks;

namespace RuriLib.Models.Hits.HitOutputs;

public class DiscordWebhookHitOutput(string webhook, string username = "", string avatarUrl = "", bool onlyHits = true) : IHitOutput
{
    public string Webhook { get; set; } = webhook;
    public string Username { get; set; } = username;
    public string AvatarUrl { get; set; } = avatarUrl;
    public bool OnlyHits { get; set; } = onlyHits;

    public async Task Store(Hit hit)
    {
        if (OnlyHits && hit.Type != "SUCCESS")
        {
            return;
        }

        await SimpleHttpClient.PostToDiscordAsync(
            Webhook, 
            hit.ToString(), 
            string.IsNullOrWhiteSpace(Username) ? null : Username,
            string.IsNullOrWhiteSpace(AvatarUrl) ? null : AvatarUrl);
    }
}
