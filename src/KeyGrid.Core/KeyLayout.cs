namespace KeyGrid.Core;

/// <summary>
/// Column and row key sets for one selection session.
/// </summary>
public sealed class KeyAllocation
{
    public IReadOnlyList<char> ColumnKeys { get; }
    public IReadOnlyList<char> RowKeys { get; }

    internal KeyAllocation(char[] columnKeys, char[] rowKeys)
    {
        ColumnKeys = Array.AsReadOnly(columnKeys);
        RowKeys = Array.AsReadOnly(rowKeys);
    }

    public bool TryGetColumnIndex(char key, out int index)
    {
        return TryGetIndex(ColumnKeys, key, out index);
    }

    public bool TryGetRowIndex(char key, out int index)
    {
        return TryGetIndex(RowKeys, key, out index);
    }

    private static bool TryGetIndex(IReadOnlyList<char> keys, char key, out int index)
    {
        char normalized = char.ToUpperInvariant(key);
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] == normalized)
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }
}

/// <summary>
/// Allocates disjoint column and row key sets from two physical keyboard blocks.
/// The left block is used for columns and the right block for rows, with
/// deterministic borrowing when a dimension overflows its natural block.
/// </summary>
public static class KeyLayout
{
    public const int MaxKeys = 26;

    /// <summary>
    /// Left-hand column keys in physical keyboard order.
    /// </summary>
    public static IReadOnlyList<char> LeftBlock { get; } = Array.AsReadOnly(new[]
    {
        'Q', 'W', 'E', 'R', 'T', 'A', 'S', 'D', 'F', 'G', 'Z', 'X', 'C', 'V', 'B'
    });

    /// <summary>
    /// Right-hand row keys in physical keyboard order.
    /// </summary>
    public static IReadOnlyList<char> RightBlock { get; } = Array.AsReadOnly(new[]
    {
        'Y', 'U', 'I', 'O', 'P', 'H', 'J', 'K', 'L', 'N', 'M'
    });

    /// <summary>
    /// Allocates disjoint column and row keys for the given division counts.
    /// </summary>
    public static KeyAllocation Allocate(int columnCount, int rowCount)
    {
        if (columnCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount), "Column count must be at least one.");
        }

        if (rowCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount), "Row count must be at least one.");
        }

        if ((long)columnCount + rowCount > MaxKeys)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowCount),
                $"Column and row counts must total at most {MaxKeys}.");
        }

        var columns = new char[columnCount];
        int leftColumnCount = Math.Min(columnCount, LeftBlock.Count);
        for (int i = 0; i < leftColumnCount; i++)
        {
            columns[i] = LeftBlock[i];
        }

        for (int i = LeftBlock.Count; i < columnCount; i++)
        {
            columns[i] = RightBlock[i - LeftBlock.Count];
        }

        var rows = new char[rowCount];
        if (columnCount <= LeftBlock.Count)
        {
            int rightRowCount = Math.Min(rowCount, RightBlock.Count);
            for (int i = 0; i < rightRowCount; i++)
            {
                rows[i] = RightBlock[i];
            }

            for (int i = RightBlock.Count; i < rowCount; i++)
            {
                // Columns used only the left prefix, so the remaining left keys are free.
                rows[i] = LeftBlock[columnCount + (i - RightBlock.Count)];
            }
        }
        else
        {
            // Columns borrowed a right-block prefix, so rows use the remaining suffix.
            int rightStart = columnCount - LeftBlock.Count;
            for (int i = 0; i < rowCount; i++)
            {
                rows[i] = RightBlock[rightStart + i];
            }
        }

        return new KeyAllocation(columns, rows);
    }
}
