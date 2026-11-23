using System.ComponentModel;
using System.Globalization;
using Moltaro.OnecOutpost.Agent.Policy;

namespace Moltaro.OnecOutpost.Console.Commands;

// Reads --stop-timeout in the constant form [d.]hh:mm:ss of policy.json, by AgentPolicyReader.TryReadTimeSpan, so
// that the console and the policy accept one form; ServiceSettings checks the range.
internal sealed class StopTimeoutConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string);
    }

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && AgentPolicyReader.TryReadTimeSpan(text, out TimeSpan span))
        {
            return span;
        }

        throw new FormatException(ServiceSettings.StopTimeoutRule);
    }
}
