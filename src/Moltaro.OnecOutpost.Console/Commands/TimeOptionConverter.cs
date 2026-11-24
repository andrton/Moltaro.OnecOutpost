using System.ComponentModel;
using System.Globalization;
using Moltaro.OnecOutpost.Agent.Policy;

namespace Moltaro.OnecOutpost.Console.Commands;

// Reads an option of time in the constant form [d.]hh:mm:ss of policy.json, by AgentPolicyReader.TryReadTimeSpan, so
// that the console and the policy accept one form; a value in another form is refused with the rule of the option,
// which never repeats the value given. The settings of the option check its range.
internal abstract class TimeOptionConverter(string rule) : TypeConverter
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

        throw new FormatException(rule);
    }
}
