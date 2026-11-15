using KeyGrid.Core;
using Xunit;

namespace KeyGrid.Tests;

public class KeyLayoutTests
{
    [Fact]
    public void Blocks_MatchSpecExactly()
    {
        Assert.Equal("QWERTASDFGZXCVB", new string(KeyLayout.LeftBlock.ToArray()));
        Assert.Equal("YUIOPHJKLNM", new string(KeyLayout.RightBlock.ToArray()));
        Assert.Equal(15, KeyLayout.LeftBlock.Count);
        Assert.Equal(11, KeyLayout.RightBlock.Count);
        Assert.Equal(KeyLayout.MaxKeys, KeyLayout.LeftBlock.Count + KeyLayout.RightBlock.Count);
        Assert.Empty(KeyLayout.LeftBlock.Intersect(KeyLayout.RightBlock));
    }

    [Theory]
    [InlineData(7, 6, "QWERTAS", "YUIOPH")]
    [InlineData(10, 8, "QWERTASDFG", "YUIOPHJK")]
    [InlineData(12, 9, "QWERTASDFGZX", "YUIOPHJKL")]
    [InlineData(14, 11, "QWERTASDFGZXCV", "YUIOPHJKLNM")]
    [InlineData(15, 11, "QWERTASDFGZXCVB", "YUIOPHJKLNM")]
    [InlineData(16, 10, "QWERTASDFGZXCVBY", "UIOPHJKLNM")]
    [InlineData(17, 9, "QWERTASDFGZXCVBYU", "IOPHJKLNM")]
    [InlineData(14, 12, "QWERTASDFGZXCV", "YUIOPHJKLNMB")]
    [InlineData(25, 1, "QWERTASDFGZXCVBYUIOPHJKLN", "M")]
    [InlineData(1, 25, "Q", "YUIOPHJKLNMWERTASDFGZXCVB")]
    public void Allocate_MatchesSpecifiedBlocksAndBorrowing(
        int columnCount,
        int rowCount,
        string expectedColumns,
        string expectedRows)
    {
        var allocation = KeyLayout.Allocate(columnCount, rowCount);

        Assert.Equal(expectedColumns, new string(allocation.ColumnKeys.ToArray()));
        Assert.Equal(expectedRows, new string(allocation.RowKeys.ToArray()));
    }

    [Fact]
    public void Allocation_ResolvesKeysCaseInsensitivelyAndRejectsCrossSetKeys()
    {
        var allocation = KeyLayout.Allocate(10, 8);

        Assert.True(allocation.TryGetColumnIndex('q', out int columnIndex));
        Assert.Equal(0, columnIndex);
        Assert.True(allocation.TryGetRowIndex('y', out int rowIndex));
        Assert.Equal(0, rowIndex);

        // At 10x8, Q is only a column key and Y is only a row key.
        Assert.False(allocation.TryGetRowIndex('Q', out _));
        Assert.False(allocation.TryGetColumnIndex('Y', out _));
    }

    [Fact]
    public void Allocate_AllValidPairs_ProduceDisjointDeterministicSets()
    {
        var allKeys = KeyLayout.LeftBlock.Concat(KeyLayout.RightBlock).ToHashSet();

        for (int columns = 1; columns < KeyLayout.MaxKeys; columns++)
        {
            for (int rows = 1; columns + rows <= KeyLayout.MaxKeys; rows++)
            {
                var allocation = KeyLayout.Allocate(columns, rows);
                var repeated = KeyLayout.Allocate(columns, rows);

                Assert.Equal(columns, allocation.ColumnKeys.Count);
                Assert.Equal(rows, allocation.RowKeys.Count);
                Assert.Equal(allocation.ColumnKeys, repeated.ColumnKeys);
                Assert.Equal(allocation.RowKeys, repeated.RowKeys);
                Assert.Empty(allocation.ColumnKeys.Intersect(allocation.RowKeys));
                Assert.All(allocation.ColumnKeys, key => Assert.Contains(key, allKeys));
                Assert.All(allocation.RowKeys, key => Assert.Contains(key, allKeys));
                Assert.Equal(columns + rows, allocation.ColumnKeys.Concat(allocation.RowKeys).Distinct().Count());
            }
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(15, 12)]
    [InlineData(26, 1)]
    public void Allocate_InvalidCounts_Throws(int columnCount, int rowCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyLayout.Allocate(columnCount, rowCount));
    }
}
