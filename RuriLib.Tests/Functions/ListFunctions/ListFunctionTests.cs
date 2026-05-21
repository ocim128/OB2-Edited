using RuriLib.Blocks.Functions.List;
using RuriLib.Tests.Functions.Http;

namespace RuriLib.Tests.Functions.ListFunctions;

public class ListFunctionTests
{
    [Fact]
    public void ZipLists_FillsShorterFirstList()
    {
        using var context = new HttpTransportTestContext();

        var result = Methods.ZipLists(
            context.Data,
            ["a"],
            ["1", "2"],
            fill: true,
            fillString: "NULL",
            format: "[0]-[1]");

        Assert.Equal(["a-1", "NULL-2"], result);
    }

    [Fact]
    public void ZipLists_FillsShorterSecondList()
    {
        using var context = new HttpTransportTestContext();

        var result = Methods.ZipLists(
            context.Data,
            ["a", "b"],
            ["1"],
            fill: true,
            fillString: "NULL",
            format: "[0]-[1]");

        Assert.Equal(["a-1", "b-NULL"], result);
    }

    [Fact]
    public void ListToDictionary_SkipsItemsWithoutSeparator()
    {
        using var context = new HttpTransportTestContext();

        var result = Methods.ListToDictionary(
            context.Data,
            ["first:one", "missing-separator", "second:two"]);

        Assert.Equal(2, result.Count);
        Assert.Equal("one", result["first"]);
        Assert.Equal("two", result["second"]);
    }
}
