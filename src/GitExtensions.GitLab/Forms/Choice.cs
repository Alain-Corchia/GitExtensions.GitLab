namespace GitExtensions.GitLab.Forms;

/// <summary>
///  Combo box item: a value displayed with a text.
/// </summary>
internal sealed class Choice<T>(T value, string text)
{
    public T Value { get; } = value;

    public override string ToString() => text;
}
