using RuriLib.Helpers;
using System.Threading.Tasks;

namespace RuriLib.Models.Hits.HitOutputs
{
    public class TelegramBotHitOutput : IHitOutput
    {
        public string Token { get; set; }
        public long ChatId { get; set; }
        public bool OnlyHits { get; set; }

        public TelegramBotHitOutput(string token, long chatId, bool onlyHits = true)
        {
            Token = token;
            ChatId = chatId;
            OnlyHits = onlyHits;
        }

        public async Task Store(Hit hit)
        {
            if (OnlyHits && hit.Type != "SUCCESS")
            {
                return;
            }

            await SimpleHttpClient.SendTelegramMessageAsync(Token, ChatId, hit.ToString());
        }
    }
}
