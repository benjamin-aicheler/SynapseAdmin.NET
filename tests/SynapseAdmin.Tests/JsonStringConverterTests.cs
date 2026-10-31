using System.Text.Json;
using SynapseAdmin.Infrastructure.Serialization;
using SynapseAdmin.Models.Responses;
using Xunit;

namespace SynapseAdmin.Tests;

public class JsonStringConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringConverter() }
    };

    [Theory]
    [InlineData("\"token_abc\"", "token_abc")]
    [InlineData("12345", "12345")]
    [InlineData("98.75", "98.75")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", null)]
    public void Read_CoercesPrimitiveTokensToString(string jsonLiteral, string? expected)
    {
        var result = JsonSerializer.Deserialize<string?>(jsonLiteral, Options);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_NumericNextTokenInSynapseResponse_DeserializesAsString()
    {
        const string json = """
        {
            "media": [],
            "total": 100,
            "next_token": 50
        }
        """;

        var parsed = JsonSerializer.Deserialize<SynapseAdminUserMediaResult>(json, Options);

        Assert.NotNull(parsed);
        Assert.Equal("50", parsed.NextToken);
        Assert.Equal(100, parsed.Total);
    }

    [Fact]
    public void Read_UnexpectedObjectOrArrayToken_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string>("[1, 2]", Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<string>("{\"a\":1}", Options));
    }

    [Fact]
    public void Write_OutputsJsonStringLiteral()
    {
        var json = JsonSerializer.Serialize("12345", Options);
        Assert.Equal("\"12345\"", json);
    }
}
