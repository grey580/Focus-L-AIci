namespace FocusLAIci.Web.Models;

public static class SlugUtility
{
    public static string CreateSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var lastWasDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = character;
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            buffer[length++] = '-';
            lastWasDash = true;
        }

        var slug = new string(buffer[..length]).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }
}
