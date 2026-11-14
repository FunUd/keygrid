using KeyGrid.Core;
using Xunit;

namespace KeyGrid.Tests;

public class KeyLayoutTests
{
    [Fact]
    public void KeySequence_MatchesSpecExactly()
    {
        string expected = "QWERTYUIOPASDFGHJKLZXCVBNM";
        Assert.Equal(26, KeyLayout.KeySequence.Count);
        string actual = new string(KeyLayout.KeySequence.ToArray());
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData('Q', 0)]
    [InlineData('q', 0)]
    [InlineData('W', 1)]
    [InlineData('P', 9)]
    [InlineData('A', 10)]
    [InlineData('M', 25)]
    [InlineData('m', 25)]
    public void TryGetIndex_ResolvesIndicesCorrectlyAndCaseInsensitively(char key, int expectedIndex)
    {
        Assert.True(KeyLayout.TryGetIndex(key, out int index));
        Assert.Equal(expectedIndex, index);
    }

    [Theory]
    [InlineData('1')]
    [InlineData(' ')]
    [InlineData('\n')]
    [InlineData('?')]
    public void TryGetIndex_ReturnsFalseForNonGridKeys(char key)
    {
        Assert.False(KeyLayout.TryGetIndex(key, out _));
    }

    [Theory]
    [InlineData('Q', 14, true)]
    [InlineData('F', 14, true)] // 14th key is 'F' (index 13)
    [InlineData('G', 14, false)] // 15th key is 'G' (index 14)
    public void IsValidKeyForCount_RespectsDivisionBounds(char key, int count, bool expectedValid)
    {
        Assert.Equal(expectedValid, KeyLayout.IsValidKeyForCount(key, count));
    }
}
