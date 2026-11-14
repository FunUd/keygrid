namespace KeyGrid.Core;

/// <summary>
/// Manages the mapping between keyboard keys and grid column/row indices.
/// Follows the spec sequence: Q W E R T Y U I O P A S D F G H J K L Z X C V B N M (up to 26 keys).
/// </summary>
public static class KeyLayout
{
    public const int MaxKeys = 26;

    /// <summary>
    /// The ordered key sequence specified in spec.md section 3.3.
    /// </summary>
    public static readonly IReadOnlyList<char> KeySequence = new[]
    {
        'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P',
        'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L',
        'Z', 'X', 'C', 'V', 'B', 'N', 'M'
    };

    private static readonly Dictionary<char, int> KeyToIndex = new();

    static KeyLayout()
    {
        for (int i = 0; i < KeySequence.Count; i++)
        {
            KeyToIndex[KeySequence[i]] = i;
            KeyToIndex[char.ToLowerInvariant(KeySequence[i])] = i;
        }
    }

    /// <summary>
    /// Returns the character for the given 0-based index.
    /// </summary>
    public static char GetKey(int index)
    {
        if (index < 0 || index >= KeySequence.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {KeySequence.Count - 1}.");
        }

        return KeySequence[index];
    }

    /// <summary>
    /// Tries to resolve a character to a 0-based index.
    /// </summary>
    public static bool TryGetIndex(char key, out int index)
    {
        return KeyToIndex.TryGetValue(key, out index);
    }

    /// <summary>
    /// Validates whether a key corresponds to an active column or row given the division limit.
    /// </summary>
    public static bool IsValidKeyForCount(char key, int count)
    {
        if (TryGetIndex(key, out int index))
        {
            return index >= 0 && index < count;
        }

        return false;
    }
}
