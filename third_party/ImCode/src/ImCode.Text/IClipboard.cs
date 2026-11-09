namespace ImCode.Text;

public interface IClipboard
{
    string GetText();
    void SetText(string text);
}

public sealed class MemoryClipboard : IClipboard
{
    string _text = "";

    public string GetText() => _text;
    public void SetText(string text) => _text = text;
}
