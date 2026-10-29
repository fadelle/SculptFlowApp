using System.Collections;

namespace Omni.App.Common.Exceptions;

/// <summary>One channel's automatic reply can't be saved (IAutoReplyService.SaveAllAsync): wraps the validation's
/// ArgumentException (same text key and arguments) and says which <see cref="Channel"/>, so the page can open that tab.</summary>
public class AutoReplyInvalidException : ArgumentException
{
    public AutoReplyInvalidException(ArgumentException reason, string channel) : base(reason.Message, reason)
    {
        Channel = channel;
        foreach (DictionaryEntry entry in reason.Data) Data[entry.Key] = entry.Value; // the text arguments (TextError.WithArgs)
    }

    public string Channel { get; }
}
