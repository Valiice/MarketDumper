using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MarketDumper.Services;

public static partial class RetainerRowVerifier
{
    // A row matches when it names the item and contains the exact quantity as a
    // standalone number token. Prices can contain the same digits ("1,348" splits
    // into 1 and 348), so a false accept is possible in pathological cases — this
    // is a guard against clicking the wrong row, not a proof of identity.
    public static bool Matches(string rowText, string itemName, int quantity)
    {
        if (string.IsNullOrWhiteSpace(rowText) || string.IsNullOrWhiteSpace(itemName))
            return false;
        var nameIndex = rowText.IndexOf(itemName, StringComparison.OrdinalIgnoreCase);
        if (nameIndex < 0)
            return false;

        // Reject when the name is part of a longer item name ("Iron Ore" inside
        // "Iron Ore Cluster") — an adjacent SEPARATE word means a different item.
        // Directly-glued letters are fine: real rows embed SeString payload bytes
        // hard against the name ("…Levinchrome AethersandIH").
        var end = nameIndex + itemName.Length;
        if (nameIndex >= 2 && rowText[nameIndex - 1] == ' ' && char.IsLetterOrDigit(rowText[nameIndex - 2]))
            return false;
        if (end + 1 < rowText.Length && rowText[end] == ' ' && char.IsLetterOrDigit(rowText[end + 1]))
            return false;

        // Strip the item name out before tokenizing so digits embedded in the name
        // (e.g. "Grade 2 Shroud Soil") can't masquerade as the quantity token.
        var remainder = rowText.Remove(nameIndex, itemName.Length);

        return NonDigitRegex().Split(remainder)
            .Any(t => t.Length > 0 && int.TryParse(t, out var q) && q == quantity);
    }

    [GeneratedRegex(@"\D+")]
    private static partial Regex NonDigitRegex();
}
