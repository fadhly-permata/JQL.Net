using Newtonsoft.Json.Linq;

namespace JQL.Net.Core.Utilities;

internal class JTokenComparer : IComparer<JToken?>
{
    public int Compare(JToken? x, JToken? y)
    {
        if (ReferenceEquals(objA: x, objB: y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        if (x is not JValue vx || y is not JValue vy)
            return string.Compare(
                strA: x.ToString(),
                strB: y.ToString(),
                comparisonType: StringComparison.OrdinalIgnoreCase
            );

        var valX = vx.Value;
        var valY = vy.Value;

        switch (valX)
        {
            case null when valY == null:
                return 0;
            case null:
                return -1;
        }

        if (valY == null)
            return 1;

        if (IsNumeric(obj: valX) && IsNumeric(obj: valY))
            return Convert.ToDouble(value: valX).CompareTo(value: Convert.ToDouble(value: valY));

        return string.Compare(
            strA: valX.ToString(),
            strB: valY.ToString(),
            comparisonType: StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsNumeric(object obj) =>
        obj
            is sbyte
                or byte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal;
}
