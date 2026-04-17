using System.Text;

namespace Flux.Native.Utils
{
    public class HtmlStyler
    {
        private readonly string html;
        private readonly StringBuilder sb;
        private bool finalized;

        public HtmlStyler(string html)
        {
            this.html = html;
            sb = new StringBuilder();
            sb.Append("<html><head><style> body { ");
        }

        public HtmlStyler WithStyle(string name, string value)
        {
            sb.Append($"{name}: {value}; ");
            return this;
        }

        public override string ToString()
        {
            if (!finalized)
            {
                sb.Append($"}} </style><body>{html}</body></html>");
                finalized = true;
            }
            return sb.ToString();
        }
    }
}
