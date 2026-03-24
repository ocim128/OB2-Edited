using System;

namespace RuriLib.Functions.Puppeteer
{
    public static class SharedElementHelpers
    {
        public static string GetElementsScript(FindElementBy findBy, string identifier)
        {
            if (findBy == FindElementBy.XPath)
            {
                var script = $"document.evaluate(\"{identifier.Replace("\"", "\\\"")}\", document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null)";
                return $"Array.from({{ length: {script}.snapshotLength }}, (_, index) => {script}.snapshotItem(index))";
            }

            return $"document.querySelectorAll('{BuildSelector(findBy, identifier)}')";
        }

        public static string GetElementScript(FindElementBy findBy, string identifier, int index)
            => findBy == FindElementBy.XPath
            ? $"document.evaluate(\"{identifier.Replace("\"", "\\\"")}\", document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue"
            : $"document.querySelectorAll('{BuildSelector(findBy, identifier)}')[{index}]";

        public static string BuildSelector(FindElementBy findBy, string identifier)
            => findBy switch
            {
                FindElementBy.Id => '#' + identifier,
                FindElementBy.ClassName => '.' + string.Join('.', identifier.Split(' ')),
                FindElementBy.CssSelector => identifier,
                FindElementBy.Selector => identifier,
                _ => throw new NotSupportedException()
            };
    }
}
